using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

/// <summary>Gallery 中用于演示 PropertyGrid 按钮编辑器的操作值。</summary>
internal sealed class DemoPropertyAction(string text, Action<IWin32Window?> execute)
{
    public string Text { get; } = text;
    public Action<IWin32Window?> Execute { get; } = execute;
}

/// <summary>将 DemoPropertyAction 属性显示为 ModernButton。</summary>
internal sealed class DemoPropertyActionEditorProvider : IPropertyEditorProvider
{
    public int Priority => 100;

    public bool CanEdit(PropertyDescriptor property) =>
        property.PropertyType == typeof(DemoPropertyAction);

    public Control CreateEditor(PropertyEditorContext context)
    {
        var action = (DemoPropertyAction)context.Value!;
        var button = new ModernButton
        {
            Text = action.Text,
            ButtonType = ModernButtonType.Primary,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        button.Click += (_, _) => action.Execute(button.FindForm());
        return button;
    }
}

internal sealed class HelloDemoForm : Form
{
    public HelloDemoForm()
    {
        Text = "PropertyGrid Action Demo";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(360, 180);
        MinimumSize = new Size(320, 160);
        Font = new Font("Microsoft YaHei UI", 9F);

        Controls.Add(new Label
        {
            Text = "Hallow, word",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            ForeColor = ModernTheme.Light.Primary
        });
    }
}
