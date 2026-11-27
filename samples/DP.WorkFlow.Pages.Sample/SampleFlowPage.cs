using DP.WorkFlow.UI.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Samples.Pages;

/// <summary>
/// “流程”页：流程库和流程设计合成一页。进来是流程库；双击流程或点“打开设计”进入设计，
/// 设计上方一条返回栏：左上角“‹ 流程库”回到列表，旁边是正在编辑的流程名（有没保存的修改时带 *）。
/// 回到流程库不会关掉设计里的流程，再打开同一个流程时修改还在；打开别的流程时照常先问要不要放弃修改。
/// </summary>
internal sealed class SampleFlowPage : UserControl
{
    private readonly WorkflowFlowLibraryPage _library;
    private readonly SampleDesignPage _design;
    private readonly Panel _designView = new() { Dock = DockStyle.Fill };
    private readonly Label _flowName = new();

    public SampleFlowPage(WorkflowFlowLibraryPage library, SampleDesignPage design)
    {
        _library = library;
        _design = design;
        var back = new ModernButton { Text = "流程库", ButtonType = ModernButtonType.Text, Icon = ModernIconKind.ChevronLeft };
        back.Click += (_, _) => ShowLibrary();
        var bar = new WorkflowToolbar();
        bar.Add(back);
        bar.AddSeparator();
        _flowName.Font = new Font(Font, FontStyle.Bold);
        bar.Add(_flowName, 360);
        _design.Dock = DockStyle.Fill;
        _designView.Controls.Add(_design);
        _designView.Controls.Add(new WorkflowBlock { Dock = DockStyle.Top, LogicalExtent = 10 });
        _designView.Controls.Add(bar);
        _library.Dock = DockStyle.Fill;
        Controls.Add(_designView);
        Controls.Add(_library);
        ShowLibrary();
    }

    /// <summary>是否正在看设计（而不是流程库列表）。</summary>
    public bool IsDesigning => _designView.Visible;

    public void ShowLibrary()
    {
        // 设计里可能刚保存过草稿：回来时重新列一遍，“有改动”等标记跟着更新。
        if (_designView.Visible) _library.Reload();
        _designView.Visible = false;
        _library.Visible = true;
    }

    public void ShowDesign()
    {
        RefreshTitle();
        _library.Visible = false;
        _designView.Visible = true;
    }

    /// <summary>刷新返回栏上的流程名（主窗口每秒调用一次，保存、改名后跟着变）。</summary>
    public void RefreshTitle()
    {
        var text = (_design.FlowName ?? "未命名流程") + (_design.IsDirty ? " *" : string.Empty);
        if (_flowName.Text != text) _flowName.Text = text;
    }
}
