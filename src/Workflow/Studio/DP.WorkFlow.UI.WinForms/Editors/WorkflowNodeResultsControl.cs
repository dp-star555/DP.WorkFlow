using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>节点“运行结果”页：用只读 ModernPropertyGrid 显示最近一次运行状态与输出，与参数页同一风格。</summary>
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
        if (items.SequenceEqual(_items)) return;
        var sameLayout = items.Count == _items.Count
            && items.Zip(_items).All(pair => pair.First.Category == pair.Second.Category && pair.First.Name == pair.Second.Name);
        _items = items;
        if (sameLayout && _grid.SelectedObject is ResultObject current)
        {
            current.Items = items;
            for (var index = 0; index < items.Count; index++) _grid.RefreshProperty(ResultObject.Key(index));
        }
        else
            _grid.SelectedObject = new ResultObject(items);
    }

    /// <summary>把结果行暴露为只读属性集合，供通用属性表显示。</summary>
    private sealed class ResultObject(IReadOnlyList<WorkflowNodeResultItem> items) : CustomTypeDescriptor
    {
        public IReadOnlyList<WorkflowNodeResultItem> Items { get; set; } = items;

        public static string Key(int index) => "Result" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override PropertyDescriptorCollection GetProperties() => new(
            Items.Select((item, index) => (PropertyDescriptor)new ResultProperty(this, index, item)).ToArray(), readOnly: true);

        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetProperties();

        public override object GetPropertyOwner(PropertyDescriptor? propertyDescriptor) => this;
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
