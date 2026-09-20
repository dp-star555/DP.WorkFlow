namespace DP.WorkFlow.UI.WinForms;

/// <summary>以列表方式添加、删除和整理 C# using 命名空间。</summary>
internal sealed partial class CSharpUsingManagerDialog : Form
{
    private static readonly string[] CommonNamespaces =
    {
        "System", "System.Collections.Generic", "System.Diagnostics", "System.Globalization", "System.IO",
        "System.Linq", "System.Net.Http", "System.Text", "System.Text.Json", "System.Threading",
        "System.Threading.Tasks", "System.Windows.Forms", "ScriptEngine", "DP.WorkFlow"
    };


    /// <summary>供 Visual Studio WinForms 设计器显示 using 管理窗口。</summary>
    public CSharpUsingManagerDialog()
    {
        InitializeComponent();
        rootLayout.RowStyles[1].Height = 40;
        rootLayout.RowStyles[3].Height = 42;
        rootLayout.RowStyles[5].Height = 42;
        foreach (var button in new[] { addButton, removeButton, sortButton, defaultsButton, okButton, cancelButton })
        {
            var preferredWidth = button.GetPreferredSize(Size.Empty).Width;
            button.AutoSize = false;
            button.Size = new Size(Math.Max(76, preferredWidth), 30);
        }
        WorkflowWinFormsStyle.Apply(this);
        addButton.Click += (_, _) => AddInput();
        removeButton.Click += (_, _) => RemoveSelected();
        sortButton.Click += (_, _) => ReplaceItems(Namespaces);
        defaultsButton.Click += (_, _) => ReplaceItems(Namespaces.Concat(new[] { "System", "System.Collections.Generic", "System.Linq", "System.Threading", "System.Threading.Tasks" }));
        _input.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Enter) return;
            AddInput();
            eventArgs.SuppressKeyPress = true;
        };
        _imports.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode != Keys.Delete) return;
            RemoveSelected();
            eventArgs.SuppressKeyPress = true;
        };
        _imports.DoubleClick += (_, _) => RemoveSelected();
        Shown += (_, _) => _input.Focus();
    }

    /// <summary>使用已有 using 集合初始化运行时窗口。</summary>
    /// <returns>返回处理结果。</returns>
    public CSharpUsingManagerDialog(IEnumerable<string> namespaces) : this()
    {
        ArgumentNullException.ThrowIfNull(namespaces);
        _input.Items.AddRange(CommonNamespaces.Cast<object>().ToArray());
        foreach (var item in namespaces.Select(Normalize).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal))
            _imports.Items.Add(item);
    }

    public IReadOnlyList<string> Namespaces => _imports.Items.Cast<string>()
        .Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToArray();

    /// <summary>添加Input。</summary>
    private void AddInput()
    {
        var value = Normalize(_input.Text);
        if (!IsValidNamespace(value))
        {
            MessageBox.Show(this, "请输入有效的命名空间，例如 System.Text。", "using 管理", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _input.SelectAll();
            return;
        }
        if (!_imports.Items.Cast<string>().Contains(value, StringComparer.Ordinal)) _imports.Items.Add(value);
        ReplaceItems(Namespaces);
        _input.Text = string.Empty;
        _input.Focus();
    }

    /// <summary>删除Selected。</summary>
    private void RemoveSelected()
    {
        foreach (var item in _imports.SelectedItems.Cast<object>().ToArray()) _imports.Items.Remove(item);
    }

    /// <summary>执行 Replace Items 相关处理。</summary>
    /// <param name="items">“items”参数。</param>
    private void ReplaceItems(IEnumerable<string> items)
    {
        var selected = _imports.SelectedItem as string;
        _imports.BeginUpdate();
        _imports.Items.Clear();
        _imports.Items.AddRange(items.Select(Normalize).Where(IsValidNamespace).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).Cast<object>().ToArray());
        _imports.EndUpdate();
        if (selected is not null) _imports.SelectedItem = selected;
    }

    /// <summary>执行 Normalize 相关处理。</summary>
    /// <param name="value">要转换或设置的值。</param>
    /// <returns>返回处理结果。</returns>
    private static string Normalize(string? value)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.StartsWith("global using ", StringComparison.Ordinal)) result = result[13..];
        else if (result.StartsWith("using ", StringComparison.Ordinal)) result = result[6..];
        return result.Trim().TrimEnd(';').Trim();
    }

    /// <summary>执行 All 相关处理。</summary>
    /// <returns>返回处理结果。</returns>
    private static bool IsValidNamespace(string value) => value.Length > 0 && value.Split('.').All(segment =>
        segment.Length > 0 && (char.IsLetter(segment[0]) || segment[0] == '_')
        && segment.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_'));
}
