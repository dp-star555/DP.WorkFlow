using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ScriptEngine;

/// <summary>
/// 将 Worker 进程树封装进 Windows Job Object。该模块隐藏平台互操作细节，关闭句柄时由内核
/// 终止整个进程树，并可在轮询检测之外强制执行进程内存和活动进程数限制。
/// </summary>
internal sealed class WindowsProcessJob : IDisposable
{
    private const uint JobObjectLimitActiveProcess = 0x00000008;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private readonly JobHandle _handle;

    private WindowsProcessJob(JobHandle handle) => _handle = handle;

    public static WindowsProcessJob? TryAssign(Process process, RoslynScriptWorkerOptions options)
    {
        if (!options.UseWindowsJobObject) return null;
        if (!IsWindows())
        {
            if (options.RequireWindowsJobObject)
                throw new PlatformNotSupportedException("Windows Job Object 仅能在 Windows 上使用。");
            return null;
        }

        JobHandle? handle = null;
        try
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建脚本 Worker Job Object。");

            var limits = new JobObjectExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
            if (options.MaximumProcessCount > 0)
            {
                limits.BasicLimitInformation.LimitFlags |= JobObjectLimitActiveProcess;
                limits.BasicLimitInformation.ActiveProcessLimit = checked((uint)options.MaximumProcessCount);
            }
            if (options.MaximumCommittedMemoryBytes > 0)
            {
                limits.BasicLimitInformation.LimitFlags |= JobObjectLimitProcessMemory;
                limits.ProcessMemoryLimit = new UIntPtr(checked((ulong)options.MaximumCommittedMemoryBytes));
            }

            var length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(handle, 9, buffer, (uint)length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法配置脚本 Worker Job Object。");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法将脚本 Worker 分配到 Job Object。");

            var result = new WindowsProcessJob(handle);
            handle = null;
            return result;
        }
        catch (Exception exception) when (!options.RequireWindowsJobObject
                                          && exception is Win32Exception or PlatformNotSupportedException)
        {
            return null;
        }
        finally
        {
            handle?.Dispose();
        }
    }

    public void Dispose() => _handle.Dispose();

    private static bool IsWindows()
    {
#if NETFRAMEWORK
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern JobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        JobHandle job,
        int informationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);

    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private JobHandle() : base(ownsHandle: true) { }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
