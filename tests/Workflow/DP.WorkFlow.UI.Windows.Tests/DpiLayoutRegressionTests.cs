using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

// 控件库用例：保护 ModernUI 控件在 96-DPI 下的逻辑尺寸，只在控件库源码改动时才需要重跑。
[Trait(TestCategories.Category, TestCategories.UiControls)]
public sealed class DpiLayoutRegressionTests
{
    [Fact]
    public void CompositeDateAndDurationControls_KeepChildrenInsideAfterScaling()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var range = new ModernDateRangePicker { Size = new Size(330, 34) };
                using var duration = new ModernDurationInput { Size = new Size(230, 34) };

                range.Scale(new SizeF(1.5f, 1.5f));
                duration.Scale(new SizeF(1.5f, 1.5f));

                AssertChildrenInside(range);
                AssertChildrenInside(duration);
                var rangeChildren = range.Controls.Cast<Control>().OrderBy(control => control.Left).ToArray();
                Assert.Equal(0, rangeChildren[0].Left);
                Assert.Equal(range.ClientSize.Width, rangeChildren[^1].Right);
                var durationChildren = duration.Controls.Cast<Control>().OrderBy(control => control.Left).ToArray();
                Assert.Equal(0, durationChildren[0].Left);
                Assert.Equal(duration.ClientSize.Width, durationChildren[^1].Right);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "DPI layout regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void DateAndDurationControls_RelayoutAfterHandleScalingCompletes()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(760, 180) };
                using var date = new ModernDatePicker { Bounds = new Rectangle(10, 10, 180, 34) };
                using var time = new ModernTimePicker { Bounds = new Rectangle(200, 10, 180, 34) };
                using var range = new ModernDateRangePicker { Bounds = new Rectangle(10, 70, 330, 34) };
                using var duration = new ModernDurationInput { Bounds = new Rectangle(360, 70, 260, 34) };
                host.Controls.AddRange([date, time, range, duration]);
                host.Show();
                host.Scale(new SizeF(1.5f, 1.5f));
                Application.DoEvents();

                AssertChildrenInside(date);
                AssertChildrenInside(time);
                AssertChildrenInside(range);
                AssertChildrenInside(duration);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Handle DPI layout regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Input_SynchronizesNativeEditorFontAfterInitialHandleDpiTransaction()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var expectedFont = new Font("Microsoft YaHei UI", 6F);
                using var staleChildFont = new Font("Microsoft YaHei UI", 4F);
                using var host = new Form { ClientSize = new Size(320, 100), ShowInTaskbar = false };
                using var input = new ModernInput { Bounds = new Rectangle(20, 20, 240, 34), Font = expectedFont };
                input.InnerTextBox.Font = staleChildFont;
                host.Controls.Add(input);
                host.Show();
                PumpMessages(TimeSpan.FromMilliseconds(100));

                Assert.Equal(input.Font, input.InnerTextBox.Font);
                Assert.Equal(input.Font.Height, input.InnerTextBox.Font.Height);
                host.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Initial input font synchronization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void TabControl_SynchronizesInheritedPageFontsAfterInitialHandleDpiTransaction()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var expectedFont = new Font("Microsoft YaHei UI", 6F);
                using var staleChildFont = new Font("Microsoft YaHei UI", 4F);
                using var host = new Form { ClientSize = new Size(420, 240), ShowInTaskbar = false };
                using var tabs = new ModernTabControl { Dock = DockStyle.Fill, Font = expectedFont };
                var label = new Label { Text = "Overview", Dock = DockStyle.Fill };
                var page = new TabPage("Overview");
                page.Controls.Add(label);
                tabs.TabPages.Add(page);
                // Simulate the child-only second font transform observed during initial PMv2 creation.
                page.Font = staleChildFont;
                label.Font = staleChildFont;
                host.Controls.Add(tabs);
                host.Show();
                PumpMessages(TimeSpan.FromMilliseconds(100));

                Assert.Equal(tabs.Font, page.Font);
                Assert.Equal(tabs.Font, label.Font);
                host.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Initial tab font synchronization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void CommandBar_KeepsDeviceScaledPresenterHeightAfterParentRelayout()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var bar = new ModernCommandBar { Size = new Size(400, 42) };
                bar.Commands.Add(new ModernCommand { Text = "Run", Icon = ModernIconKind.Play });
                bar.Scale(new SizeF(1.5f, 1.5f));
                bar.Width++;

                var presenter = bar.Controls.OfType<ModernButton>().Single(button => button.Command is not null);
                Assert.Equal(48, presenter.Height);
                Assert.Equal((bar.ClientSize.Height - presenter.Height) / 2, presenter.Top);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "CommandBar DPI layout regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void ComboBox_HighDpiChromeDoesNotOverlapSelectedTextLine()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            var previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                using var host = new Form
                {
                    ClientSize = new Size(360, 120),
                    Font = new Font("Microsoft YaHei UI", 9F),
                    ShowInTaskbar = false
                };
                using var combo = new ModernComboBox
                {
                    Bounds = new Rectangle(20, 20, 220, 34),
                    Text = "DemoPlc.Speed",
                    ReadOnly = true
                };
                host.Controls.Add(combo);
                host.Show();
                PumpMessages(TimeSpan.FromMilliseconds(100));

                var chrome = combo.Controls.Cast<Control>()
                    .Where(control => control.GetType().Name == "ComboBottomChromeSurface")
                    .OrderBy(control => control.Top)
                    .ToArray();
                Assert.Equal(2, chrome.Length);
                var textLineHeight = TextRenderer.MeasureText("Ag", combo.Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
                var textTop = (combo.ClientSize.Height - textLineHeight) / 2;
                var textBottom = textTop + textLineHeight;
                Assert.True(chrome[0].Bottom <= textTop,
                    $"Top chrome {chrome[0].Bounds} overlaps selected text {textTop}..{textBottom} at {combo.DeviceDpi} DPI.");
                Assert.True(chrome[1].Top >= textBottom,
                    $"Bottom chrome {chrome[1].Bounds} overlaps selected text {textTop}..{textBottom} at {combo.DeviceDpi} DPI.");
                host.Close();
            }
            catch (Exception exception) { failure = exception; }
            finally { _ = SetThreadDpiAwarenessContext(previous); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "High-DPI combo chrome regression test timed out.");
        Assert.Null(failure);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [Fact]
    public void MessageWindow_UsesOwnerDeviceDpiForItsClientSize()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var owner = new Form { ClientSize = new Size(500, 300), StartPosition = FormStartPosition.CenterScreen };
                owner.Show();
                using var message = ModernMessage.Success(owner, "操作完成", 0);
                Application.DoEvents();
                var window = Assert.IsAssignableFrom<Form>(message);
                var scale = owner.DeviceDpi / 96f;
                Assert.True(window.ClientSize.Width >= (int)Math.Round(180 * scale),
                    $"Message width {window.ClientSize.Width} is not scaled for owner DPI {owner.DeviceDpi}.");
                Assert.True(window.ClientSize.Height >= (int)Math.Round(46 * scale),
                    $"Message height {window.ClientSize.Height} is not scaled for owner DPI {owner.DeviceDpi}.");
                Assert.NotNull(window.Region);
                Assert.False(window.Region.IsVisible(0, 0));
                Assert.True(window.Region.IsVisible(window.ClientSize.Width / 2, window.ClientSize.Height / 2));
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Message DPI layout regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Message_RemainsCenteredWhenOwnerMovesDuringEntranceAnimation()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = true;
                using var owner = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(120, 120, 760, 480)
                };
                owner.Show();
                using var message = ModernMessage.Success(owner, "Operation completed", 0);
                Application.DoEvents();
                owner.Top += 100;
                var until = DateTime.UtcNow.AddMilliseconds(300);
                while (DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                var window = Assert.IsAssignableFrom<Form>(message);
                var ownerBounds = owner.RectangleToScreen(owner.ClientRectangle);
                Assert.InRange(Math.Abs((window.Left + window.Width / 2) - (ownerBounds.Left + ownerBounds.Width / 2)), 0, 1);
                Assert.InRange(Math.Abs(window.Top - (ownerBounds.Top + 18)), 0, 1);
            }
            catch (Exception exception) { failure = exception; }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Message geometry regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Notification_RemainsRightAlignedWhenOwnerMovesDuringEntranceAnimation()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = true;
                using var owner = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(120, 120, 760, 480)
                };
                owner.Show();
                using var notification = ModernNotification.Show(owner, "DP.WorkFlow complete", "No errors were found.", ModernVisualStatus.Success, 0);
                Application.DoEvents();
                owner.Left += 140;
                var until = DateTime.UtcNow.AddMilliseconds(320);
                while (DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                var window = Assert.IsAssignableFrom<Form>(notification);
                var ownerBounds = owner.RectangleToScreen(owner.ClientRectangle);
                Assert.InRange(Math.Abs(window.Right - (ownerBounds.Right - 18)), 0, 1);
                Assert.NotNull(window.Region);
                Assert.False(window.Region.IsVisible(0, 0));
                Assert.True(window.Region.IsVisible(window.ClientSize.Width / 2, window.ClientSize.Height / 2));
            }
            catch (Exception exception) { failure = exception; }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Notification geometry regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Input_ErrorBorderHasSymmetricEdgeWeight()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var input = new ModernInput
                {
                    Size = new Size(220, 46),
                    Text = "Invalid value",
                    ValidationState = ModernValidationState.Error,
                    Theme = ModernTheme.Light
                };
                using var bitmap = new Bitmap(input.Width, input.Height);
                input.DrawToBitmap(bitmap, input.ClientRectangle);

                static int ErrorWeight(IEnumerable<Color> colors) => colors.Sum(color =>
                    color.R > 180 && color.R > color.G * 1.35 && color.R > color.B * 1.25
                        ? color.R - Math.Max(color.G, color.B)
                        : 0);
                var centerX = input.Width / 2;
                var centerY = input.Height / 2;
                var top = ErrorWeight(Enumerable.Range(0, input.Height / 2).Select(y => bitmap.GetPixel(centerX, y)));
                var bottom = ErrorWeight(Enumerable.Range(input.Height / 2, input.Height - input.Height / 2).Select(y => bitmap.GetPixel(centerX, y)));
                var left = ErrorWeight(Enumerable.Range(0, input.Width / 2).Select(x => bitmap.GetPixel(x, centerY)));
                var right = ErrorWeight(Enumerable.Range(input.Width / 2, input.Width - input.Width / 2).Select(x => bitmap.GetPixel(x, centerY)));
                Assert.True(Math.Abs(top - bottom) <= 35, $"top={top}, bottom={bottom}");
                Assert.True(Math.Abs(left - right) <= 35, $"left={left}, right={right}");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Input error border symmetry test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void ComboBox_ErrorTopBorderRemainsContinuousAcrossNativeArrowSurface()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var owner = new Form { ClientSize = new Size(420, 120) };
                using var combo = new ModernComboBox
                {
                    Bounds = new Rectangle(20, 20, 320, 40),
                    Text = "Invalid.Namespace",
                    ValidationState = ModernValidationState.Error,
                    Theme = ModernTheme.Light
                };
                owner.Controls.Add(combo);
                owner.Show();
                Application.DoEvents();
                using var bitmap = new Bitmap(combo.Width, combo.Height);
                combo.DrawToBitmap(bitmap, combo.ClientRectangle);

                for (var x = 16; x < combo.Width - 16; x++)
                {
                    var hasErrorInk = Enumerable.Range(0, Math.Min(7, combo.Height))
                        .Select(y => bitmap.GetPixel(x, y))
                        .Any(color => color.R > 190 && color.R > color.G * 1.8 && color.R > color.B * 1.6);
                    Assert.True(hasErrorInk, $"Error top border is interrupted at x={x}.");
                }
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "ComboBox error chrome regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void ModernCompositeControls_DoNotDoubleScaleDirectChildren()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            var controls = new List<Control>();
            try
            {
                controls.AddRange([
                    new ModernInput { Size = new Size(220, 34) },
                    new ModernTextArea { Size = new Size(260, 100) },
                    new ModernInputNumber { Size = new Size(220, 34) },
                    new ModernComboBox { Size = new Size(220, 34) },
                    new ModernDatePicker { Size = new Size(180, 34) },
                    new ModernDateRangePicker { Size = new Size(330, 34) },
                    new ModernTimePicker { Size = new Size(180, 34) },
                    new ModernDurationInput { Size = new Size(260, 34) },
                    new ModernPagination { Size = new Size(420, 42), TotalCount = 100 },
                    new ModernCollapsiblePanel { Size = new Size(280, 120) }
                ]);
                foreach (var control in controls)
                {
                    control.Scale(new SizeF(1.5f, 1.5f));
                    AssertChildrenInside(control);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { foreach (var control in controls) control.Dispose(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Composite DPI regression test timed out.");
        Assert.Null(failure);
    }

    private static void PumpMessages(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static void AssertChildrenInside(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            Assert.True(child.Left >= 0, $"{child.GetType().Name} starts outside {parent.GetType().Name}: {child.Bounds}");
            Assert.True(child.Top >= 0, $"{child.GetType().Name} starts above {parent.GetType().Name}: {child.Bounds}");
            Assert.True(child.Right <= parent.ClientSize.Width,
                $"{child.GetType().Name} exceeds {parent.GetType().Name}: child={child.Bounds}, parent={parent.ClientSize}");
            Assert.True(child.Bottom <= parent.ClientSize.Height,
                $"{child.GetType().Name} exceeds {parent.GetType().Name}: child={child.Bounds}, parent={parent.ClientSize}");
        }
    }
}
