using ScriptEngine.Workspaces;

namespace ScriptEngine.WinForms;

internal static class ScriptWorkspaceDialogs
{
    public static string? RequestNewName(IWin32Window? owner, string currentName)
    {
        using var dialog = new Form
        {
            Text = "重命名符号",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(390, 112),
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White
        };
        var label = new Label { Text = "新名称：", AutoSize = true, Location = new Point(12, 17) };
        var input = new TextBox
        {
            Text = currentName,
            Location = new Point(76, 13),
            Width = 300,
            BackColor = Color.FromArgb(51, 51, 55),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(220, 66), Width = 75 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(301, 66), Width = 75 };
        dialog.Controls.AddRange(new Control[] { label, input, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) =>
        {
            input.SelectAll();
            input.Focus();
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }

    public static RoslynScriptWorkspaceEdit? SelectEdit(
        IWin32Window? owner,
        IReadOnlyList<RoslynScriptWorkspaceEdit> edits)
    {
        if (edits.Count == 0) return null;
        if (edits.Count == 1) return edits[0];
        using var dialog = new Form
        {
            Text = "选择代码修复",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(540, 300),
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            IntegralHeight = false
        };
        foreach (var edit in edits) list.Items.Add(edit.Title);
        list.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 80 };
        var apply = new Button { Text = "应用", DialogResult = DialogResult.OK, Width = 80 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        dialog.Controls.Add(list);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0) dialog.DialogResult = DialogResult.OK;
        };
        return dialog.ShowDialog(owner) == DialogResult.OK && list.SelectedIndex >= 0
            ? edits[list.SelectedIndex]
            : null;
    }

    public static bool ConfirmEdit(IWin32Window? owner, RoslynScriptWorkspaceEdit edit)
    {
        if (edit is null) throw new ArgumentNullException(nameof(edit));
        var changes = edit.FileChanges ?? Array.Empty<RoslynScriptWorkspaceFileChange>();
        if (changes.Count == 0) return true;
        using var dialog = new Form
        {
            Text = "预览项目修改 - " + edit.Title,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(980, 620),
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White
        };
        var files = new ListBox
        {
            Dock = DockStyle.Left,
            Width = 190,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            IntegralHeight = false
        };
        foreach (var change in changes) files.Items.Add(change.FileName);
        var comparison = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 385,
            BackColor = Color.FromArgb(55, 55, 58)
        };
        var before = CreatePreviewBox();
        var after = CreatePreviewBox();
        comparison.Panel1.Controls.Add(before);
        comparison.Panel2.Controls.Add(after);
        var beforeTitle = new Label { Text = "修改前", Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
        var afterTitle = new Label { Text = "修改后", Dock = DockStyle.Top, Height = 26, TextAlign = ContentAlignment.MiddleLeft };
        comparison.Panel1.Controls.Add(beforeTitle);
        comparison.Panel2.Controls.Add(afterTitle);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 88 };
        var apply = new Button { Text = $"应用 {changes.Count} 个文件", DialogResult = DialogResult.OK, Width = 128 };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        dialog.Controls.Add(comparison);
        dialog.Controls.Add(files);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        files.SelectedIndexChanged += (_, _) =>
        {
            if (files.SelectedIndex < 0) return;
            var change = changes[files.SelectedIndex];
            before.Text = AddLineNumbers(change.OriginalSource);
            after.Text = AddLineNumbers(change.ChangedSource);
            var beforeLine = GetLineFromPosition(change.OriginalSource, change.Start);
            var afterLine = GetLineFromPosition(change.ChangedSource, change.Start);
            ScrollToLine(before, beforeLine);
            ScrollToLine(after, afterLine);
        };
        files.SelectedIndex = 0;
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    private static RichTextBox CreatePreviewBox() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        WordWrap = false,
        DetectUrls = false,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(30, 30, 30),
        ForeColor = Color.Gainsboro,
        Font = new Font("Consolas", 9.5F),
        ScrollBars = RichTextBoxScrollBars.Both
    };

    private static string AddLineNumbers(string source)
    {
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var width = Math.Max(2, lines.Length.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        return string.Join(Environment.NewLine, lines.Select((line, index) =>
            (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(width) + "  " + line));
    }

    private static int GetLineFromPosition(string source, int position)
    {
        var line = 0;
        for (var index = 0; index < Math.Min(position, source.Length); index++)
            if (source[index] == '\n') line++;
        return line;
    }

    private static void ScrollToLine(RichTextBox box, int line)
    {
        var position = box.GetFirstCharIndexFromLine(Math.Max(0, line - 2));
        if (position < 0) return;
        box.SelectionStart = position;
        box.ScrollToCaret();
        box.SelectionLength = 0;
    }

    public static RoslynScriptReferenceLocation? SelectReference(
        IWin32Window? owner,
        IReadOnlyList<RoslynScriptReferenceLocation> locations)
    {
        using var dialog = new Form
        {
            Text = $"查找所有引用 ({locations.Count})",
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(720, 390),
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White
        };
        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        list.Columns.Add("类型", 70);
        list.Columns.Add("文件", 390);
        list.Columns.Add("行", 70);
        list.Columns.Add("列", 70);
        foreach (var location in locations)
        {
            var row = new ListViewItem(location.IsDefinition ? "定义" : "引用") { Tag = location };
            row.SubItems.Add(location.FileName);
            row.SubItems.Add(location.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
            row.SubItems.Add(location.Column.ToString(System.Globalization.CultureInfo.InvariantCulture));
            list.Items.Add(row);
        }
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var close = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, Width = 80 };
        var navigate = new Button { Text = "跳转", DialogResult = DialogResult.OK, Width = 80 };
        buttons.Controls.Add(close);
        buttons.Controls.Add(navigate);
        dialog.Controls.Add(list);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = navigate;
        dialog.CancelButton = close;
        if (list.Items.Count > 0) list.Items[0].Selected = true;
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedItems.Count > 0) dialog.DialogResult = DialogResult.OK;
        };
        return dialog.ShowDialog(owner) == DialogResult.OK && list.SelectedItems.Count > 0
            ? list.SelectedItems[0].Tag as RoslynScriptReferenceLocation
            : null;
    }
}
