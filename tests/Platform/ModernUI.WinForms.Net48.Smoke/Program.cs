using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Net48.Smoke;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var host = new Form { ClientSize = new Size(900, 700) })
            {
                var toolboxControls = typeof(ModernControl).Assembly.GetExportedTypes()
                    .Where(type => typeof(Control).IsAssignableFrom(type))
                    .Where(type => type.GetCustomAttribute<ToolboxItemAttribute>(false)?.ToolboxItemTypeName.Length > 0)
                    .Where(type => !type.IsAbstract)
                    .OrderBy(type => type.FullName)
                    .ToArray();

                var top = 0;
                foreach (var type in toolboxControls)
                {
                    if (Activator.CreateInstance(type) is not Control control)
                        throw new InvalidOperationException($"Cannot create {type.FullName}.");
                    control.Bounds = new Rectangle(0, top, 280, Math.Max(32, control.Height));
                    if (control is not ToolStripDropDown)
                    {
                        host.Controls.Add(control);
                        top = (top + control.Height + 4) % 640;
                    }
                    control.CreateControl();
                }

                host.CreateControl();
                using (var bitmap = new Bitmap(host.ClientSize.Width, host.ClientSize.Height))
                    host.DrawToBitmap(bitmap, host.ClientRectangle);

                VerifyDateRangePopupFonts(host);
                Console.WriteLine($"NET48_SMOKE_OK controls={toolboxControls.Length}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyDateRangePopupFonts(Form host)
    {
        using (var range = new ModernDateRangePicker
        {
            Bounds = new Rectangle(320, 20, 480, 34),
            StartDate = new DateTime(2026, 8, 5),
            EndDate = new DateTime(2026, 8, 12),
            Font = new Font("Microsoft YaHei UI", 9F)
        })
        {
            host.Controls.Add(range);
            host.Show();
            Application.DoEvents();
            var pickers = range.Controls.OfType<ModernDatePicker>().OrderBy(picker => picker.Left).ToArray();
            VerifyOrder(host, pickers, new[] { 1, 0, 1 }, "right-left-right");
            VerifyOrder(host, pickers, new[] { 0, 1, 0 }, "left-right-left");
            host.Controls.Remove(range);
        }
    }

    private static void VerifyOrder(Form host, ModernDatePicker[] pickers, int[] order, string scenario)
    {
        foreach (var index in order)
        {
            pickers[index].DroppedDown = true;
            Application.DoEvents();
            var popup = host.OwnedForms.Single(form => form.Visible);
            var calendar = Descendants(popup).Single(control => control.GetType().Name == "ModernCalendarSurface");
            var expected = pickers[index].Font;
            if (!string.Equals(calendar.Font.FontFamily.Name, expected.FontFamily.Name, StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(calendar.Font.SizeInPoints - expected.SizeInPoints) > .01f ||
                calendar.Font.Height != expected.Height)
                throw new InvalidOperationException($"Date range popup font mismatch ({scenario}, index={index}): " +
                    $"picker={expected.Name} {expected.SizeInPoints}pt/{expected.Height}px, " +
                    $"calendar={calendar.Font.Name} {calendar.Font.SizeInPoints}pt/{calendar.Font.Height}px.");
            pickers[index].DroppedDown = false;
            Application.DoEvents();
        }
    }

    private static Control[] Descendants(Control root) => root.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)))
        .ToArray();
}
