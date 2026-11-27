using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// “流程”页：流程库和流程设计合成一页。进来是流程库；双击流程或点“打开设计”进入设计，
/// 设计命令卡最左边的“‹ 流程库”回到列表（流程名和“已修改”标记就在同一行的路径里）。
/// 回到流程库不会关掉设计里的流程，再打开同一个流程时修改还在；打开别的流程时照常先问要不要放弃修改。
/// </summary>
internal sealed class SampleFlowPage : UserControl
{
    private readonly WorkflowFlowLibraryPage _library;
    private readonly SampleDesignPage _design;

    public SampleFlowPage(WorkflowFlowLibraryPage library, SampleDesignPage design)
    {
        _library = library;
        _design = design;
        _design.AddBackButton("流程库", ShowLibrary);
        _design.Dock = DockStyle.Fill;
        _library.Dock = DockStyle.Fill;
        Controls.Add(_design);
        Controls.Add(_library);
        ShowLibrary();
    }

    public void ShowLibrary()
    {
        // 设计里可能刚保存过草稿：回来时重新列一遍，“有改动”等标记跟着更新。
        if (_design.Visible) _library.Reload();
        _design.Visible = false;
        _library.Visible = true;
    }

    public void ShowDesign()
    {
        _library.Visible = false;
        _design.Visible = true;
    }
}
