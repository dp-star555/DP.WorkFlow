using System.ComponentModel;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;
using ModernPropertyGrid.WinForms;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>左侧节点属性，右侧原生画布；制作、测试和错误状态固定显示。</summary>
public sealed class VisionTemplateAuthoringRenderer : IWorkflowWinFormsNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionTemplateAuthoringPageModel.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionTemplateAuthoringPageModel);
    /// <inheritdoc/>
    public Control CreateControl(WorkflowNodeEditorPageDescriptor page) => new VisionTemplateWorkspaceControl((VisionTemplateAuthoringPageModel)page.Model);
}

internal sealed class VisionTemplateWorkspaceControl : UserControl
{
    private readonly VisionTemplateAuthoringPageModel _model;
    private readonly VisionFrameEditorControl _frame;
    private readonly ModernPropertyGrid.WinForms.ModernPropertyGrid _properties = new() { Dock = DockStyle.Fill, Theme = ModernUI.WinForms.ModernTheme.Dark, ShowSearchBar = false };
    private readonly Label _buildState = State("TemplateBuildState");
    private readonly Label _testState = State("TemplateTestState");
    private readonly Label _issue = State("TemplateFailure");
    private readonly List<(WorkflowPropertyEntry Entry, ModernUI.WinForms.ModernButton Button)> _actions = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 150 };
    private readonly ToolTip _tips = new();
    private bool _running;
    private string _operation = "";
    private long _revision = -1;
    private IReadOnlyList<VisionTemplateResourceChoice>? _listed;

    internal VisionTemplateWorkspaceControl(VisionTemplateAuthoringPageModel model)
    {
        _model = model; BackColor = Color.FromArgb(30, 30, 30); ForeColor = Color.Gainsboro;
        _frame = new VisionFrameEditorControl(model.Frame, templatePane: false,
            pick: p => { try { model.Pick(p); RefreshProperties(); } catch (Exception ex) { model.Draft.ReportFailure(ex); } RefreshState(); }, picking: () => model.IsPicking || model.IsOperating) { Dock = DockStyle.Fill };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
        bool sized = false;
        split.SizeChanged += (_, _) => { if (sized || split.Width < 640) return; split.SplitterDistance = Math.Min(360, split.Width - 320); split.Panel1MinSize = 300; split.Panel2MinSize = 280; sized = true; };
        Controls.Add(split); split.Panel2.Controls.Add(_frame);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(5) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        split.Panel1.Controls.Add(left); left.Controls.Add(_properties, 0, 0);
        var states = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        states.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); states.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); states.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        states.Controls.Add(_buildState, 0, 0); states.Controls.Add(_testState, 0, 1); states.Controls.Add(_issue, 0, 2); left.Controls.Add(states, 0, 1);
        _properties.ValidationFailed += (_, e) => { model.Draft.ReportFailure(e.Exception); RefreshState(); };
        _properties.RegisterEditor(new ChoiceEditor());
        _properties.RegisterEditor(new ActionEditor(this));
        _properties.PropertyValueChanged += (_, _) => { RefreshProperties(); RefreshState(); };
        bool opened = false;
        Load += async (_, _) => { if (opened) return; opened = true; await Run(model.OpenAsync, "读取模板"); };
        _timer.Tick += (_, _) => { if (!IsDisposed) { RefreshPropertiesIfChanged(); RefreshState(); } }; _timer.Start();
        RefreshProperties(); RefreshState(); model.Frame.RegisterViewLifetime(Dispose);
    }
    private async Task Run(Func<Task> action, string operation, bool pageOwnsOperation = false)
    {
        if (_running || _model.Draft.IsBusy || _model.Draft.IsDisposed) return;
        _running = true; if (!pageOwnsOperation) _model.IsOperating = true; _operation = operation; RefreshState();
        try { await action(); }
        catch (Exception ex) { _model.Draft.ReportFailure(ex); }
        finally { _running = false; _model.IsOperating = false; if (!IsDisposed && !_model.Draft.IsDisposed) { RefreshProperties(); RefreshState(); _frame.RefreshPreview(); } }
    }
    private void RefreshPropertiesIfChanged() { if (_revision != _model.Draft.EditRevision || !ReferenceEquals(_listed, _model.Draft.Resources)) RefreshProperties(); }
    private void RefreshProperties()
    {
        if (_model.Draft.IsDisposed) return;
        _revision = _model.Draft.EditRevision; _listed = _model.Draft.Resources; _actions.Clear();
        _properties.SelectedObject = new PropertyObject(_model.Properties(ExecuteCommand));
    }
    private async Task ExecuteCommand(EVisionTemplateAuthoringCommand command)
    {
        if (_model.CommandBlockReason(command).Length != 0) return;
        string? path = null;
        if (command is EVisionTemplateAuthoringCommand.Import or EVisionTemplateAuthoringCommand.ReadSample or EVisionTemplateAuthoringCommand.ReadTestImage)
        {
            using var file = new OpenFileDialog { Filter = command == EVisionTemplateAuthoringCommand.Import
                ? "模板清单|manifest.json|JSON|*.json" : "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.pgm|所有文件|*.*" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            path = file.FileName;
        }
        await Run(async () =>
        {
            await _model.ExecuteAsync(command, path);
            if (command == EVisionTemplateAuthoringCommand.Test) _frame.SetView(5);
            else if (command == EVisionTemplateAuthoringCommand.ReadTestImage) _frame.SetView(3);
            else if (command != EVisionTemplateAuthoringCommand.Refresh) _frame.SetView(4);
        }, command switch { EVisionTemplateAuthoringCommand.Test => "测试匹配", EVisionTemplateAuthoringCommand.Build => "生成模型", _ => "处理模板" }, pageOwnsOperation: true);
    }
    private void RefreshState()
    {
        if (_model.Draft.IsDisposed) return;
        var draft = _model.Draft;
        _properties.Enabled = !_running && !draft.IsBusy;
        foreach (var (entry, button) in _actions)
        {
            if (button.IsDisposed) continue;
            button.Text = Convert.ToString(entry.Value); button.Enabled = !_running && entry.ActionBlockReason.Length == 0;
            _tips.SetToolTip(button, entry.ActionBlockReason.Length == 0 ? entry.Description : entry.ActionBlockReason);
        }
        _buildState.Text = "制作：" + (_running ? "正在" + _operation + "…" : draft.BuildState) + "\n" + (_model.CanCommit ? "可以应用" : _model.CommitBlockReason);
        _buildState.ForeColor = draft.IsBuilt ? Color.LightGreen : Color.Gainsboro;
        _testState.Text = "测试：" + draft.TestState + "\n" + draft.TestSummary;
        _testState.ForeColor = draft.TrialResult is { Found: true } ? Color.LightGreen : draft.TrialResult != null ? Color.Khaki : Color.Gainsboro;
        _issue.Text = draft.Failure.Length > 0 ? "失败原因：" + draft.Failure : _model.IsPicking ? _model.PickMode : _model.TestBlockReason;
        _issue.ForeColor = draft.Failure.Length > 0 ? Color.Salmon : Color.Khaki;
        _tips.SetToolTip(_issue, _issue.Text);
    }
    private static Label State(string name) => new() { Name = name, Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(2), Margin = new Padding(2), TextAlign = ContentAlignment.MiddleLeft };
    protected override void Dispose(bool disposing) { if (disposing) { _timer.Stop(); _timer.Dispose(); _tips.Dispose(); } base.Dispose(disposing); }

    private sealed class ActionEditor(VisionTemplateWorkspaceControl owner) : IPropertyEditorProvider
    {
        public int Priority => 100;
        public bool CanEdit(PropertyDescriptor property) => property is EntryProperty p && p.Entry.EditorKind == WorkflowPropertyEditorKind.Action;
        public Control CreateEditor(PropertyEditorContext context)
        {
            var entry = ((EntryProperty)context.Property).Entry;
            var button = new ModernUI.WinForms.ModernButton { Text = Convert.ToString(entry.Value), Theme = ModernUI.WinForms.ModernTheme.Dark, Dock = DockStyle.Fill };
            owner._actions.Add((entry, button));
            button.Click += async (_, _) =>
            {
                if (button.IsDisposed || owner._running || owner._model.Draft.IsDisposed) return;
                try { await entry.ExecuteActionAsync(); }
                catch (Exception ex) { owner._model.Draft.ReportFailure(ex); owner.RefreshState(); }
            };
            return button;
        }
    }

    private sealed class ChoiceEditor : IPropertyEditorProvider
    {
        public int Priority => 100;
        public bool CanEdit(PropertyDescriptor property) => property is EntryProperty entry && entry.Entry.EditorKind == WorkflowPropertyEditorKind.Choice;
        public Control CreateEditor(PropertyEditorContext context)
        {
            var entry = ((EntryProperty)context.Property).Entry;
            var combo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            combo.Items.AddRange(entry.Choices.Cast<object>().ToArray()); combo.SelectedItem = entry.Choices.FirstOrDefault(c => Equals(c.Value, entry.Value));
            combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedItem is WorkflowPropertyChoice choice) context.CommitValue(choice.Value); };
            return combo;
        }
    }
    private sealed class PropertyObject(IReadOnlyList<WorkflowPropertyEntry> entries) : CustomTypeDescriptor
    {
        public override PropertyDescriptorCollection GetProperties() => new(entries.Select((e, order) => (PropertyDescriptor)new EntryProperty(e, order)).ToArray());
        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();
        public override object GetPropertyOwner(PropertyDescriptor? pd) => this;
    }
    private sealed class EntryProperty(WorkflowPropertyEntry entry, int order) : PropertyDescriptor(entry.Name,
        new Attribute[] { new DisplayNameAttribute(entry.DisplayName), new CategoryAttribute(entry.Category), new DescriptionAttribute(entry.Description), new PropertyOrderAttribute(order) })
    {
        internal WorkflowPropertyEntry Entry => entry;
        public override Type ComponentType => typeof(PropertyObject);
        public override bool IsReadOnly => entry.IsReadOnly;
        public override Type PropertyType => entry.ValueType;
        public override object? GetValue(object? component) => entry.Value;
        public override void SetValue(object? component, object? value) => entry.SetValue(value);
        public override bool CanResetValue(object component) => false;
        public override void ResetValue(object component) { }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
