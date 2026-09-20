namespace WinFormsApp_test;

internal static class Program
{
    private static int _reportingFatalError;

    /// <summary>应用程序入口。</summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        try
        {
            Application.Run(new Form1());
        }
        finally
        {
            Application.ThreadException -= OnThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e) => ReportFatalError("UI 线程异常", e.Exception);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        ReportFatalError("未处理异常", e.ExceptionObject as Exception ?? new InvalidOperationException(Convert.ToString(e.ExceptionObject) ?? "未知异常"));

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportFatalError("未观察的异步异常", e.Exception);
        e.SetObserved();
    }

    private static void ReportFatalError(string category, Exception exception)
    {
        if (Interlocked.Exchange(ref _reportingFatalError, 1) != 0)
            return;
        try
        {
            var text = $"{DateTimeOffset.Now:O} [{category}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            try
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "WinFormsApp_test.error.log"), text);
            }
            catch (Exception logException) when (logException is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                System.Diagnostics.Debug.WriteLine(logException);
            }
            MessageBox.Show(text, "示例程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Volatile.Write(ref _reportingFatalError, 0);
        }
    }
}
