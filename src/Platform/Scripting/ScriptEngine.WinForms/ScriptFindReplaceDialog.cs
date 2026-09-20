namespace ScriptEngine.WinForms;

/// <summary>复用编辑器查找接口的轻量非模态查找替换窗口。</summary>
internal sealed class ScriptFindReplaceDialog : Form
{
    private readonly RoslynScriptEditorControl _editor;
    private readonly TextBox _findText = new() { Width = 240 };
    private readonly TextBox _replaceText = new() { Width = 240 };
    private readonly CheckBox _matchCase = new() { Text = "区分大小写", AutoSize = true };
    private readonly CheckBox _wholeWord = new() { Text = "全字匹配", AutoSize = true };
    private readonly TableLayoutPanel _layout = new();

    public ScriptFindReplaceDialog(RoslynScriptEditorControl editor, bool showReplace)
    {
        _editor = editor;
        Text = showReplace ? "查找和替换" : "查找";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        ClientSize = new Size(430, showReplace ? 150 : 112);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(37, 37, 38);
        ForeColor = Color.FromArgb(241, 241, 241);

        _layout.Dock = DockStyle.Fill;
        _layout.Padding = new Padding(10);
        _layout.ColumnCount = 3;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _layout.RowCount = showReplace ? 3 : 2;
        AddRow(0, "查找", _findText, CreateButton("下一个", (_, _) => FindNext()));
        if (showReplace)
            AddRow(1, "替换", _replaceText, CreateButton("替换", (_, _) => ReplaceNext()));

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        options.Controls.Add(_matchCase);
        options.Controls.Add(_wholeWord);
        var optionRow = showReplace ? 2 : 1;
        _layout.Controls.Add(options, 1, optionRow);
        if (showReplace)
            _layout.Controls.Add(CreateButton("全部替换", (_, _) => ReplaceAll()), 2, optionRow);
        Controls.Add(_layout);
        AcceptButton = _layout.GetControlFromPosition(2, 0) as Button;
        KeyPreview = true;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape) Close();
        };
    }

    public void ActivateSearch(bool showReplace)
    {
        if (showReplace && _layout.RowCount < 3)
        {
            Close();
            return;
        }
        if (!string.IsNullOrEmpty(_editor.SelectedText) && !_editor.SelectedText.Contains(Environment.NewLine))
            _findText.Text = _editor.SelectedText;
        Show();
        Activate();
        _findText.Focus();
        _findText.SelectAll();
    }

    private void AddRow(int row, string label, Control input, Button command)
    {
        _layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        input.Dock = DockStyle.Fill;
        input.BackColor = Color.FromArgb(51, 51, 55);
        input.ForeColor = ForeColor;
        if (input is TextBox textBox) textBox.BorderStyle = BorderStyle.FixedSingle;
        _layout.Controls.Add(input, 1, row);
        _layout.Controls.Add(command, 2, row);
    }

    private static Button CreateButton(string text, EventHandler clicked)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 51, 55),
            ForeColor = Color.White,
            Margin = new Padding(4, 1, 0, 1)
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 74);
        button.Click += clicked;
        return button;
    }

    private void FindNext()
    {
        if (!_editor.FindNext(_findText.Text, _matchCase.Checked, _wholeWord.Checked))
            System.Media.SystemSounds.Beep.Play();
    }

    private void ReplaceNext()
    {
        var comparison = _matchCase.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (string.Equals(_editor.SelectedText, _findText.Text, comparison))
            _editor.SelectedText = _replaceText.Text;
        FindNext();
    }

    private void ReplaceAll()
    {
        var count = _editor.ReplaceAll(_findText.Text, _replaceText.Text, _matchCase.Checked, _wholeWord.Checked);
        Text = $"查找和替换 · 已替换 {count} 处";
    }
}

/// <summary>行号跳转输入窗口。</summary>
internal static class ScriptGoToLineDialog
{
    public static void Show(IWin32Window? owner, RoslynScriptEditorControl editor)
    {
        using var dialog = new Form
        {
            Text = "跳转到行",
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(280, 92),
            ShowInTaskbar = false,
            BackColor = Color.FromArgb(37, 37, 38),
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9F)
        };
        var number = new NumericUpDown
        {
            Minimum = 1,
            Maximum = Math.Max(1, editor.Text.Count(character => character == '\n') + 1),
            Value = Math.Max(1, editor.Text.Take(editor.SelectionStart).Count(character => character == '\n') + 1),
            Location = new Point(16, 15),
            Width = 150,
            BackColor = Color.FromArgb(51, 51, 55),
            ForeColor = Color.White
        };
        var ok = new Button { Text = "跳转", DialogResult = DialogResult.OK, Location = new Point(180, 13), Width = 78 };
        dialog.Controls.Add(number);
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;
        if (dialog.ShowDialog(owner) == DialogResult.OK) editor.GoToLine((int)number.Value);
    }
}
