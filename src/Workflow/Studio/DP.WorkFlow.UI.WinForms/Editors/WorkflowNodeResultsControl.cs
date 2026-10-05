using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点“运行结果”页：用 ModernPropertyGrid 显示可选输出端口开关、最近一次运行状态与输出，与参数页同一风格。
/// 只有端口开关可编辑，其余结果行只读。
/// </summary>
internal sealed class WorkflowNodeResultsControl : UserControl
{
    private readonly WorkflowNodeResultPageModel _model;
    private readonly ModernPropertyGrid.WinForms.ModernPropertyGrid _grid = new()
    {
        Dock = DockStyle.Fill,
        Theme = ModernTheme.Dark,
        ShowSearchBar = false
    };
    private IReadOnlyList<WorkflowNodeResultItem> _items = Array.Empty<WorkflowNodeResultItem>();
    private IReadOnlyList<WorkflowNodeOutputPortItem> _ports = Array.Empty<WorkflowNodeOutputPortItem>();

    public WorkflowNodeResultsControl(WorkflowNodeResultPageModel model)
    {
        _model = model;
        Controls.Add(_grid);
        _model.Changed += OnModelChanged;
        RefreshItems();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _model.Changed -= OnModelChanged;
        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) BeginInvoke(RefreshItems);
        else RefreshItems();
    }

    /// <summary>行结构不变时只刷新值，保留滚动位置；结构变化时整体重建。</summary>
    private void RefreshItems()
    {
        if (IsDisposed) return;
        var items = _model.GetItems();
        var ports = _model.GetOutputPorts();
        if (items.SequenceEqual(_items) && ports.SequenceEqual(_ports)) return;
        var sameLayout = items.Count == _items.Count && ports.Count == _ports.Count
            && items.Zip(_items).All(pair => pair.First.Category == pair.Second.Category && pair.First.Name == pair.Second.Name)
            && ports.Zip(_ports).All(pair => pair.First.Key == pair.Second.Key);
        _items = items;
        _ports = ports;
        if (sameLayout && _grid.SelectedObject is ResultObject current)
        {
            current.Items = items;
            current.Ports = ports;
            for (var index = 0; index < ports.Count; index++) _grid.RefreshProperty(ResultObject.PortKey(index));
            for (var index = 0; index < items.Count; index++) _grid.RefreshProperty(ResultObject.Key(index));
        }
        else
            _grid.SelectedObject = new ResultObject(_model, items, ports);
    }

    /// <summary>把端口开关与结果行暴露为属性集合，供通用属性表显示。</summary>
    private sealed class ResultObject(
        WorkflowNodeResultPageModel model,
        IReadOnlyList<WorkflowNodeResultItem> items,
        IReadOnlyList<WorkflowNodeOutputPortItem> ports) : CustomTypeDescriptor
    {
        public WorkflowNodeResultPageModel Model { get; } = model;
        public IReadOnlyList<WorkflowNodeResultItem> Items { get; set; } = items;
        public IReadOnlyList<WorkflowNodeOutputPortItem> Ports { get; set; } = ports;

        public static string Key(int index) => "Result" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static string PortKey(int index) => "Port" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override PropertyDescriptorCollection GetProperties() => new(
            Ports.Select((port, index) => (PropertyDescriptor)new PortProperty(this, index, port))
                .Concat(Items.Select((item, index) => (PropertyDescriptor)new ResultProperty(this, index, item)))
                .ToArray());

        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();

        public override object GetPropertyOwner(PropertyDescriptor? propertyDescriptor) => this;
    }

    /// <summary>可选输出端口开关：勾选在节点上显示并可连线，取消勾选则隐藏端口、改用数据绑定引用结果。</summary>
    private sealed class PortProperty(ResultObject owner, int index, WorkflowNodeOutputPortItem port)
        : PropertyDescriptor(ResultObject.PortKey(index), new Attribute[]
        {
            new DisplayNameAttribute(port.Key),
            new CategoryAttribute(WorkflowNodeResultPageModel.PortCategory),
            new DescriptionAttribute($"勾选后在节点上显示“{port.Key}”输出并可连线；取消勾选则隐藏该端口，结果仍可通过数据绑定引用。"),
            new PropertyOrderAttribute(index - 1000)
        })
    {
        public override Type ComponentType => typeof(ResultObject);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(bool);
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => index < owner.Ports.Count && owner.Ports[index].Visible;
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value)
        {
            if (index < owner.Ports.Count && value is bool visible) owner.Model.SetOutputPortVisible(owner.Ports[index].Key, visible);
        }
        public override bool ShouldSerializeValue(object component) => false;
    }

    private sealed class ResultProperty(ResultObject owner, int index, WorkflowNodeResultItem item)
        : PropertyDescriptor(ResultObject.Key(index), new Attribute[]
        {
            new DisplayNameAttribute(item.Name),
            new CategoryAttribute(item.Category),
            new PropertyOrderAttribute(index),
            ReadOnlyAttribute.Yes
        })
    {
        public override Type ComponentType => typeof(ResultObject);
        public override bool IsReadOnly => true;
        public override Type PropertyType => typeof(string);
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => index < owner.Items.Count ? owner.Items[index].Value : null;
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value) => throw new NotSupportedException("运行结果只读。");
        public override bool ShouldSerializeValue(object component) => false;
    }
}
