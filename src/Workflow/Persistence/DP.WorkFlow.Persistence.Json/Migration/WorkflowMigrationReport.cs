namespace DP.WorkFlow.Persistence.Json;

/// <summary>
/// 描述旧流程文档迁移期间发生的兼容处理。
/// </summary>
public sealed class WorkflowMigrationReport
{
    private readonly List<string> _warnings = new();

    /// <summary>获取输入文档版本。</summary>
    public int SourceSchemaVersion { get; internal set; }

    /// <summary>获取输出文档版本。</summary>
    public int TargetSchemaVersion { get; internal set; }

    /// <summary>获取迁移警告。</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    internal void AddWarning(string warning) => _warnings.Add(warning);
}

/// <summary>包含加载后的正式工作流文档及迁移报告。</summary>
/// <param name="Document">包含入口、语义图和布局的工作流文档。</param>
/// <param name="Migration">读取旧文档时产生的迁移报告。</param>
public sealed record WorkflowLoadResult(WorkflowDocument Document, WorkflowMigrationReport Migration)
{
    /// <summary>获取当前设计器使用的过渡画布投影。</summary>
    public WorkflowCanvasModel Canvas => Document.CanvasProjection;
}
