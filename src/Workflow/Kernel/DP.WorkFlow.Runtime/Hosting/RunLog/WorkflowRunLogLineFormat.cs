using System.Globalization;
using System.Text.Json;

namespace DP.WorkFlow;

/// <summary>
/// 运行日志一行的格式：<c>时间 级别 [流程/位置] 说明</c>，和流程出口推送的文字相同，直接能看；
/// 后面隔一个制表符附上这条事件的 JSON（回放用）。JSON 里没有原样的制表符和换行，所以最后一个制表符后面就是回放数据。
/// </summary>
internal static class WorkflowRunLogLineFormat
{
    private const char Separator = '\t';

    public static string Write(string flowName, WorkflowRunEvent runEvent, string? nodeTitle)
    {
        var text = WorkflowOutlet.FormatLine(flowName, runEvent, nodeTitle).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ')
            .Replace(Separator, ' ');
        return runEvent.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " "
            + WorkflowOutlet.FormatLevel(WorkflowOutlet.GetLevel(runEvent)) + " " + text + Separator + WorkflowRunEventJson.Write(runEvent);
    }

    /// <summary>读出行尾的事件；不是 Workflow 写的行（空行、外部加的注释等）返回空。</summary>
    public static WorkflowRunEvent? Read(string? line)
    {
        var index = line?.LastIndexOf(Separator) ?? -1;
        if (index < 0 || index + 1 >= line!.Length || line[index + 1] != '{') return null;
        try
        {
            return WorkflowRunEventJson.Read(line.Substring(index + 1));
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException or OverflowException)
        {
            return null;
        }
    }
}
