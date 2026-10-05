using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DP.Vision;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI.Wpf;

/// <summary>节点页面内展开的模板制作区。</summary>
internal sealed class VisionTemplateEditorControl : Expander
{
    private readonly VisionTemplateEditorModel _model;
    private readonly StackPanel _fields = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly Action<int> _show;
    private readonly Func<Task> _tryInput;
    private readonly Func<string, Task> _tryTestImage;
    private readonly Func<string> _reference;
    private int _generation;
    internal bool PickOrigin { get; private set; }
    internal bool PickDirection { get; private set; }
    internal VisionTemplateEditorControl(VisionTemplateEditorModel model, Action<int> show, Func<Task> tryInput, Func<string, Task> tryTestImage, Func<string> reference, bool expanded = false)
    {
        _model = model; _show = show; _tryInput = tryInput; _tryTestImage = tryTestImage; _reference = reference;
        Header = "模板制作"; Margin = new Thickness(4);
        Content = new ScrollViewer { Content = _fields, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Expanded += (_, _) => _show(4); Rebuild(); IsExpanded = expanded;
    }
    internal void RefreshFields() { if (!_model.IsDisposed) Rebuild(); }

    private async Task Run(Func<Task> action)
    {
        try { await action(); if (_model.IsBusy || _model.IsDisposed) return; Rebuild(); _status.Text = _model.Status; }
        catch (Exception ex) { if (!_model.IsDisposed) _status.Text = ex.Message; }
    }
    private void Rebuild()
    {
        _generation++; var generation = _generation; _fields.Children.Clear();
        var choices = _model.Choices.Select(d => new Choice(d.ImplementationId, d.Engine + " / " + ((DP.Vision.Algorithms.IVisionTemplateFactoryDescription)d.Factory).MethodDisplayName)).ToList();
        if (!choices.Any(c => c.Id == _model.ImplementationId)) choices.Add(new Choice(_model.ImplementationId, _model.ImplementationId + "（未安装或不兼容）"));
        var implementations = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = _model.ImplementationId, Margin = new Thickness(4), MinWidth = 320 };
        implementations.SelectionChanged += (_, _) => { if (generation != _generation || implementations.SelectedItem is not Choice choice) return;
            try { _model.ImplementationId = choice.Id; Rebuild(); } catch (Exception ex) { _status.Text = ex.Message; } };
        _fields.Children.Add(implementations);
        var resources = new ComboBox { ItemsSource = _model.Resources, Margin = new Thickness(3), MinWidth = 300 };
        resources.SelectionChanged += async (_, _) =>
        {
            if (generation != _generation || resources.SelectedItem is not VisionTemplateResourceChoice choice) return;
            await Run(async () => { await _model.LoadResourceAsync(choice.Reference); _show(4); });
        };
        _fields.Children.Add(new TextBlock { Text = "本配方已制作模板（最多256个版本）", Margin = new Thickness(3) });
        _fields.Children.Add(resources);
        var actions = new WrapPanel();
        void Button(string caption, Func<Task> action)
        { var button = new Button { Content = caption, Margin = new Thickness(3), Padding = new Thickness(5) }; button.Click += async (_, _) => await Run(action); actions.Children.Add(button); }
        Button("刷新模板列表", _model.RefreshResourcesAsync);
        Button("读取样图…", async () =>
        { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.pgm|所有文件|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await _model.ReadSourceAsync(dialog.FileName); _show(4); } });
        Button("使用当前输入", () => { _model.UseInput(); _show(4); return Task.CompletedTask; });
        Button("新建空白模板", () => { _model.StartNewTemplate(); _show(4); return Task.CompletedTask; });
        Button("读取当前模板", async () => { await _model.LoadResourceAsync(_reference()); _show(4); });
        Button("选择模板资源…", async () =>
        { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "模板清单|manifest.json|JSON|*.json" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await _model.LoadResourceAsync(dialog.FileName); _show(4); } });
        Button("在图上设原点", () => { PickOrigin = true; PickDirection = false; _show(4); return Task.CompletedTask; });
        Button("在图上设方向", () => { PickDirection = true; PickOrigin = false; _show(4); return Task.CompletedTask; });
        Button("生成模型", _model.BuildAsync);
        Button("输入试匹配", async () => { await _tryInput(); _show(5); });
        Button("测试图像试匹配…", async () =>
        { var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.pgm|所有文件|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) { await _tryTestImage(dialog.FileName); _show(5); } });
        _fields.Children.Add(actions);
        Field("参考原点 X", _model.OriginX.ToString("R", CultureInfo.InvariantCulture), value => _model.OriginX = double.Parse(value, CultureInfo.InvariantCulture));
        Field("参考原点 Y", _model.OriginY.ToString("R", CultureInfo.InvariantCulture), value => _model.OriginY = double.Parse(value, CultureInfo.InvariantCulture));
        Field("参考X轴（°）", (_model.AxisAngleRadians * 180 / Math.PI).ToString("R", CultureInfo.InvariantCulture), value => _model.AxisAngleRadians = double.Parse(value, CultureInfo.InvariantCulture) * Math.PI / 180);
        foreach (var parameter in _model.Parameters)
        {
            var entry = WorkflowPropertyEntry.Create(parameter.Id, parameter.DisplayName, "制作参数", parameter.Description ?? "", WorkflowPropertyEditorKind.Text, parameter.ValueType,
                () => parameter.ValueType.IsEnum ? Enum.Parse(parameter.ValueType, _model.ParameterValue(parameter)) : Convert.ChangeType(_model.ParameterValue(parameter), parameter.ValueType, CultureInfo.InvariantCulture),
                value => _model.SetParameter(parameter, Convert.ToString(value, CultureInfo.InvariantCulture)!));
            if (parameter.DisplayRadiansAsDegrees) entry.WithRadiansAsDegrees();
            Field(entry.DisplayName, Convert.ToString(entry.Value, CultureInfo.InvariantCulture)!, entry.SetValue, entry.Description);
        }
        _status.Text = _model.Status; _fields.Children.Add(_status);
        void Field(string caption, string value, Action<string> write, string? description = null)
        {
            var row = new Grid { Margin = new Thickness(3) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var label = new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center };
            var text = new TextBox { Text = value, MinWidth = 150, ToolTip = description };
            var accepted = value;
            text.LostFocus += (_, _) => { if (generation != _generation || _model.IsDisposed) return;
                try { write(text.Text); accepted = text.Text; _status.Text = _model.Status; } catch (Exception ex) { text.Text = accepted; _status.Text = ex.Message; } };
            row.Children.Add(label); Grid.SetColumn(text, 1); row.Children.Add(text); _fields.Children.Add(row);
        }
    }
    internal void Pick(PointD point)
    {
        if (!PickOrigin && !PickDirection) return;
        try { if (PickOrigin) { _model.OriginX = point.X; _model.OriginY = point.Y; }
            else { if (Math.Abs(point.X - _model.OriginX) + Math.Abs(point.Y - _model.OriginY) < 1e-10) throw new ArgumentException("方向点不能与原点重合。");
                _model.AxisAngleRadians = Math.Atan2(point.Y - _model.OriginY, point.X - _model.OriginX); }
            PickOrigin = PickDirection = false; Rebuild(); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private sealed record Choice(string Id, string Label);
}
