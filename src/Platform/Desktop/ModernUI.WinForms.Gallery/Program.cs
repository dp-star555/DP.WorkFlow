using System.Globalization;
using System.Runtime.InteropServices;
using ModernUI.Localization;

namespace ModernUI.WinForms.Gallery;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        GalleryApplication.Initialize();
        GalleryApplication.ShowWithoutActivationForProbe = args.Contains("--no-activate-probe", StringComparer.OrdinalIgnoreCase);
        var deterministicSettingsProbe = args.Contains("--deterministic-settings-probe", StringComparer.OrdinalIgnoreCase);
        ModernUI.WinForms.ModernUiSettings.AnimationsEnabled =
            !args.Contains("--no-animation", StringComparer.OrdinalIgnoreCase);
        ModernUI.WinForms.ModernUiSettings.RespectSystemAnimationPreference = !deterministicSettingsProbe;
        ModernUI.WinForms.ModernUiSettings.RespectSystemHighContrast = !deterministicSettingsProbe;
        var visualStates = args.Contains("--visual-states", StringComparer.OrdinalIgnoreCase);
        var dark = args.Contains("--dark", StringComparer.OrdinalIgnoreCase);
        var highContrast = args.Contains("--high-contrast", StringComparer.OrdinalIgnoreCase);
        if (dark && highContrast) throw new ArgumentException("--dark and --high-contrast cannot be combined.", nameof(args));
        var rightToLeft = args.Contains("--rtl", StringComparer.OrdinalIgnoreCase);
        var english = args.Contains("--english", StringComparer.OrdinalIgnoreCase);
        var scrollArgument = args.FirstOrDefault(argument => argument.StartsWith("--scroll=", StringComparison.OrdinalIgnoreCase));
        var offsetText = GetOptionValue(scrollArgument);
        var scrollOffset = int.TryParse(offsetText, out var parsedOffset) ? Math.Max(0, parsedOffset) : 0;
        var theme = highContrast
            ? ModernUI.WinForms.ModernTheme.HighContrast
            : dark ? ModernUI.WinForms.ModernTheme.Dark : ModernUI.WinForms.ModernTheme.Light;
        var themeName = highContrast ? "HighContrast" : dark ? "Dark" : "Light";
        var initialLocation = ParseLocation(GetOptionValue(args.FirstOrDefault(argument =>
            argument.StartsWith("--location=", StringComparison.OrdinalIgnoreCase))));
        var demoName = GetOptionValue(args.FirstOrDefault(argument => argument.StartsWith("--demo=", StringComparison.OrdinalIgnoreCase)));
        if (!visualStates && Enum.TryParse<GalleryDemoCategory>(demoName, true, out var category))
        {
            var descriptor = GalleryDemoCatalog.All.Single(item => item.Category == category);
            var demo = new DemoCategoryForm(descriptor, theme);
            demo.SetInitialScrollOffset(scrollOffset);
            ApplyTextDirection(demo, rightToLeft);
            ApplyInitialLocation(demo, initialLocation);
            InstallDateProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--date-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallDateRangeFontProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--date-range-font-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallFeedbackProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--feedback-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallBusinessProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--business-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallContinuousFrameProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--frame-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallScrollFrameProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                argument.StartsWith("--scroll-frame-probe=", StringComparison.OrdinalIgnoreCase))));
            InstallSettingsMatrixProbe(demo, GetOptionValue(args.FirstOrDefault(argument =>
                    argument.StartsWith("--settings-matrix-probe=", StringComparison.OrdinalIgnoreCase))),
                theme, themeName, rightToLeft, ModernUI.WinForms.ModernUiSettings.AnimationsEnabled);
            Application.Run(demo);
            return;
        }

        var ownedDemoName = GetOptionValue(args.FirstOrDefault(argument => argument.StartsWith("--owned-demo=", StringComparison.OrdinalIgnoreCase)));
        if (!visualStates && Enum.TryParse<GalleryDemoCategory>(ownedDemoName, true, out var ownedCategory))
        {
            var gallery = new GalleryForm();
            var descriptor = GalleryDemoCatalog.All.Single(item => item.Category == ownedCategory);
            gallery.Shown += (_, _) =>
            {
                var timer = new System.Windows.Forms.Timer { Interval = 500 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    gallery.OpenDemo(descriptor);
                };
                timer.Start();
            };
            Application.Run(gallery);
            return;
        }

        Form root;
        if (visualStates)
        {
            root = new VisualStatesForm(theme, scrollOffset, english);
        }
        else
        {
            var gallery = new GalleryForm(theme);
            if (args.Contains("--theme-frame-probe", StringComparer.OrdinalIgnoreCase))
                gallery.ScheduleThemeFrameProbe();
            root = gallery;
        }
        ApplyTextDirection(root, rightToLeft);
        ApplyInitialLocation(root, initialLocation);
        Application.Run(root);
    }

    private static void ApplyTextDirection(Form form, bool rightToLeft)
    {
        if (!rightToLeft) return;
        form.RightToLeft = RightToLeft.Yes;
        form.RightToLeftLayout = true;
        var context = GalleryLocalization.CreateContext(
            CultureInfo.GetCultureInfo("zh-CN"), TextDirection.RightToLeft);
        ModernUI.WinForms.ModernUiSettings.ApplyLocalization(form, context);
    }

    private static Point? ParseLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value!.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y)
            ? new Point(x, y)
            : throw new ArgumentException($"Invalid Gallery location '{value}'. Expected x,y.", nameof(value));
    }

    private static void ApplyInitialLocation(Form form, Point? location)
    {
        if (location is not { } point) return;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = point;
    }

    private static void InstallDateRangeFontProbe(DemoCategoryForm demo, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        var path = DecodePath(outputPath!);
        demo.Shown += (_, _) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                try
                {
                    var range = Descendants(demo).OfType<ModernUI.WinForms.ModernDateRangePicker>().FirstOrDefault()
                        ?? throw new InvalidOperationException("Date range font probe requires the DateAndTime demo.");
                    var pickers = range.Controls.OfType<ModernUI.WinForms.ModernDatePicker>()
                        .OrderBy(picker => picker.Left).ToArray();
                    var lines = new List<string>();
                    ProbeDateRangeFontOrder(demo, pickers, new[] { 1, 0, 1 }, "right-left-right", lines);
                    ProbeDateRangeFontOrder(demo, pickers, new[] { 0, 1, 0 }, "left-right-left", lines);
                    File.WriteAllLines(path, lines);
                    demo.Text = "DATE_RANGE_FONT_PROBE_OK";
                }
                catch (Exception exception)
                {
                    File.WriteAllText(path, exception.ToString());
                    demo.Text = "DATE_RANGE_FONT_PROBE_FAILED";
                }
            };
            timer.Start();
        };
    }

    private static void ProbeDateRangeFontOrder(Form owner, ModernUI.WinForms.ModernDatePicker[] pickers,
        IEnumerable<int> order, string scenario, ICollection<string> lines)
    {
        foreach (var index in order)
        {
            var picker = pickers[index];
            picker.DroppedDown = true;
            Application.DoEvents();
            var popup = owner.OwnedForms.Single(form => form.Visible);
            var calendar = Descendants(popup).Single(control => control.GetType().Name == "ModernCalendarSurface");
            lines.Add($"{scenario}[{index}] owner={FontText(picker.Font)} native={FontText(picker.InnerPicker.Font)} " +
                $"calendar={FontText(calendar.Font)} dpi={picker.DeviceDpi}/{picker.InnerPicker.DeviceDpi}/" +
                $"{calendar.DeviceDpi}/{popup.DeviceDpi} popup={popup.ClientSize.Width}x{popup.ClientSize.Height}");
            picker.DroppedDown = false;
            Application.DoEvents();
        }
    }

    private static string DecodePath(string value) => Uri.UnescapeDataString(value);

    private static string FontText(Font font)
    {
        try { return $"{font.Name}:{font.SizeInPoints:0.##}pt:{font.Height}px"; }
        catch (Exception exception) { return $"INVALID({exception.GetType().Name})"; }
    }

    private static void InstallDateProbe(DemoCategoryForm demo, string? probe)
    {
        if (string.IsNullOrWhiteSpace(probe)) return;
        var probeName = probe!;
        demo.Shown += (_, _) => demo.BeginInvoke((Action)(() =>
        {
            var date = Descendants(demo).OfType<ModernUI.WinForms.ModernDatePicker>().FirstOrDefault()
                ?? throw new InvalidOperationException("Date probe requires the DateAndTime demo.");
            if (probeName.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                date.DroppedDown = true;
                return;
            }
            throw new ArgumentException($"Unknown date probe '{probeName}'.", nameof(probe));
        }));
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void InstallFeedbackProbe(Form owner, string? probe)
    {
        if (string.IsNullOrWhiteSpace(probe)) return;
        var probeName = probe!;
        owner.Shown += (_, _) => owner.BeginInvoke((Action)(() =>
        {
            if (probeName.Equals("message", StringComparison.OrdinalIgnoreCase))
                _ = ModernUI.WinForms.ModernMessage.Info(owner, "这是一条全局消息，用于确认完整真实文本不会被关闭按钮或窗口边界裁剪", 30000);
            else if (probeName.Equals("notification", StringComparison.OrdinalIgnoreCase))
                _ = ModernUI.WinForms.ModernNotification.Show(owner, "诊断完成", "发现 2 条可优化建议。",
                    ModernUI.WinForms.ModernVisualStatus.Primary, 30000);
            else if (probeName.Equals("tooltip", StringComparison.OrdinalIgnoreCase) ||
                     probeName.Equals("tooltip-short", StringComparison.OrdinalIgnoreCase))
            {
                var shortProbe = probeName.Equals("tooltip-short", StringComparison.OrdinalIgnoreCase);
                Control target = shortProbe
                    ? Descendants(owner).OfType<ModernUI.WinForms.ModernInput>().First(input => input.Text.Contains("192.168"))
                    : Descendants(owner).OfType<ModernUI.WinForms.ModernButton>().First(button => button.Text.Contains("ToolTip"));
                if (shortProbe)
                {
                    var provider = new ModernUI.WinForms.ModernValidationProvider();
                    owner.FormClosed += (_, _) => provider.Dispose();
                    provider.SetValidation(target, ModernUI.WinForms.ModernValidationState.Success, "地址可用");
                    provider.FocusInvalid(target);
                }
                else
                {
                    var toolTip = new ModernUI.WinForms.ModernToolTip
                    {
                        Placement = ModernUI.WinForms.ModernToolTipPlacement.Top,
                        MaximumWidth = 280
                    };
                    owner.FormClosed += (_, _) => toolTip.Dispose();
                    toolTip.Show(target,
                        "这是跟随锚点、支持自动翻转和 DPI 的现代 ToolTip。\n移动或滚动页面时会重新定位。",
                        duration: 30000);
                }
            }
            else
                throw new ArgumentException($"Unknown feedback probe '{probeName}'.", nameof(probe));
        }));
    }

    private static void InstallScrollFrameProbe(DemoCategoryForm demo, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        var path = DecodePath(outputPath!);
        demo.Shown += (_, _) =>
        {
            var delay = new System.Windows.Forms.Timer { Interval = 900 };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                delay.Dispose();
                try
                {
                    var viewport = Descendants(demo).OfType<ModernUI.WinForms.ModernScrollView>()
                        .OrderByDescending(scroll => Math.Max(0,
                            (scroll.Content?.Height ?? 0) - scroll.ClientSize.Height))
                        .FirstOrDefault()
                        ?? throw new InvalidOperationException("Scroll frame probe requires a scrollable demo.");
                    var contentHeight = Math.Max(1, viewport.Content?.Height ?? 1);
                    var maximumOffset = Math.Max(0, contentHeight - viewport.ClientSize.Height);
                    if (maximumOffset <= 0) throw new InvalidOperationException("The selected viewport cannot scroll.");

                    var scale = viewport.DeviceDpi / 96d;
                    var trackTop = Math.Max(1, (int)Math.Round(4 * scale));
                    var trackHeight = Math.Max(1d, viewport.ClientSize.Height - 8 * scale);
                    var thumbHeight = Math.Max(28 * scale,
                        trackHeight * viewport.ClientSize.Height / contentHeight);
                    thumbHeight = Math.Min(trackHeight, thumbHeight);
                    var startY = trackTop + (int)Math.Round(thumbHeight / 2d);
                    var endY = Math.Max(startY + 1,
                        viewport.ClientSize.Height - trackTop - (int)Math.Round(thumbHeight / 2d));
                    var x = viewport.ClientSize.Width - Math.Max(1,
                        (int)Math.Round(viewport.ScrollBarGutter * scale / 2d));
                    var observations = new List<string>();

                    SendMouse(viewport.Handle, 0x0201, 0x0001, x, startY);
                    var step = 0;
                    const int steps = 18;
                    var drag = new System.Windows.Forms.Timer { Interval = 16 };
                    drag.Tick += (_, _) =>
                    {
                        try
                        {
                            step++;
                            var y = startY + (int)Math.Round((endY - startY) * step / (double)steps);
                            SendMouse(viewport.Handle, 0x0200, 0x0001, x, y);
                            observations.Add($"{step}:{viewport.ScrollOffset}");
                            demo.Text = $"SCROLL_FRAME_PROBE step={step} offset={viewport.ScrollOffset}";
                            if (step < steps) return;
                            SendMouse(viewport.Handle, 0x0202, 0, x, y);
                            drag.Stop();
                            drag.Dispose();
                            var monotonic = observations.Select(value => int.Parse(value.Substring(value.IndexOf(':') + 1)))
                                .Zip(observations.Select(value => int.Parse(value.Substring(value.IndexOf(':') + 1))).Skip(1),
                                    (left, right) => right >= left).All(value => value);
                            if (!monotonic || viewport.ScrollOffset <= 0)
                                throw new InvalidOperationException("Scroll thumb drag did not advance monotonically.");
                            File.WriteAllText(path, "SCROLL_FRAME_PROBE_OK " + string.Join(",", observations));
                            demo.Text = $"SCROLL_FRAME_PROBE_OK offset={viewport.ScrollOffset}";
                        }
                        catch (Exception exception)
                        {
                            drag.Stop();
                            drag.Dispose();
                            File.WriteAllText(path, exception.ToString());
                            demo.Text = "SCROLL_FRAME_PROBE_FAILED";
                        }
                    };
                    drag.Start();
                }
                catch (Exception exception)
                {
                    File.WriteAllText(path, exception.ToString());
                    demo.Text = "SCROLL_FRAME_PROBE_FAILED";
                }
            };
            delay.Start();
        };
    }

    private static void InstallSettingsMatrixProbe(DemoCategoryForm demo, string? outputPath,
        ModernUI.WinForms.ModernTheme expectedTheme, string themeName, bool rightToLeft, bool animationsEnabled)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        var path = DecodePath(outputPath!);
        demo.Shown += (_, _) =>
        {
            var delay = new System.Windows.Forms.Timer { Interval = 900 };
            delay.Tick += (_, _) =>
            {
                delay.Stop();
                delay.Dispose();
                try
                {
                    var controls = Descendants(demo).ToArray();
                    var modernControls = controls.OfType<ModernUI.WinForms.ModernControl>().ToArray();
                    var expectedDirection = rightToLeft ? RightToLeft.Yes : RightToLeft.No;
                    if (demo.RightToLeft != expectedDirection || demo.RightToLeftLayout != rightToLeft)
                        throw new InvalidOperationException("The form did not apply the requested text direction.");
                    if (modernControls.Length == 0 || modernControls.Any(control => control.RightToLeft != expectedDirection))
                        throw new InvalidOperationException("Text direction did not reach the complete modern control tree.");
                    if (modernControls.Any(control => !ReferenceEquals(control.Theme, expectedTheme)))
                        throw new InvalidOperationException("Theme did not reach the complete modern control tree.");
                    if (ModernUI.WinForms.ModernUiSettings.EffectiveAnimationsEnabled != animationsEnabled)
                        throw new InvalidOperationException("Effective animation state does not match the matrix scenario.");

                    var select = controls.OfType<ModernUI.WinForms.ModernSelect>().Single();
                    var multiple = controls.OfType<ModernUI.WinForms.ModernSelectMultiple>().Single();
                    if (!Equals(select.SelectedValue, "continuous"))
                        throw new InvalidOperationException("Text direction or theme setup changed the selected business value.");
                    var selectedValues = multiple.SelectedValues
                        .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).ToArray();
                    var expectedValues = new[] { "raw", "result", "log", "thumbnail" };
                    if (!selectedValues.SequenceEqual(expectedValues, StringComparer.Ordinal))
                        throw new InvalidOperationException("Text direction or theme setup changed multi-select business values.");

                    select.DroppedDown = true;
                    Application.DoEvents();
                    var popup = demo.OwnedForms.SingleOrDefault(form => form.Visible)
                        ?? throw new InvalidOperationException("The settings matrix Select popup did not become visible.");
                    var anchor = select.RectangleToScreen(select.ClientRectangle);
                    var popupBounds = popup.Bounds;
                    var aligned = rightToLeft
                        ? popupBounds.Right == anchor.Right
                        : popupBounds.Left == anchor.Left;
                    if (!aligned)
                        throw new InvalidOperationException("The Select popup did not align to the requested text direction.");
                    if (popup.DeviceDpi != demo.DeviceDpi)
                        throw new InvalidOperationException("The Select popup did not inherit its owner's DPI.");

                    File.WriteAllText(path,
                        $"SETTINGS_MATRIX_PROBE_OK theme={themeName} direction={(rightToLeft ? "RTL" : "LTR")} " +
                        $"motion={(animationsEnabled ? "Animations" : "ReducedMotion")} dpi={demo.DeviceDpi} " +
                        $"anchor={anchor.Left},{anchor.Top},{anchor.Width},{anchor.Height} " +
                        $"popup={popupBounds.Left},{popupBounds.Top},{popupBounds.Width},{popupBounds.Height} " +
                        $"popupDpi={popup.DeviceDpi} value={select.SelectedValue} " +
                        $"values={string.Join(",", selectedValues)} modern={modernControls.Length} " +
                        $"systemHighContrast={SystemInformation.HighContrast} " +
                        $"colors={expectedTheme.Background.ToArgb()},{expectedTheme.Container.ToArgb()}," +
                        $"{expectedTheme.Text.ToArgb()},{expectedTheme.Primary.ToArgb()}");
                    demo.Text = $"SETTINGS_MATRIX_PROBE_OK theme={themeName} direction={(rightToLeft ? "RTL" : "LTR")}";
                }
                catch (Exception exception)
                {
                    File.WriteAllText(path, exception.ToString());
                    demo.Text = "SETTINGS_MATRIX_PROBE_FAILED";
                }
            };
            delay.Start();
        };
    }

    private static void SendMouse(IntPtr window, int message, int keyState, int x, int y)
    {
        var position = new IntPtr((y << 16) | (x & 0xFFFF));
        _ = SendMessage(window, message, new IntPtr(keyState), position);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private static void InstallContinuousFrameProbe(DemoCategoryForm demo, string? probe)
    {
        if (string.IsNullOrWhiteSpace(probe)) return;
        var probeName = probe!;
        demo.Shown += (_, _) =>
        {
            // Give the external collector enough time to discover the owner HWND before the first
            // popup becomes visible. No foreground or pointer manipulation is performed here.
            var timer = new System.Windows.Forms.Timer { Interval = 900 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (probeName.Equals("combobox", StringComparison.OrdinalIgnoreCase))
                {
                    var combo = Descendants(demo).OfType<ModernUI.WinForms.ModernComboBox>().FirstOrDefault()
                        ?? throw new InvalidOperationException("ComboBox frame probe requires the Inputs demo.");
                    combo.InnerComboBox.Focus();
                    combo.DroppedDown = true;
                }
                else if (probeName.Equals("select", StringComparison.OrdinalIgnoreCase))
                {
                    var select = Descendants(demo).OfType<ModernUI.WinForms.ModernSelect>().FirstOrDefault()
                        ?? throw new InvalidOperationException("Select frame probe requires the Selection demo.");
                    select.DroppedDown = true;
                }
                else if (probeName.Equals("datepicker", StringComparison.OrdinalIgnoreCase))
                {
                    var date = Descendants(demo).OfType<ModernUI.WinForms.ModernDatePicker>().FirstOrDefault()
                        ?? throw new InvalidOperationException("DatePicker frame probe requires the DateAndTime demo.");
                    date.DroppedDown = true;
                }
                else if (probeName.Equals("tooltip", StringComparison.OrdinalIgnoreCase))
                {
                    var target = Descendants(demo).OfType<ModernUI.WinForms.ModernButton>()
                        .FirstOrDefault(button => button.Text.Contains("ToolTip"))
                        ?? throw new InvalidOperationException("ToolTip frame probe requires the FeedbackAndOverlays demo.");
                    var toolTip = new ModernUI.WinForms.ModernToolTip
                    {
                        Placement = ModernUI.WinForms.ModernToolTipPlacement.Top,
                        MaximumWidth = 280,
                        AutoPopDelay = 30000
                    };
                    demo.FormClosed += (_, _) => toolTip.Dispose();
                    toolTip.Show(target,
                        "这是跟随锚点、支持自动翻转和 DPI 的现代 ToolTip。\n移动或滚动页面时会重新定位。",
                        duration: 30000);
                }
                else
                {
                    throw new ArgumentException($"Unknown frame probe '{probeName}'.", nameof(probe));
                }
            };
            timer.Start();
        };
    }

    private static void InstallBusinessProbe(DemoCategoryForm demo, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        var path = DecodePath(outputPath!);
        demo.Shown += async (_, _) =>
        {
            try
            {
                var controls = Descendants(demo).ToArray();
                var name = controls.OfType<ModernUI.WinForms.ModernInput>()
                    .Single(control => control.AccessibleName == "设备名称");
                var address = controls.OfType<ModernUI.WinForms.ModernInput>()
                    .Single(control => control.AccessibleName == "IPv4 地址");
                ModernUI.WinForms.ModernButton Button(string text) => Descendants(demo)
                    .OfType<ModernUI.WinForms.ModernButton>().Single(button => button.Text == text);
                var summary = controls.OfType<ModernUI.WinForms.ModernValidationSummary>().Single();
                var alert = controls.OfType<ModernUI.WinForms.ModernAlert>().Single();

                name.Text = string.Empty;
                Button("保存配置").PerformClick();
                await WaitUntilAsync(() => Button("English").Enabled && summary.Provider?.Results.Any(result =>
                    ReferenceEquals(result.Control, name) && result.State == ModernUI.WinForms.ModernValidationState.Error) == true);

                name.Text = "Inspection Camera";
                address.Text = "192.168.1.42";
                Button("English").PerformClick();
                await WaitUntilAsync(() => Descendants(demo).OfType<ModernUI.WinForms.ModernButton>()
                    .Any(button => button.Text == "中文"));
                if (name.Text != "Inspection Camera" || address.Text != "192.168.1.42")
                    throw new InvalidOperationException("Locale switch changed business input values.");

                Button("Save configuration").PerformClick();
                await WaitUntilAsync(() => alert.Text == "Configuration saved" && Button("Test connection").Enabled);
                name.Text = "Unsaved Camera";
                Button("Restore saved values").PerformClick();
                if (name.Text != "Inspection Camera")
                    throw new InvalidOperationException("Restore did not recover the saved snapshot.");

                Button("Test connection").PerformClick();
                await WaitUntilAsync(() => alert.Text == "Device connection succeeded", timeoutMilliseconds: 4000);
                File.WriteAllText(path,
                    $"BUSINESS_PROBE_OK name={name.Text} address={address.Text} culture=en-US status={alert.Text}");
                demo.Text = "BUSINESS_PROBE_OK";
            }
            catch (Exception exception)
            {
                File.WriteAllText(path, exception.ToString());
                demo.Text = "BUSINESS_PROBE_FAILED";
            }
        };
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds = 2500)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds) throw new TimeoutException("Business probe timed out.");
            await Task.Delay(25);
        }
    }

    private static string? GetOptionValue(string? argument)
    {
        if (argument is null) return null;
        var separator = argument.IndexOf('=');
        return separator < 0 || separator == argument.Length - 1 ? null : argument.Substring(separator + 1);
    }
}
