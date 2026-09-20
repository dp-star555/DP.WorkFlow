namespace ModernUI.WinForms.Gallery;

/// <summary>Gallery 数据绑定案例使用的稳定值/显示文本模型。</summary>
internal sealed record DemoOption(string Id, string Name)
{
    public override string ToString() => Name;
}
