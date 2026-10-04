using System.Globalization;
using DP.Vision;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>嵌入节点页面的制作区；参数根据引擎描述展开。</summary>
internal sealed class VisionTemplateEditorControl : UserControl
{
    private readonly VisionTemplateEditorModel _model;
    private readonly FlowLayoutPanel _fields = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(540, 0) };
    private readonly Action<int> _show;
    private readonly Func<Task> _tryInput, _tryManual;
    private readonly Func<string> _reference;
    private int _generation;
    internal bool PickOrigin { get; private set; }
    internal bool PickDirection { get; private set; }

    internal VisionTemplateEditorControl(VisionTemplateEditorModel model, Action<int> show, Func<Task> tryInput, Func<Task> tryManual, Func<string> reference, bool expanded = false)
    {
        _model = model; _show = show; _tryInput = tryInput; _tryManual = tryManual; _reference = reference;
        Dock = DockStyle.Top; Height = 38;
        var toggle = new Button { Text = "▶ 模板制作", Dock = DockStyle.Top, Height = 32 };
        _fields.Visible = false;
        toggle.Click += (_, _) => { _fields.Visible = !_fields.Visible; Height = _fields.Visible ? 320 : 38; toggle.Text = _fields.Visible ? "▼ 模板制作" : "▶ 模板制作"; if (_fields.Visible) _show(4); };
        Controls.Add(_fields); Controls.Add(toggle); Rebuild();
        if (expanded) toggle.PerformClick();
    }
    internal void RefreshFields() { if (!_model.IsDisposed) Rebuild(); }

    private async Task Run(Func<Task> action)
    {
        try { await action(); if (!IsDisposed && !_model.IsDisposed) { Rebuild(); _status.Text = _model.Status; } }
        catch (Exception ex) { if (!IsDisposed) _status.Text = ex.Message; }
    }
    private void Rebuild()
    {
        _generation++; foreach (Control control in _fields.Controls.Cast<Control>().ToArray()) { _fields.Controls.Remove(control); if (control != _status) control.Dispose(); }
        var generation = _generation;
        var implementations = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520, DisplayMember = "Label", ValueMember = "Id" };
        var choices = _model.Choices.Select(d => new Choice(d.ImplementationId, d.Engine + " / " + ((DP.Vision.Algorithms.IVisionTemplateFactoryDescription)d.Factory).MethodDisplayName)).ToList();
        if (!choices.Any(c => c.Id == _model.ImplementationId)) choices.Add(new Choice(_model.ImplementationId, _model.ImplementationId + "（未安装或不兼容）"));
        implementations.DataSource = choices; implementations.SelectedValue = _model.ImplementationId;
        implementations.SelectedIndexChanged += (_, _) => { if (generation != _generation || implementations.SelectedItem is not Choice choice) return;
            try { _model.ImplementationId = choice.Id; Rebuild(); } catch (Exception ex) { _status.Text = ex.Message; } };
        _fields.Controls.Add(implementations);
        var resources = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 520 };
        resources.Items.AddRange(_model.Resources.Cast<object>().ToArray());
        resources.SelectedIndexChanged += async (_, _) =>
        {
            if (generation != _generation || resources.SelectedItem is not VisionTemplateResourceChoice choice) return;
            await Run(async () => { await _model.LoadResourceAsync(choice.Reference); _show(4); });
        };
        _fields.Controls.Add(new Label { Text = "本配方已制作模板（最多256个版本）", AutoSize = true });
        _fields.Controls.Add(resources);
        var actions = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(560, 0) };
        void Button(string caption, Func<Task> action)
        { var button = new Button { Text = caption, AutoSize = true }; button.Click += async (_, _) => await Run(action); actions.Controls.Add(button); }
        Button("刷新模板列表", _model.RefreshResourcesAsync);
        Button("读取样图…", async () =>
        { using var dialog = new OpenFileDialog { Filter = "图像|*.png;*.bmp;*.jpg;*.jpeg;*.tif;*.tiff;*.pgm|所有文件|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK) { await _model.ReadSourceAsync(dialog.FileName); _show(4); } });
        Button("使用当前输入", () => { _model.UseInput(); _show(4); return Task.CompletedTask; });
        Button("新建空白模板", () => { _model.StartNewTemplate(); _show(4); return Task.CompletedTask; });
        Button("读取当前模板", async () => { await _model.LoadResourceAsync(_reference()); _show(4); });
        Button("选择模板资源…", async () =>
        { using var dialog = new OpenFileDialog { Filter = "模板清单|manifest.json|JSON|*.json" }; if (dialog.ShowDialog(this) == DialogResult.OK) { await _model.LoadResourceAsync(dialog.FileName); _show(4); } });
        Button("在图上设原点", () => { PickOrigin = true; PickDirection = false; _show(4); return Task.CompletedTask; });
        Button("在图上设方向", () => { PickDirection = true; PickOrigin = false; _show(4); return Task.CompletedTask; });
        Button("生成模型", _model.BuildAsync);
        Button("输入试匹配", async () => { await _tryInput(); _show(5); });
        Button("手动预览试匹配", async () => { await _tryManual(); _show(5); });
        _fields.Controls.Add(actions);
        Field("参考原点 X", _model.OriginX.ToString("R", CultureInfo.InvariantCulture), value => _model.OriginX = double.Parse(value, CultureInfo.InvariantCulture));
        Field("参考原点 Y", _model.OriginY.ToString("R", CultureInfo.InvariantCulture), value => _model.OriginY = double.Parse(value, CultureInfo.InvariantCulture));
        Field("参考X轴（rad）", _model.AxisAngleRadians.ToString("R", CultureInfo.InvariantCulture), value => _model.AxisAngleRadians = double.Parse(value, CultureInfo.InvariantCulture));
        foreach (var parameter in _model.Parameters)
            Field(parameter.DisplayName, _model.ParameterValue(parameter), value => _model.SetParameter(parameter, value), parameter.Description);
        _status.Text = _model.Status; _fields.Controls.Add(_status);
        void Field(string caption, string value, Action<string> write, string? description = null)
        {
            var row = new FlowLayoutPanel { AutoSize = true }; var label = new Label { Text = caption, Width = 210, AutoSize = false, Height = 26 };
            var text = new TextBox { Text = value, Width = 300, AccessibleDescription = description };
            var accepted = value;
            text.Validated += (_, _) => { if (generation != _generation || IsDisposed || _model.IsDisposed) return;
                try { write(text.Text); accepted = text.Text; _status.Text = _model.Status; } catch (Exception ex) { text.Text = accepted; _status.Text = ex.Message; } };
            row.Controls.Add(label); row.Controls.Add(text); _fields.Controls.Add(row);
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
