using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.DesignerSmoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Run();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run()
    {
        using var surface = new DesignSurface(typeof(Form));
        var host = (IDesignerHost?)surface.GetService(typeof(IDesignerHost))
            ?? throw new InvalidOperationException("IDesignerHost is unavailable.");
        var root = host.RootComponent as Form
            ?? throw new InvalidOperationException("Designer root is not a Form.");
        root.ClientSize = new Size(1200, 800);

        var toolboxTypes = typeof(ModernControl).Assembly.GetExportedTypes()
            .Where(type => typeof(Control).IsAssignableFrom(type))
            .Where(type => !type.IsAbstract)
            .Where(type => type.GetCustomAttribute<ToolboxItemAttribute>(false) is { } item &&
                           !string.IsNullOrEmpty(item.ToolboxItemTypeName))
            .OrderBy(type => type.FullName)
            .ToArray();
        if (toolboxTypes.Length == 0) throw new InvalidOperationException("No Toolbox controls were discovered.");

        var created = 0;
        foreach (var type in toolboxTypes)
        {
            var component = host.CreateComponent(type, $"designer{type.Name}");
            if (component is not Control control)
                throw new InvalidOperationException($"Designer did not create Control {type.FullName}.");
            if (control is not ToolStripDropDown)
            {
                root.Controls.Add(control);
                control.Bounds = new Rectangle((created % 4) * 290, (created / 4) * 48,
                    270, Math.Max(30, control.Height));
            }
            VerifyPropertyDescriptors(type, control);
            created++;
        }

        if (host.Container.Components.Count < created + 1)
            throw new InvalidOperationException("Designer container did not retain all created controls.");
        using (var bitmap = new Bitmap(root.ClientSize.Width, root.ClientSize.Height))
            root.DrawToBitmap(bitmap, root.ClientRectangle);

        surface.Flush();
        Console.WriteLine($"DESIGNER_SMOKE_OK controls={created} components={host.Container.Components.Count}");
    }

    private static void VerifyPropertyDescriptors(Type type, Control control)
    {
        var properties = TypeDescriptor.GetProperties(control);
        var name = properties[nameof(Control.Name)]
            ?? throw new InvalidOperationException($"{type.FullName} has no Name descriptor.");
        if (name.IsReadOnly) throw new InvalidOperationException($"{type.FullName}.Name is read-only in designer.");
        name.SetValue(control, control.Name);

        foreach (PropertyDescriptor property in properties.Cast<PropertyDescriptor>())
        {
            if (property.SerializationVisibility == DesignerSerializationVisibility.Content)
                _ = property.GetValue(control);
            if (property.ComponentType != type ||
                property.Attributes[typeof(DefaultValueAttribute)] is not DefaultValueAttribute defaultValue ||
                property.IsReadOnly || property.SerializationVisibility == DesignerSerializationVisibility.Hidden)
                continue;
            var current = property.GetValue(control);
            if (!Equals(current, defaultValue.Value))
                throw new InvalidOperationException($"{type.FullName}.{property.Name} runtime default '{current}' " +
                    $"does not match DefaultValue '{defaultValue.Value}'.");
            if (property.ShouldSerializeValue(control))
                throw new InvalidOperationException($"{type.FullName}.{property.Name} serializes its declared default value.");
        }
    }
}
