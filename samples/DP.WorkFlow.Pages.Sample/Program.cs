namespace DP.WorkFlow.Samples.Pages;

internal static class Program
{
    /// <summary>
    /// 入口。数据放在程序目录的 sample-data 下（流程库 flows、草稿、版本历史）；第一次运行时写入几条模拟流程。
    /// 参数 --reset 清空 sample-data 重新生成。
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = Path.Combine(AppContext.BaseDirectory, "sample-data");
        if (args.Contains("--reset", StringComparer.OrdinalIgnoreCase) && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.ToString(), "示例程序异常", MessageBoxButtons.OK, MessageBoxIcon.Error);
        using var host = new SampleWorkflowHost(root);
        SampleFlows.SeedIfEmpty(host);
        host.Load();
        Application.Run(new SampleForm(host));
    }
}
