using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.Wpf;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>节点式左右分栏模板编辑器，固定显示制作状态、测试结果及错误。</summary>
public sealed class VisionTemplateAuthoringRenderer : IWorkflowWpfNodeEditorPageRenderer
{
    /// <inheritdoc/>
    public string RendererKey => VisionTemplateAuthoringPageModel.RendererKey;
    /// <inheritdoc/>
    public Type ModelType => typeof(VisionTemplateAuthoringPageModel);
    /// <inheritdoc/>
    public FrameworkElement CreateElement(WorkflowNodeEditorPageDescriptor page) => new VisionTemplateWorkspaceControl((VisionTemplateAuthoringPageModel)page.Model);
}

internal sealed class VisionTemplateWorkspaceControl : Grid, IDisposable
{
    private readonly VisionTemplateAuthoringPageModel _model;
    private readonly VisionFrameEditorControl _frame;
    private readonly StackPanel _properties = new();
    private readonly TextBlock _buildState = State("TemplateBuildState");
    private readonly TextBlock _testState = State("TemplateTestState");
    private readonly TextBlock _issue = State("TemplateFailure");
    private readonly List<(WorkflowPropertyEntry Entry, Button Button)> _actions = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private bool _running, _disposed;
    private string _operation = "";
    private long _revision = -1;
    private int _generation;
    private IReadOnlyList<VisionTemplateResourceChoice>? _listed;

    internal VisionTemplateWorkspaceControl(VisionTemplateAuthoringPageModel model)
    {
        _model = model; Background = Dark; MinWidth = 620;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360), MinWidth = 300 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) }); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 280 });
        _frame = new VisionFrameEditorControl(model.Frame, templatePane: false,
            pick: p => { try { model.Pick(p); RefreshProperties(); } catch (Exception ex) { model.Draft.ReportFailure(ex); } RefreshState(); }, picking: () => model.IsPicking || model.IsOperating);
        SetColumn(_frame, 2); Children.Add(_frame);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.DimGray }; SetColumn(splitter, 1); Children.Add(splitter);
        var left = new Grid { Margin = new Thickness(5) };
        Children.Add(left);
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
        var scroll = new ScrollViewer { Content = _properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; left.Children.Add(scroll);
        var states = new Grid(); states.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) }); states.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) }); states.RowDefinitions.Add(new RowDefinition());
        states.Children.Add(_buildState); SetRow(_testState, 1); states.Children.Add(_testState); SetRow(_issue, 2); states.Children.Add(_issue); SetRow(states, 1); left.Children.Add(states);
        bool opened = false;
        Loaded += async (_, _) => { if (_disposed) return; _timer.Start(); if (opened) return; opened = true; await Run(model.OpenAsync, "读取模板"); };
        Unloaded += (_, _) => _timer.Stop(); _timer.Tick += (_, _) => { if (!_disposed) { if (_revision != model.Draft.EditRevision || !ReferenceEquals(_listed, model.Draft.Resources)) RefreshProperties(); RefreshState(); } };
        RefreshProperties(); RefreshState(); model.Frame.RegisterViewLifetime(Dispose);
    }
    private async Task Run(Func<Task> action, string operation, bool pageOwnsOperation = false)
    {
        if (_running || _model.Draft.IsBusy || _model.Draft.IsDisposed) return;
        _running = true; if (!pageOwnsOperation) _model.IsOperating = true; _operation = operation; RefreshState();
        try { await action(); }
        catch (Exception ex) { _model.Draft.ReportFailure(ex); }
        finally { _running = false; _model.IsOperating = false; if (!_disposed && !_model.Draft.IsDisposed) { RefreshProperties(); RefreshState(); _frame.RefreshPreview(); } }
    }
    private void RefreshProperties()
    {
        if (_model.Draft.IsDisposed) return;
        _revision = _model.Draft.EditRevision; int generation = ++_generation; _properties.Children.Clear(); _actions.Clear();
        foreach (var group in _model.Properties(ExecuteCommand).GroupBy(p => p.Category))
        {
            var fields = new StackPanel();
            foreach (var entry in group)
            {
                var row = new Grid { Margin = new Thickness(3), MinHeight = 30 }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(new TextBlock { Text = entry.DisplayName, Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3), ToolTip = entry.Description });
                FrameworkElement editor;
                if (entry.EditorKind == WorkflowPropertyEditorKind.Action)
                {
                    var button = Button(Convert.ToString(entry.Value) ?? "执行"); _actions.Add((entry, button));
                    button.Click += async (_, _) =>
                    {
                        if (generation != _generation || _running || _model.Draft.IsDisposed) return;
                        try { await entry.ExecuteActionAsync(); } catch (Exception ex) { _model.Draft.ReportFailure(ex); RefreshState(); }
                    };
                    editor = button;
                }
                else if (entry.EditorKind == WorkflowPropertyEditorKind.ReadOnly) editor = new TextBlock { Text = Convert.ToString(entry.Value), Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                else if (entry.EditorKind is WorkflowPropertyEditorKind.Choice or WorkflowPropertyEditorKind.Enum)
                {
                    var choices = entry.EditorKind == WorkflowPropertyEditorKind.Choice ? entry.Choices : Enum.GetValues(entry.ValueType).Cast<object>().Select(v => new WorkflowPropertyChoice(v.ToString()!, v)).ToArray();
                    var combo = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => Equals(c.Value, entry.Value)), Margin = new Thickness(2) };
                    combo.SelectionChanged += (_, _) => { if (generation != _generation || combo.SelectedItem is not WorkflowPropertyChoice choice) return; Edit(() => entry.SetValue(choice.Value)); };
                    editor = combo;
                }
                else if (entry.EditorKind == WorkflowPropertyEditorKind.Boolean)
                {
                    var toggle = new CheckBox { IsChecked = entry.Value is true, VerticalAlignment = VerticalAlignment.Center };
                    toggle.Click += (_, _) => { if (generation == _generation) Edit(() => entry.SetValue(toggle.IsChecked == true)); }; editor = toggle;
                }
                else
                {
                    var accepted = Convert.ToString(entry.Value, CultureInfo.InvariantCulture) ?? "";
                    var text = new TextBox { Text = accepted, VerticalContentAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)), Foreground = Brushes.Gainsboro, BorderBrush = Brushes.DimGray, Margin = new Thickness(2) };
                    text.LostKeyboardFocus += (_, _) => { if (generation != _generation || _model.Draft.IsDisposed) return;
                        try { entry.SetValue(text.Text); accepted = text.Text; RefreshState(); } catch (Exception ex) { text.Text = accepted; _model.Draft.ReportFailure(ex); RefreshState(); } };
                    editor = text;
                }
                editor.ToolTip = entry.Description; SetColumn(editor, 1); row.Children.Add(editor); fields.Children.Add(row);
            }
            _properties.Children.Add(new Expander { Header = group.Key, Content = fields, IsExpanded = true, Foreground = Brushes.Gainsboro, Margin = new Thickness(2, 4, 2, 4) });
        }
        _listed = _model.Draft.Resources;
        void Edit(Action action) { try { action(); RefreshProperties(); RefreshState(); } catch (Exception ex) { _model.Draft.ReportFailure(ex); RefreshState(); } }
    }
    private async Task ExecuteCommand(EVisionTemplateAuthoringCommand command)
    {
        if (_model.CommandBlockReason(command).Length != 0) return;
        string? path = null;
        if (command is EVisionTemplateAuthoringCommand.Import or EVisionTemplateAuthoringCommand.ReadSample or EVisionTemplateAuthoringCommand.ReadTestImage)
        {
            var file = new Microsoft.Win32.OpenFileDialog { Filter = command == EVisionTemplateAuthoringCommand.Import
                ? "模板清单|manifest.json|JSON|*.json" : "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.pgm|所有文件|*.*" };
            if (file.ShowDialog(Window.GetWindow(this)) != true) return;
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
        var draft = _model.Draft; _properties.IsEnabled = !_running && !draft.IsBusy;
        foreach (var (entry, button) in _actions)
        {
            button.Content = entry.Value; button.IsEnabled = !_running && entry.ActionBlockReason.Length == 0;
            button.ToolTip = entry.ActionBlockReason.Length == 0 ? entry.Description : entry.ActionBlockReason;
        }
        _buildState.Text = "制作：" + (_running ? "正在" + _operation + "…" : draft.BuildState) + "\n" + (_model.CanCommit ? "可以应用" : _model.CommitBlockReason);
        _buildState.Foreground = draft.IsBuilt ? Brushes.LightGreen : Brushes.Gainsboro;
        _testState.Text = "测试：" + draft.TestState + "\n" + draft.TestSummary;
        _testState.Foreground = draft.TrialResult is { Found: true } ? Brushes.LightGreen : draft.TrialResult != null ? Brushes.Khaki : Brushes.Gainsboro;
        _issue.Text = draft.Failure.Length > 0 ? "失败原因：" + draft.Failure : _model.IsPicking ? _model.PickMode : _model.TestBlockReason;
        _issue.Foreground = draft.Failure.Length > 0 ? Brushes.Salmon : Brushes.Khaki;
        _issue.ToolTip = _issue.Text;
    }
    private static Brush Dark => new SolidColorBrush(Color.FromRgb(30, 30, 30));
    private static Button Button(string caption) => new() { Content = caption, Margin = new Thickness(3), Padding = new Thickness(4) };
    private static TextBlock State(string name) => new() { Name = name, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), Foreground = Brushes.Gainsboro };
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _frame.Dispose(); }
}
