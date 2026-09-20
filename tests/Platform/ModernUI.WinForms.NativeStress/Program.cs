using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ModernUI.WinForms;

namespace ModernUI.WinForms.NativeStress;

internal static class Program
{
    private const uint GwOwner = 4;
    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
#if NET48
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#else
            ApplicationConfiguration.Initialize();
#endif
            var iterations = ParseIterations(args);
            Run(iterations);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static int ParseIterations(string[] args)
    {
        var argument = args.FirstOrDefault(value => value.StartsWith("--iterations=", StringComparison.OrdinalIgnoreCase));
        if (argument is null) return 120;
        var value = argument.Substring(argument.IndexOf('=') + 1);
        if (!int.TryParse(value, out var iterations) || iterations is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(args), value, "Iterations must be between 1 and 5000.");
        return iterations;
    }

    private static void Run(int iterations)
    {
        using var host = new Form
        {
            Text = "ModernUI native stress",
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(120, 120, 720, 420),
            ShowInTaskbar = false
        };
        host.Show();
        PumpMessages(3);
        VerifyGridEditorTypographyAcrossScreens();
        var baselineTopLevel = EnumerateThreadWindows().ToHashSet();

        WarmUpComboBox(host);
        WarmUpGrid(host);
        CollectAndPump();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var handlesBefore = process.HandleCount;
        var gdiBefore = GetGuiResources(process.Handle, GdiObjects);
        var userBefore = GetGuiResources(process.Handle, UserObjects);

        var backgroundClosures = 0;
        for (var index = 0; index < iterations; index++)
        {
            if (StressComboBox(host, index)) backgroundClosures++;
            StressImmediateComboBoxDisposal(host);
            StressGrid(host, index);
            StressMultiSelectGrid(host, index);
            if ((index + 1) % 20 == 0) CollectAndPump();
        }

        CollectAndPump();
        process.Refresh();
        var handleGrowth = process.HandleCount - handlesBefore;
        var gdiGrowth = (int)GetGuiResources(process.Handle, GdiObjects) - (int)gdiBefore;
        var userGrowth = (int)GetGuiResources(process.Handle, UserObjects) - (int)userBefore;
        AssertNoUnexpectedVisibleTopLevelWindows(host, baselineTopLevel);
        AssertAtMost("process handle growth", handleGrowth, 32);
        AssertAtMost("GDI object growth", gdiGrowth, 8);
        AssertAtMost("USER object growth", userGrowth, 8);

        Console.WriteLine($"NATIVE_STRESS_OK iterations={iterations} handles={handleGrowth} gdi={gdiGrowth} user={userGrowth} backgroundClosures={backgroundClosures}");
        host.Close();
        PumpMessages(2);
    }

    private static void VerifyGridEditorTypographyAcrossScreens()
    {
        foreach (var screen in Screen.AllScreens)
        {
            using var font = new Font("Microsoft YaHei UI", 9F);
            using var form = new Form
            {
                AutoScaleMode = AutoScaleMode.None,
                Font = font,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(
                    screen.WorkingArea.Left + 24,
                    screen.WorkingArea.Top + 24,
                    640,
                    240)
            };
            using var grid = new ModernDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                EditMode = DataGridViewEditMode.EditProgrammatically
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", Width = 220 });
            grid.Columns.Add(new ModernDataGridViewComboBoxColumn
            {
                Name = "Model",
                Width = 280,
                DataSource = new[] { "Modbus.Generic", "Modbus.Inovance.H5U" }
            });
            grid.Rows.Add("DemoPlc", "Modbus.Generic");
            form.Controls.Add(grid);
            // Match the InteractionDemo bootstrap: compose at logical size, then establish its
            // explicit DPI baseline from the HWND's monitor during Load.
            form.Load += (_, _) =>
            {
                var dpi = Math.Max(96, form.DeviceDpi);
                var scale = dpi / 96F;
                if (Math.Abs(scale - 1F) > .001F) form.Scale(new SizeF(scale, scale));
                form.AutoScaleDimensions = new SizeF(dpi, dpi);
                form.AutoScaleMode = AutoScaleMode.Dpi;
            };
            form.Show();
            PumpMessages(3);

            for (var cycle = 0; cycle < 3; cycle++)
            {
                VerifyEditorFont(grid, grid.Rows[0].Cells[0], screen, cycle, "text");
                VerifyEditorFont(grid, grid.Rows[0].Cells[1], screen, cycle, "select");
            }

            form.Close();
            PumpMessages(2);
        }
    }

    private static void VerifyEditorFont(
        ModernDataGridView grid,
        DataGridViewCell cell,
        Screen screen,
        int cycle,
        string kind)
    {
        grid.CurrentCell = cell;
        if (!grid.BeginEdit(true))
            throw new InvalidOperationException($"DataGridView {kind} editor did not start on {screen.DeviceName}.");
        var editor = grid.EditingControl
            ?? throw new InvalidOperationException($"DataGridView {kind} editor was not created on {screen.DeviceName}.");
        // The real cell click opens the select synchronously, before the grid's deferred final-DPI
        // correction runs. This ordering is required to catch stale popup typography.
        if (kind == "select" && editor is ModernDataGridViewSelectEditingControl select)
            select.DroppedDown = true;
        PumpMessages(2);

        var expected = cell.InheritedStyle.Font;
        if (expected is null || Math.Abs(editor.Font.SizeInPoints - expected.SizeInPoints) > 0.01F)
            throw new InvalidOperationException(
                $"DataGridView {kind} editor font changed after selection. " +
                $"screen={screen.DeviceName} dpi={grid.DeviceDpi} cycle={cycle} " +
                $"cell={expected?.SizeInPoints:F3}pt editor={editor.Font.SizeInPoints:F3}pt.");

        if (editor is ModernDataGridViewSelectEditingControl opened)
        {
            var surface = (Control)(typeof(ModernSelect)
                .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(opened)
                ?? throw new InvalidOperationException("ModernSelect popup surface was not available."));
            var expectedPopupPoints = expected.SizeInPoints * editor.DeviceDpi / Math.Max(1F, surface.DeviceDpi);
            if (Math.Abs(surface.Font.SizeInPoints - expectedPopupPoints) > 0.01F)
                throw new InvalidOperationException(
                    $"DataGridView popup font changed after selection. " +
                    $"screen={screen.DeviceName} grid/editor/popupDpi={grid.DeviceDpi}/{editor.DeviceDpi}/{surface.DeviceDpi} cycle={cycle} " +
                    $"cell={expected.SizeInPoints:F3}pt popupExpected={expectedPopupPoints:F3}pt " +
                    $"popup={surface.Font.SizeInPoints:F3}pt.");
            using var gridGraphics = grid.CreateGraphics();
            using var popupGraphics = surface.CreateGraphics();
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var cellText = TextRenderer.MeasureText(gridGraphics, "Modbus.Generic", expected, Size.Empty, flags);
            var popupText = TextRenderer.MeasureText(popupGraphics, "Modbus.Generic", surface.Font, Size.Empty, flags);
            if (Math.Abs(popupText.Height - cellText.Height) > 1)
                throw new InvalidOperationException(
                    $"DataGridView popup rendered text at a different pixel size. " +
                    $"screen={screen.DeviceName} gridDpi={grid.DeviceDpi}/{gridGraphics.DpiX:F0} " +
                    $"popupDpi={surface.DeviceDpi}/{popupGraphics.DpiX:F0} cycle={cycle} " +
                    $"cell={cellText.Height}px popup={popupText.Height}px.");
            opened.DroppedDown = false;
        }
        if (!grid.EndEdit())
            throw new InvalidOperationException($"DataGridView {kind} editor did not end on {screen.DeviceName}.");
        PumpMessages(1);
    }

    private static void WarmUpComboBox(Form host)
    {
        for (var index = 0; index < 3; index++) _ = StressComboBox(host, index);
    }

    private static bool StressComboBox(Form host, int index)
    {
        using var combo = new ModernComboBox
        {
            Bounds = new Rectangle(24, 24, 280, 34),
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems
        };
        combo.Items.AddRange(["Camera A", "Camera B", "Camera C"]);
        host.Controls.Add(combo);
        combo.CreateControl();
        PumpMessages(1);

        if (combo.InnerComboBox.AutoCompleteMode != AutoCompleteMode.SuggestAppend ||
            combo.InnerComboBox.AutoCompleteSource != AutoCompleteSource.ListItems)
            throw new InvalidOperationException("Auto-complete was not configured after parent HWND stabilization.");

        var backgroundClosure = OpenNativeComboBox(host, combo, index);
        combo.DroppedDown = false;
        combo.RightToLeft = index % 2 == 0 ? RightToLeft.Yes : RightToLeft.No;
        PumpMessages(1);
        combo.AutoCompleteMode = index % 3 == 0 ? AutoCompleteMode.Append : AutoCompleteMode.SuggestAppend;
        combo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        PumpMessages(1);
        if (!combo.IsHandleCreated || !combo.InnerComboBox.IsHandleCreated)
            throw new InvalidOperationException("ComboBox lost its native handle during recreation.");

        host.Controls.Remove(combo);
        return backgroundClosure;
    }

    private static bool OpenNativeComboBox(Form host, ModernComboBox combo, int index)
    {
        var comboListCreated = false;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            combo.InnerComboBox.Focus();
            combo.DroppedDown = true;
            PumpMessages(2);
            comboListCreated |= EnumerateThreadWindows().Any(window =>
                string.Equals(GetWindowClassName(window), "ComboLBox", StringComparison.Ordinal));
            if (combo.DroppedDown)
            {
                if (!comboListCreated)
                    throw new InvalidOperationException($"Native ComboBox list HWND was not created on first open. index={index}.");
                return false;
            }

            // Native ComboBox closes by Windows design when another application owns the foreground.
            // The stress contract in that environment is first-HWND creation, not persistent visibility.
            if (comboListCreated && GetForegroundWindow() != host.Handle) return true;
        }

        throw new InvalidOperationException("Native ComboBox popup transaction did not create a usable list HWND. " +
            $"index={index} foreground=0x{GetForegroundWindow().ToInt64():X} host=0x{host.Handle.ToInt64():X} " +
            $"activeForm={ReferenceEquals(Form.ActiveForm, host)} hostFocused={host.ContainsFocus} " +
            $"comboFocused={combo.ContainsFocus} visible={combo.Visible}/{combo.InnerComboBox.Visible} " +
            $"handles={combo.IsHandleCreated}/{combo.InnerComboBox.IsHandleCreated} comboLBox={comboListCreated}.");
    }

    private static void StressImmediateComboBoxDisposal(Form host)
    {
        var combo = new ModernComboBox { Bounds = new Rectangle(320, 24, 280, 34) };
        combo.Items.AddRange(["Immediate A", "Immediate B"]);
        host.Controls.Add(combo);
        combo.CreateControl();
        // Do not pump: creation followed by immediate disposal must not leave native callbacks.
        host.Controls.Remove(combo);
        combo.Dispose();
        PumpMessages(1);
        if (!combo.IsDisposed) throw new InvalidOperationException("ComboBox did not dispose immediately after handle creation.");
    }

    private static void WarmUpGrid(Form host)
    {
        for (var index = 0; index < 3; index++)
        {
            StressGrid(host, index);
            StressMultiSelectGrid(host, index);
        }
    }

    private static void StressGrid(Form host, int index)
    {
        using var grid = new ModernDataGridView
        {
            Bounds = new Rectangle(24, 80, 540, 220),
            AllowUserToAddRows = false,
            RowHeadersVisible = false
        };
        grid.Columns.Add(new ModernDataGridViewComboBoxColumn
        {
            Name = "Source",
            Width = 260,
            DataSource = new[] { "Literal", "Binding", "Expression" }
        });
        grid.Rows.Add(index % 2 == 0 ? "Literal" : "Binding");
        host.Controls.Add(grid);
        grid.CreateControl();
        var cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;
        var mouse = new DataGridViewCellMouseEventArgs(0, 0, 6, 6,
            new MouseEventArgs(MouseButtons.Left, 1, 6, 6, 0));
        cell.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cell, new object[] { mouse });
        if (!grid.IsCurrentCellInEditMode)
            throw new InvalidOperationException("DataGridView did not enter edit mode from the first click.");
        PumpMessages(1);
        if (grid.EditingControl is not ModernDataGridViewSelectEditingControl editor)
            throw new InvalidOperationException("DataGridView did not create the modern select editor.");

        editor.DroppedDown = true;
        PumpMessages(1);
        if (!editor.DroppedDown) throw new InvalidOperationException("DataGridView editor popup did not open.");
        editor.SelectedIndex = (index + 1) % editor.Items.Count;
        if (index % 4 == 3)
        {
            // Exercise abrupt teardown while the managed editor popup still owns a top-level HWND.
            host.Controls.Remove(grid);
            grid.Dispose();
            PumpMessages(1);
            if (host.OwnedForms.Any(form => form.Visible))
                throw new InvalidOperationException("DataGridView editor left a visible owned popup after disposal.");
            return;
        }

        editor.DroppedDown = false;
        if (!grid.EndEdit()) throw new InvalidOperationException("DataGridView did not commit the edited value.");
        PumpMessages(1);
        host.Controls.Remove(grid);
    }

    private static void StressMultiSelectGrid(Form host, int index)
    {
        using var options = new BindingSource
        {
            DataSource = new[]
            {
                new SelectionOption(100, "VID 100"),
                new SelectionOption(101, "VID 101"),
                new SelectionOption(102, "VID 102")
            }
        };
        using var sourceGrid = new DataGridView
        {
            Bounds = new Rectangle(580, 80, 120, 220),
            DataSource = options
        };
        using var grid = new ModernDataGridView
        {
            Bounds = new Rectangle(24, 80, 540, 220),
            AllowUserToAddRows = false,
            RowHeadersVisible = false
        };
        grid.Columns.Add(new ModernDataGridViewMultiSelectColumn
        {
            Name = "VariableIds",
            Width = 420,
            DataSource = options,
            DisplayMember = nameof(SelectionOption.Label),
            ValueMember = nameof(SelectionOption.Id),
            MaxVisibleTags = 3,
            ShowTagRemoveButtons = true
        });
        grid.Rows.Add("102,101");
        host.Controls.Add(sourceGrid);
        host.Controls.Add(grid);
        sourceGrid.CreateControl();
        grid.CreateControl();
        var cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;
        var mouse = new DataGridViewCellMouseEventArgs(0, 0, 6, 6,
            new MouseEventArgs(MouseButtons.Left, 1, 6, 6, 0));
        cell.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cell, new object[] { mouse });
        if (!grid.IsCurrentCellInEditMode)
            throw new InvalidOperationException("Multi-select grid did not enter edit mode from the first click.");
        PumpMessages(1);
        if (grid.EditingControl is not ModernDataGridViewMultiSelectEditingControl editor)
            throw new InvalidOperationException("Multi-select grid did not create the modern tag editor.");
        var selector = editor.Controls.OfType<ModernSelectMultiple>().Single();
        if (selector.SelectedValues.Count != 2 ||
            !string.Equals(Convert.ToString(editor.EditingControlFormattedValue), "102,101", StringComparison.Ordinal))
            throw new InvalidOperationException("Multi-select grid did not preserve configured value order.");

        editor.DroppedDown = true;
        PumpMessages(1);
        if (!editor.DroppedDown) throw new InvalidOperationException("Multi-select grid popup did not open.");
        selector.SetItemChecked(0, true);
        if (index % 4 == 3)
        {
            host.Controls.Remove(grid);
            host.Controls.Remove(sourceGrid);
            grid.Dispose();
            PumpMessages(1);
            if (host.OwnedForms.Any(form => form.Visible))
                throw new InvalidOperationException("Multi-select grid left a visible popup after disposal.");
            return;
        }

        editor.DroppedDown = false;
        if (!grid.EndEdit()) throw new InvalidOperationException("Multi-select grid did not commit the edited value.");
        PumpMessages(1);
        if (!string.Equals(Convert.ToString(grid.Rows[0].Cells[0].Value), "102,101,100", StringComparison.Ordinal))
            throw new InvalidOperationException("Multi-select grid changed the configured order while committing tags.");

        int Scale(int value) => (int)Math.Round(value * grid.DeviceDpi / 96d);
        int measured = TextRenderer.MeasureText("VID 102", cell.InheritedStyle.Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        int tagWidth = Math.Min(Scale(170), measured + Scale(36));
        int closeX = Scale(8) + tagWidth - Scale(18) + Scale(6);
        var closeMouse = new DataGridViewCellMouseEventArgs(0, 0, closeX, cell.Size.Height / 2,
            new MouseEventArgs(MouseButtons.Left, 1, closeX, cell.Size.Height / 2, 0));
        cell.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(cell, new object[] { closeMouse });
        if (!string.Equals(Convert.ToString(cell.Value), "101,100", StringComparison.Ordinal))
            throw new InvalidOperationException("Multi-select tag close button did not remove the selected value.");
        host.Controls.Remove(grid);
        host.Controls.Remove(sourceGrid);
    }

    private static void AssertNoUnexpectedVisibleTopLevelWindows(Form owner, HashSet<IntPtr> baseline)
    {
        var unexpected = EnumerateThreadWindows()
            .Where(window => window != owner.Handle && !baseline.Contains(window) && IsWindowVisible(window))
            .Where(window => GetWindow(window, GwOwner) != owner.Handle)
            .Select(window => $"0x{window.ToInt64():X}:{GetWindowClassName(window)}")
            .ToArray();
        if (unexpected.Length > 0)
            throw new InvalidOperationException("Visible unowned top-level HWNDs remain: " + string.Join(", ", unexpected));
    }

    private static void AssertAtMost(string name, int actual, int maximum)
    {
        if (actual > maximum) throw new InvalidOperationException($"{name} {actual} exceeds budget {maximum}.");
    }

    private static void CollectAndPump()
    {
        PumpMessages(2);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        PumpMessages(2);
    }

    private static void PumpMessages(int passes)
    {
        for (var index = 0; index < passes; index++)
        {
            Application.DoEvents();
            Thread.Sleep(2);
        }
    }

    private static IEnumerable<IntPtr> EnumerateThreadWindows()
    {
        var windows = new List<IntPtr>();
        _ = EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            windows.Add(window);
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static string GetWindowClassName(IntPtr window)
    {
        var buffer = new StringBuilder(256);
        _ = GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumThreadWindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private sealed class SelectionOption
    {
        public SelectionOption(uint id, string label)
        {
            Id = id;
            Label = label;
        }

        public uint Id { get; }
        public string Label { get; }
    }
}
