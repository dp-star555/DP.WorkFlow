using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Drawing.Design;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class ModernPropertyGridRenderingTests
{
    [Fact]
    public void PublicControls_CanBeCreatedByWinFormsToolbox()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                Type[] controlTypes =
                [
                    typeof(ModernButton), typeof(ModernInput), typeof(ModernInputNumber),
                    typeof(ModernSelect), typeof(ModernSelectMultiple), typeof(ModernCheckbox),
                    typeof(ModernSwitch), typeof(ModernPanel), typeof(ModernScrollView),
                    typeof(ModernSplitter), typeof(ModernPropertyGrid.WinForms.ModernPropertyGrid)
                ];
                foreach (var type in controlTypes)
                {
                    var item = new ToolboxItem(type);
                    var components = item.CreateComponents();
                    Assert.Single(components);
                    var control = Assert.IsAssignableFrom<Control>(components[0]);
                    foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(control))
                    {
                        if (!property.IsBrowsable) continue;
                        _ = property.GetValue(control);
                        if (property.ComponentType == type)
                            Assert.False(string.IsNullOrWhiteSpace(property.Description),
                                $"{type.Name}.{property.Name} 缺少设计器属性说明。");
                    }
                    foreach (var component in components) component.Dispose();
                }

                var toolTipComponents = new ToolboxItem(typeof(ModernToolTip)).CreateComponents();
                Assert.Single(toolTipComponents);
                Assert.IsType<ModernToolTip>(toolTipComponents[0]);
                toolTipComponents[0].Dispose();

                using (var designSurface = new DesignSurface(typeof(Form)))
                {
                    var host = Assert.IsAssignableFrom<IDesignerHost>(designSurface.GetService(typeof(IDesignerHost)));
                    foreach (var type in controlTypes)
                        Assert.IsAssignableFrom<Control>(host.CreateComponent(type));
                    Assert.IsType<ModernToolTip>(host.CreateComponent(typeof(ModernToolTip)));
                }

                using (var toolTipHost = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(120, 120) })
                using (var toolTipTarget = new Button { Location = new Point(20, 20), Size = new Size(100, 30) })
                using (var toolTip = new ModernToolTip())
                {
                    toolTipHost.Controls.Add(toolTipTarget);
                    toolTipHost.Show();
                    toolTip.Show(toolTipTarget, "跟随窗体移动", duration: 0);
                    Application.DoEvents();
                    var popup = Assert.IsAssignableFrom<Form>(typeof(ModernToolTip)
                        .GetField("_popup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(toolTip));
                    var originalPopupLocation = popup.Location;
                    Assert.False(popup.Region!.IsVisible(0, 0));
                    Assert.True(popup.Region.IsVisible(popup.ClientSize.Width / 2, popup.ClientSize.Height / 2));
                    using (var bitmap = CaptureWindow(popup))
                    {
                        Assert.True(bitmap.Width >= 80);
                        Assert.True(bitmap.Height >= 32);
                        Assert.True(CountNonBackgroundPixels(bitmap, popup.BackColor) > 40,
                            "ToolTip final HWND does not contain a readable text/outline frame.");
                    }
                    toolTipHost.Left += 40;
                    Application.DoEvents();
                    Assert.Equal(originalPopupLocation.X + 40, popup.Left);
                }

                using var input = new ModernInput();
                Assert.Equal(input.Padding.Left, input.InnerTextBox.Left);
                Assert.True(input.InnerTextBox.Top >= input.Padding.Top);
                Assert.True(input.InnerTextBox.Right <= input.ClientSize.Width - input.Padding.Right);

                using var number = new ModernInputNumber();
                var increase = number.Controls.OfType<ModernButton>().Single(button => button.Icon == ModernIconKind.Plus);
                var decrease = number.Controls.OfType<ModernButton>().Single(button => button.Icon == ModernIconKind.Minus);
                Assert.Equal(increase.Left, decrease.Left);
                Assert.Equal(increase.Bottom, decrease.Top);
                Assert.Empty(increase.Text);
                Assert.Empty(decrease.Text);
                var numberInput = number.Controls.OfType<ModernInput>().Single();
                Assert.Equal("0.00", numberInput.Text);
                Assert.All(number.Controls.OfType<ModernButton>(), button => Assert.True(button.StrongHoverFeedback));
                numberInput.Text = "not-a-number";
                typeof(Control).GetMethod("OnValidated", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(numberInput.InnerTextBox, [EventArgs.Empty]);
                Assert.True(numberInput.HasError);
                numberInput.Text = "12.50";
                Assert.False(numberInput.HasError);

                using var scrollView = new ModernScrollView();
                using var content = new Panel();
                scrollView.Controls.Add(content);
                Assert.Same(content, scrollView.Content);

                using var splitter = new ModernSplitter();
                splitter.Panel1.Controls.Add(new Panel());
                splitter.Panel2.Controls.Add(new Panel());
                Assert.Single(splitter.Panel1.Controls);
                Assert.Single(splitter.Panel2.Controls);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Toolbox creation test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void CompactPropertyGrid_KeepsDetailsSplitterPanelsInsideItsFirstFrame()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(440, 240), ShowInTaskbar = false };
                using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
                {
                    Bounds = new Rectangle(10, 10, 400, 190),
                    SelectedObject = new RenderingFixture()
                };
                host.Controls.Add(grid);
                host.Show();
                Application.DoEvents();

                var splitter = Descendants(grid).OfType<ModernSplitter>().Single();
                Assert.True(splitter.Panel1.Top >= 0 && splitter.Panel1.Bottom <= splitter.ClientSize.Height &&
                            splitter.Panel1.ClientSize.Height <= splitter.ClientSize.Height,
                    $"Panel1 escapes compact property grid: panel={splitter.Panel1.Bounds}/{splitter.Panel1.ClientSize}, client={splitter.ClientSize}.");
                Assert.True(splitter.Panel2.Top >= 0 && splitter.Panel2.Bottom <= splitter.ClientSize.Height &&
                            splitter.Panel2.ClientSize.Height <= splitter.ClientSize.Height,
                    $"Panel2 escapes compact property grid: panel={splitter.Panel2.Bounds}/{splitter.Panel2.ClientSize}, client={splitter.ClientSize}.");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Compact property-grid layout test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void LayoutContainers_AreDoubleBuffered_AndValueRefreshKeepsEditorInstance()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                // 该高度使分类展开时需要滚动条、折叠后不需要，用于覆盖临界布局。
                using var host = new Form { ClientSize = new Size(520, 280) };
                var model = new RenderingFixture();
                using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
                {
                    Dock = DockStyle.Fill,
                    SelectedObject = model
                };
                host.Controls.Add(grid);
                host.CreateControl();
                grid.CreateControl();
                host.Show();
                host.PerformLayout();
                Application.DoEvents();

                ModernUiSettings.ApplyTheme(host, ModernTheme.Light);
                Application.DoEvents();
                var propertyRow = Descendants(grid).First(control => control.GetType().Name == "PropertyRowPanel");
                using (var rowImage = new Bitmap(propertyRow.Width, propertyRow.Height))
                {
                    propertyRow.DrawToBitmap(rowImage, propertyRow.ClientRectangle);
                    Assert.Equal(ModernTheme.Light.Background.ToArgb(), rowImage.GetPixel(0, 0).ToArgb());
                    Assert.Equal(
                        ModernTheme.Light.Container.ToArgb(),
                        rowImage.GetPixel(rowImage.Width / 2, rowImage.Height - 1).ToArgb());
                }

                var layouts = Descendants(grid).Where(control => control is TableLayoutPanel or FlowLayoutPanel).ToArray();
                Assert.NotEmpty(layouts);
                Assert.All(layouts, control => Assert.True(GetStyle(control, ControlStyles.OptimizedDoubleBuffer), control.GetType().FullName));
                var splitter = Descendants(grid).OfType<ModernSplitter>().Single();
                Assert.Equal(Orientation.Horizontal, splitter.Orientation);
                var originalDistance = splitter.SplitterDistance;
                var originalDetailsHeight = splitter.Panel2.Height;
                splitter.SplitterDistance = originalDistance - 12;
                splitter.PerformLayout();
                Assert.True(splitter.Panel2.Height > originalDetailsHeight,
                    $"Programmatic splitter move did not grow details: distance {originalDistance}->{splitter.SplitterDistance}, height {originalDetailsHeight}->{splitter.Panel2.Height}.");
                splitter.SplitterDistance = originalDistance;

                var scrollViewport = Descendants(grid).OfType<ModernScrollView>().Single();
                Assert.True(GetStyle(scrollViewport, ControlStyles.OptimizedDoubleBuffer));
                Assert.True(scrollViewport.Content!.Height > scrollViewport.ClientSize.Height);
                var multiSelect = Descendants(grid).OfType<ModernSelectMultiple>().Single();
                multiSelect.ToggleDropDown();
                Application.DoEvents();
                Assert.True(multiSelect.DroppedDown);
                typeof(Control).GetMethod("OnSizeChanged", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(multiSelect.Parent, [EventArgs.Empty]);
                Assert.True(multiSelect.DroppedDown,
                    "不改变锚点屏幕边界的冗余布局事件不能关闭多选下拉层。");
                var dropDown = typeof(ModernSelectMultiple)
                    .GetField("_dropDown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(multiSelect)!;

                var multiSelectSurface = Assert.IsAssignableFrom<Control>(typeof(ModernSelectMultiple)
                    .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(multiSelect));
                var filter = Assert.IsAssignableFrom<IMessageFilter>(dropDown);
                var popupClick = Message.Create(multiSelectSurface.Handle, 0x0201, IntPtr.Zero, IntPtr.Zero);
                var oldCursorPosition = Cursor.Position;
                try
                {
                    Cursor.Position = new Point(SystemInformation.VirtualScreen.Right - 1, SystemInformation.VirtualScreen.Bottom - 1);
                    filter.PreFilterMessage(ref popupClick);
                }
                finally { Cursor.Position = oldCursorPosition; }
                Assert.True((bool)dropDown.GetType().GetProperty("Visible")!.GetValue(dropDown)!,
                    "发给下拉层子控件的点击消息不能被误判为外部点击。");

                typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(multiSelectSurface, [new MouseEventArgs(MouseButtons.Left, 1, 12, 12, 0)]);
                Application.DoEvents();
                Assert.True((bool)dropDown.GetType().GetProperty("Visible")!.GetValue(dropDown)!,
                    "多选下拉选择一项后必须保持展开。 ");

                scrollViewport.ScrollOffset = 24;
                Assert.True(scrollViewport.ScrollOffset > 0);
                Assert.False((bool)dropDown.GetType().GetProperty("Visible")!.GetValue(dropDown)!);
                scrollViewport.ScrollOffset = 0;

                var editor = Descendants(grid).OfType<ModernInput>()
                    .Single(input => input.AccessibleName == "Name");
                var numberEditor = Descendants(grid).OfType<ModernInputNumber>().Single();
                Assert.Equal(editor.Left, numberEditor.Left);
                var unitLabel = Descendants(grid).OfType<Label>().Single(label => label.Text == "ms");
                Assert.True(unitLabel.Width < 52, $"短单位不应占用固定的 52px，实际宽度为 {unitLabel.Width}px。");
                var rowWidth = editor.Parent!.Width;
                Assert.Equal(2, multiSelect.SelectedItems.Count);

                ModernInput FindNameEditor() => Descendants(grid).OfType<ModernInput>()
                    .Single(input => input.AccessibleName == "Name");
                grid.Theme = ModernTheme.Dark;
                Application.DoEvents();
                Assert.Same(editor, FindNameEditor());
                Assert.Same(ModernTheme.Dark, editor.Theme);
                grid.Theme = ModernTheme.Light;
                Application.DoEvents();
                Assert.Same(editor, FindNameEditor());

                var category = Descendants(grid).OfType<ModernButton>().First(button => button.Text.Contains("General", StringComparison.Ordinal));
                category.PerformClick();
                Application.DoEvents();
                Assert.Same(category, Descendants(grid).OfType<ModernButton>().First(button => button.Text.Contains("General", StringComparison.Ordinal)));
                Assert.Same(editor, FindNameEditor());
                PumpMessages(TimeSpan.FromMilliseconds(450));
                Assert.False(editor.Visible);
                Assert.Equal(rowWidth, editor.Parent!.Width);
                category.PerformClick();
                PumpMessages(TimeSpan.FromMilliseconds(450));
                Assert.True(editor.Visible);
                Assert.Equal(rowWidth, editor.Parent!.Width);
                Assert.Same(editor, FindNameEditor());

                model.Name = "Changed externally";
                Application.DoEvents();
                Assert.Same(editor, FindNameEditor());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Rendering regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void ThemeTransition_Reveal_AppliesThemeAndRemovesOverlayAfterAnimation()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(360, 240), BackColor = Color.Magenta };
                using var panel = new ModernPanel { Bounds = new Rectangle(20, 20, 300, 180) };
                host.Controls.Add(panel);
                host.Show();
                host.Activate();
                host.BringToFront();
                PumpMessages(TimeSpan.FromMilliseconds(250));
                var originalControlCount = host.Controls.Count;
                var originalOpenFormCount = Application.OpenForms.Count;

                ModernThemeTransition.Reveal(host, new Point(host.ClientSize.Width, 0),
                    () => ModernUiSettings.ApplyTheme(host, ModernTheme.Dark), 40);

                var overlay = Assert.Single(host.OwnedForms);
                Assert.Same(ModernTheme.Dark, panel.Theme);
                Assert.Equal(originalControlCount, host.Controls.Count);
                Assert.Equal(originalOpenFormCount + 1, Application.OpenForms.Count);
                Assert.Equal(host.ClientSize, overlay.ClientSize);
                PumpMessages(TimeSpan.FromMilliseconds(180));
                Assert.Equal(originalOpenFormCount, Application.OpenForms.Count);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        Assert.Null(failure);
    }

    [Fact]
    public void ThemeTransition_Reveal_KeepsStableRectangularOverlayDuringAnimation()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(360, 240) };
                host.Show();
                Application.DoEvents();

                ModernThemeTransition.Reveal(host, new Point(340, 20),
                    () => ModernUiSettings.ApplyTheme(host, ModernTheme.Dark), 300);
                PumpMessages(TimeSpan.FromMilliseconds(60));

                var overlay = Assert.Single(host.OwnedForms);
                Assert.Null(overlay.Region);
                Assert.Equal(FormBorderStyle.None, overlay.FormBorderStyle);
                PumpMessages(TimeSpan.FromMilliseconds(400));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        Assert.Null(failure);
    }

    [Fact]
    public void ThemeTransition_RapidToggle_KeepsOnlyOneOverlay()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(360, 240) };
                host.Show();
                Application.DoEvents();
                var originalControlCount = host.Controls.Count;
                var originalOpenFormCount = Application.OpenForms.Count;

                ModernThemeTransition.Reveal(host, new Point(340, 20),
                    () => ModernUiSettings.ApplyTheme(host, ModernTheme.Dark), 300);
                ModernThemeTransition.Reveal(host, new Point(340, 20),
                    () => ModernUiSettings.ApplyTheme(host, ModernTheme.Light), 300);

                Assert.Equal(originalControlCount, host.Controls.Count);
                Assert.Equal(originalOpenFormCount + 1, Application.OpenForms.Count);
                PumpMessages(TimeSpan.FromMilliseconds(500));
                Assert.Equal(originalOpenFormCount, Application.OpenForms.Count);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        Assert.Null(failure);
    }

    [Fact]
    public void ApplyTheme_NestedModernControls_ReceivesSameTheme()
    {
        using var root = new Panel();
        using var card = new ModernPanel();
        using var nestedPanel = new Panel();
        using var button = new ModernButton();
        using var input = new ModernInput();
        nestedPanel.Controls.Add(button);
        card.Controls.Add(nestedPanel);
        card.Controls.Add(input);
        root.Controls.Add(card);

        ModernUiSettings.ApplyTheme(root, ModernTheme.Dark);

        Assert.Same(ModernTheme.Dark, card.Theme);
        Assert.Same(ModernTheme.Dark, button.Theme);
        Assert.Same(ModernTheme.Dark, input.Theme);
    }

    [Fact]
    public void PropertyGrid_UsesUnifiedModernTheme()
    {
        using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
        {
            Theme = ModernTheme.Light
        };

        Assert.Same(ModernTheme.Light, grid.Theme);
    }

    [Fact]
    public void ModernScrollView_HeightOnlyResize_DoesNotRemeasureContentOnEveryFrame()
    {
        using var scrollView = new ModernScrollView { Size = new Size(420, 300) };
        using var content = new PreferredSizeCountingPanel { PreferredContentHeight = 900 };
        scrollView.Content = content;
        scrollView.PerformLayout();
        var baseline = content.MeasureCount;

        for (var height = 301; height <= 360; height++)
        {
            scrollView.Height = height;
            scrollView.PerformLayout();
        }

        Assert.InRange(content.MeasureCount - baseline, 0, 1);
    }

    [Fact]
    public void ModernScrollView_ContentSizeBurst_CoalescesPreferredSizeMeasurement()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var form = new Form { ClientSize = new Size(500, 360), ShowInTaskbar = false };
                using var scrollView = new ModernScrollView { Dock = DockStyle.Fill };
                using var content = new PreferredSizeCountingPanel { PreferredContentHeight = 900 };
                scrollView.Content = content;
                form.Controls.Add(scrollView);
                form.Show();
                Application.DoEvents();
                var baseline = content.MeasureCount;

                for (var index = 0; index < 20; index++)
                    content.RaiseSizeChanged();

                Assert.Equal(baseline, content.MeasureCount);
                PumpMessages(TimeSpan.FromMilliseconds(50));
                Assert.Equal(1, content.MeasureCount - baseline);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(3)));
        Assert.Null(failure);
    }

    [Fact]
    public void ModernInput_TreatsPointerOverInnerTextBoxAsHovered()
    {
        using var input = new HoverProbeInput();
        typeof(Control).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(input.InnerTextBox, [EventArgs.Empty]);
        Assert.True(input.Hovered);
    }

    [Fact]
    public void ModernMultiSelect_UsesFullyOwnerDrawnListInsteadOfNativeCheckedListBox()
    {
        using var select = new ModernSelectMultiple();
        var list = Assert.IsAssignableFrom<ListBox>(typeof(ModernSelectMultiple)
            .GetField("_list", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(select));
        Assert.IsNotType<CheckedListBox>(list);
        var surface = Assert.IsAssignableFrom<Control>(typeof(ModernSelectMultiple)
            .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(select));
        Assert.True(GetStyle(surface, ControlStyles.UserPaint));
        Assert.True(GetStyle(surface, ControlStyles.OptimizedDoubleBuffer));
        select.Items.AddRange(["First", "Second"]);
        select.SetItemChecked(0, true);
        Assert.Equal(["First"], select.SelectedItems);
    }

    [Fact]
    public void ModernSelect_CanSelectBeforeDropDownIsOpened()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var select = new ModernSelect();
                select.Items.AddRange(["First", "Second"]);
                select.SelectedIndex = 0;
                Assert.Equal("First", select.SelectedItem);
                var surface = Assert.IsAssignableFrom<Control>(typeof(ModernSelect)
                    .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(select));
                Assert.True(GetStyle(surface, ControlStyles.UserPaint));
                Assert.True(GetStyle(surface, ControlStyles.OptimizedDoubleBuffer));
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)));
        Assert.Null(failure);
    }

    [Fact]
    public void ModernPropertyGrid_UsesOneDpiScaleForItsPropertyRows()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var targetScreen = Screen.AllScreens
                    .Select(screen =>
                    {
                        using var probe = new Form
                        {
                            StartPosition = FormStartPosition.Manual,
                            Bounds = new Rectangle(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20, 120, 80),
                            ShowInTaskbar = false
                        };
                        probe.Show();
                        var dpi = probe.DeviceDpi;
                        probe.Close();
                        return (Screen: screen, Dpi: dpi);
                    })
                    .OrderBy(candidate => candidate.Dpi)
                    .First().Screen;
                using var host = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(targetScreen.WorkingArea.Left + 30, targetScreen.WorkingArea.Top + 30),
                    ClientSize = new Size(800, 520),
                    ShowInTaskbar = false
                };
                using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
                {
                    Dock = DockStyle.Fill,
                    SelectedObject = new RenderingFixture()
                };
                host.Controls.Add(grid);
                host.Show();
                PumpMessages(TimeSpan.FromMilliseconds(100));

                Assert.Equal(AutoScaleMode.Dpi, grid.AutoScaleMode);
                var row = Descendants(grid).OfType<TableLayoutPanel>()
                    .First(control => control.Tag is PropertyDescriptor);
                var expectedHeight = (int)Math.Round(ModernTheme.Light.PropertyRowHeight * grid.DeviceDpi / 96d);
                Assert.Equal(expectedHeight, row.Height);
                Assert.Equal(grid.ClientSize, grid.Controls.Cast<Control>().Single().ClientSize);
                host.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "PropertyGrid DPI regression test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void PropertyGrid_CommitPendingEditFlushesTextWithoutFocusTransition()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(520, 360), ShowInTaskbar = false };
                using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid { Dock = DockStyle.Fill };
                var fixture = new RenderingFixture();
                grid.SelectedObject = fixture;
                host.Controls.Add(grid);
                host.Show();
                PumpMessages(TimeSpan.FromMilliseconds(100));

                var editor = Descendants(grid).OfType<ModernInput>().Single(input => input.Text == "Camera");
                editor.Text = "LineCamera";
                editor.InnerTextBox.Focus();
                Assert.Equal("Camera", fixture.Name);

                Assert.True(grid.CommitPendingEdit());
                Assert.Equal("LineCamera", fixture.Name);
                host.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "PropertyGrid pending text commit test timed out.");
        Assert.Null(failure);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void PumpMessages(TimeSpan duration)
    {
        var end = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static Bitmap CaptureWindow(Control control)
    {
        var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var context = graphics.GetHdc();
        try { Assert.True(PrintWindow(control.Handle, context, 2)); }
        finally { graphics.ReleaseHdc(context); }
        return bitmap;
    }

    private static int CountNonBackgroundPixels(Bitmap bitmap, Color background)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) +
                Math.Abs(pixel.B - background.B) > 18) count++;
        }
        return count;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    private static bool GetStyle(Control control, ControlStyles style) =>
        (bool)(typeof(Control).GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [style]) ?? false);

    private sealed class HoverProbeInput : ModernInput
    {
        public bool Hovered => IsHovered;
    }

    private sealed class PreferredSizeCountingPanel : Panel
    {
        public int PreferredContentHeight { get; init; }
        public int MeasureCount { get; private set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            MeasureCount++;
            return new Size(proposedSize.Width, PreferredContentHeight);
        }

        public void RaiseSizeChanged() => OnSizeChanged(EventArgs.Empty);
    }

    private sealed class RenderingFixture : INotifyPropertyChanged
    {
        private string _name = "Camera";
        [Category("General")]
        public string Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
        [Category("General")]
        [PropertyMultiSelect("Image", "Result", "Log")]
        public string[] Outputs { get; set; } = ["Image", "Result"];

        [Category("General")]
        [PropertyUnit("ms")]
        [PropertyRange(0, 10000, 1, 0)]
        public int Timeout { get; set; } = 3000;

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
