using System.Diagnostics;
using System.Runtime.InteropServices;
using ModernUI.WinForms;

namespace ModernUI.WinForms.DataStress;

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
        using var host = new Form { ClientSize = new Size(1000, 720), ShowInTaskbar = false };
        host.Show();
        Pump();
        // Warm every native control family before the resource baseline. WinForms, comctl32 and
        // GDI+ retain process-level caches on first use; those are startup cost, not per-cycle leaks.
        ExerciseListBox(host);
        ExerciseListView(host);
        ExerciseTree(host);
        ExerciseGrid(host);
        Collect();

        var process = Process.GetCurrentProcess();
        process.Refresh();
        var handlesBefore = process.HandleCount;
        var gdiBefore = GetGuiResources(process.Handle, 0);
        var userBefore = GetGuiResources(process.Handle, 1);

        var listBoxMs = Measure(() => ExerciseListBox(host));
        var listViewMs = Measure(() => ExerciseListView(host));
        var treeMs = Measure(() => ExerciseTree(host));
        var gridMs = Measure(() => ExerciseGrid(host));
        Collect();
        process.Refresh();
        var handles = process.HandleCount - handlesBefore;
        var gdi = (int)GetGuiResources(process.Handle, 0) - (int)gdiBefore;
        var user = (int)GetGuiResources(process.Handle, 1) - (int)userBefore;

        Console.WriteLine($"DATA_STRESS_RESULT listBoxMs={listBoxMs} listViewMs={listViewMs} treeMs={treeMs} gridMs={gridMs} handles={handles} gdi={gdi} user={user}");
        AssertBudget("ListBox", listBoxMs, 3000);
        AssertBudget("ListView", listViewMs, 5000);
        AssertBudget("TreeView", treeMs, 5000);
        AssertBudget("DataGridView", gridMs, 5000);
        AssertBudget("Process handles", handles, 40);
        AssertBudget("GDI objects", gdi, 8);
        AssertBudget("USER objects", user, 8);
        Console.WriteLine($"DATA_STRESS_OK listBoxMs={listBoxMs} listViewMs={listViewMs} treeMs={treeMs} gridMs={gridMs} handles={handles} gdi={gdi} user={user}");
    }

    private static long Measure(Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        Pump();
        watch.Stop();
        return watch.ElapsedMilliseconds;
    }

    private static void ExerciseListBox(Form host)
    {
        using var control = new ModernListBox { Bounds = new Rectangle(0, 0, 460, 300) };
        host.Controls.Add(control);
        control.BeginUpdate();
        try { control.Items.AddRange(Enumerable.Range(0, 10_000).Select(index => (object)$"Item {index}").ToArray()); }
        finally { control.EndUpdate(); }
        _ = control.Handle;
        control.TopIndex = 9_980;
        control.SelectedIndex = 9_999;
        Pump();
        if (control.TopIndex < 9_900 || control.SelectedIndex != 9_999)
            throw new InvalidOperationException("ListBox did not preserve large-list scroll/selection semantics.");
        host.Controls.Remove(control);
    }

    private static void ExerciseListView(Form host)
    {
        using var control = new ModernListView { Bounds = new Rectangle(0, 0, 700, 300) };
        control.Columns.Add("Name", 320);
        control.Columns.Add("Status", 220);
        control.Columns.Add("Description", 320);
        host.Controls.Add(control);
        control.BeginUpdate();
        try
        {
            control.Items.AddRange(Enumerable.Range(0, 2_000).Select(index =>
                new ListViewItem(new[]
                {
                    $"Camera {index}",
                    index % 2 == 0 ? "Online" : "Offline",
                    "Native scroll chrome regression row " + index
                })).ToArray());
        }
        finally { control.EndUpdate(); }
        _ = control.Handle;
        control.TopItem = control.Items[1_980];
        control.Items[1_999].Selected = true;
        Pump();
        if (control.TopItem.Index < 1_900 || !control.Items[1_999].Selected)
            throw new InvalidOperationException("ListView did not preserve large-list scroll/selection semantics.");
        const int scrollStyles = 0x00100000 | 0x00200000;
        if ((GetWindowLong(control.Handle, -16) & scrollStyles) != 0)
            throw new InvalidOperationException("ListView restored deprecated native square scroll chrome.");
        host.Controls.Remove(control);
    }

    private static void ExerciseTree(Form host)
    {
        using var control = new ModernTreeView { Bounds = new Rectangle(0, 0, 600, 300) };
        host.Controls.Add(control);
        control.BeginUpdate();
        try
        {
            for (var root = 0; root < 20; root++)
            {
                var parent = control.Nodes.Add($"Group {root}");
                for (var child = 0; child < 99; child++) parent.Nodes.Add($"Node {root}-{child}");
                parent.Expand();
            }
        }
        finally { control.EndUpdate(); }
        _ = control.Handle;
        var last = control.Nodes[19].Nodes[98];
        control.SelectedNode = last;
        last.EnsureVisible();
        Pump();
        if (!ReferenceEquals(control.SelectedNode, last))
            throw new InvalidOperationException("TreeView did not preserve large-tree selection semantics.");
        host.Controls.Remove(control);
    }

    private static void ExerciseGrid(Form host)
    {
        using var control = new ModernDataGridView
        {
            Bounds = new Rectangle(0, 0, 800, 320),
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false
        };
        control.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", DataPropertyName = "Id", Width = 120 });
        control.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", DataPropertyName = "Name", Width = 320 });
        host.Controls.Add(control);
        control.DataSource = Enumerable.Range(0, 2_000).Select(index => new Row(index, $"Row {index}")).ToArray();
        _ = control.Handle;
        control.FirstDisplayedScrollingRowIndex = 1_980;
        control.CurrentCell = control.Rows[1_999].Cells[1];
        Pump();
        if (control.FirstDisplayedScrollingRowIndex < 1_900 || control.CurrentCell.RowIndex != 1_999)
            throw new InvalidOperationException("DataGridView did not preserve large-grid scroll/current-cell semantics.");
        host.Controls.Remove(control);
    }

    private static void Pump()
    {
        Application.DoEvents();
        Thread.Sleep(5);
        Application.DoEvents();
    }

    private static void Collect()
    {
        Pump();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Pump();
    }

    private static void AssertBudget(string name, long actual, long maximum)
    {
        if (actual > maximum) throw new InvalidOperationException($"{name} {actual} exceeds budget {maximum}.");
    }

    private sealed class Row
    {
        public Row(int id, string name) { Id = id; Name = name; }
        public int Id { get; }
        public string Name { get; }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
