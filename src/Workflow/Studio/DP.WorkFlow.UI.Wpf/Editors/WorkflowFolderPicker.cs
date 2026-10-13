using System.Windows;

namespace DP.WorkFlow.UI.Wpf;

/// <summary>
/// 选择文件夹。.NET 8 的 WPF 用系统文件夹对话框；4.8 的 WPF 没有它，用打开文件对话框选中文件夹内的占位文件名，取其所在目录。
/// </summary>
public static class WorkflowFolderPicker
{
    /// <summary>显示选择文件夹对话框。</summary>
    /// <param name="owner">所属窗口；可以为空。</param>
    /// <param name="title">对话框标题。</param>
    /// <param name="initialDirectory">初始目录；不存在时由系统决定。</param>
    /// <param name="folder">选中的文件夹。</param>
    /// <returns>用户确认选择时返回 <see langword="true"/>。</returns>
    public static bool TrySelect(Window? owner, string title, string? initialDirectory, out string folder)
    {
        var initial = !string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory) ? initialDirectory! : string.Empty;
#if NETFRAMEWORK
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "选择此文件夹",
            InitialDirectory = initial
        };
        if (dialog.ShowDialog(owner) == true && System.IO.Path.GetDirectoryName(dialog.FileName) is { Length: > 0 } selected)
        {
            folder = selected;
            return true;
        }
#else
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, Multiselect = false, InitialDirectory = initial };
        if (dialog.ShowDialog(owner) == true)
        {
            folder = dialog.FolderName;
            return true;
        }
#endif
        folder = string.Empty;
        return false;
    }
}
