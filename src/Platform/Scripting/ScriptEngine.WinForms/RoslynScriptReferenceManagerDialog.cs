namespace ScriptEngine.WinForms;

/// <summary>使用 PE 元数据管理脚本 DLL 引用的 WinForms 对话框；检查过程不会加载目标程序集。</summary>
public sealed class RoslynScriptReferenceManagerDialog : Form
{
    private readonly DataGridView _grid = new();
    private readonly BindingSource _source = new();
    private readonly List<RoslynScriptReferenceInfo> _references = new();

    /// <summary>创建引用管理对话框。</summary>
    /// <param name="paths">当前脚本已有的 DLL 路径。</param>
    public RoslynScriptReferenceManagerDialog(IEnumerable<string> paths)
    {
        Text = "C# 脚本 DLL 引用";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(900, 460);
        MinimumSize = new Size(720, 360);
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RoslynScriptReferenceInfo.AssemblyName), HeaderText = "程序集", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RoslynScriptReferenceInfo.Version), HeaderText = "版本", FillWeight = 16 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RoslynScriptReferenceInfo.Architecture), HeaderText = "架构", FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(RoslynScriptReferenceInfo.Path), HeaderText = "文件", FillWeight = 52 });
        _grid.DataSource = _source;

        var add = CommandButton("添加 DLL…", 102);
        var remove = CommandButton("删除选中", 94);
        var details = CommandButton("查看信息", 88);
        var ok = CommandButton("确定", 76);
        var cancel = CommandButton("取消", 76);
        ok.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        add.Click += (_, _) => AddReferences();
        remove.Click += (_, _) => RemoveSelected();
        details.Click += (_, _) => ShowSelectedDetails();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(6),
            WrapContents = false
        };
        toolbar.Controls.Add(add);
        toolbar.Controls.Add(remove);
        toolbar.Controls.Add(details);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(6),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
        ApplyDarkAppearance(this);

        foreach (var path in paths.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            try { AddReference(path, showDuplicateMessage: false); }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "引用读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        RefreshGrid();
    }

    /// <summary>创建尺寸稳定的命令按钮，避免系统字体或 DPI 改变时工具栏高度跳变。</summary>
    private static Button CommandButton(string text, int width) => new()
    {
        Text = text,
        AutoSize = false,
        Size = new Size(width, 30),
        Margin = new Padding(3)
    };

    /// <summary>应用与脚本编辑器一致的独立深色外观，不要求宿主引用特定 UI 主题库。</summary>
    private static void ApplyDarkAppearance(Control root)
    {
        var window = Color.FromArgb(30, 30, 30);
        var surface = Color.FromArgb(37, 37, 38);
        var control = Color.FromArgb(51, 51, 55);
        var border = Color.FromArgb(63, 63, 70);
        var text = Color.FromArgb(241, 241, 241);
        root.BackColor = root is DataGridView ? surface : window;
        root.ForeColor = text;

        switch (root)
        {
            case Button button:
                button.BackColor = control;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = border;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(62, 62, 66);
                break;
            case DataGridView grid:
                grid.BackgroundColor = surface;
                grid.BorderStyle = BorderStyle.FixedSingle;
                grid.GridColor = border;
                grid.EnableHeadersVisualStyles = false;
                grid.DefaultCellStyle.BackColor = control;
                grid.DefaultCellStyle.ForeColor = text;
                grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 122, 204);
                grid.DefaultCellStyle.SelectionForeColor = Color.White;
                grid.AlternatingRowsDefaultCellStyle.BackColor = surface;
                grid.ColumnHeadersDefaultCellStyle.BackColor = surface;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = text;
                grid.ColumnHeadersHeight = 30;
                grid.RowTemplate.Height = 28;
                break;
        }

        foreach (Control child in root.Controls) ApplyDarkAppearance(child);
    }

    /// <summary>用户确认后返回的 DLL 绝对路径。</summary>
    public IReadOnlyList<string> ReferencePaths => _references.Select(item => item.Path).ToArray();

    private void AddReferences()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择脚本引用 DLL",
            Filter = "托管程序集 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dialog.FileNames)
        {
            try { AddReference(path, showDuplicateMessage: true); }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "无法添加引用", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        RefreshGrid();
    }

    private void AddReference(string path, bool showDuplicateMessage)
    {
        var info = RoslynScriptReferenceInspector.Inspect(path);
        var duplicate = _references.FirstOrDefault(item => string.Equals(item.AssemblyName, info.AssemblyName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            if (string.Equals(duplicate.Path, info.Path, StringComparison.OrdinalIgnoreCase)) return;
            if (showDuplicateMessage)
                MessageBox.Show(this, $"已经存在同名程序集 {info.AssemblyName}：\n{duplicate.Path}", "程序集名称冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _references.Add(info);
    }

    private void RemoveSelected()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .OfType<RoslynScriptReferenceInfo>()
            .ToArray();
        foreach (var item in selected) _references.Remove(item);
        RefreshGrid();
    }

    private void ShowSelectedDetails()
    {
        if (_grid.CurrentRow?.DataBoundItem is not RoslynScriptReferenceInfo item) return;
        var dependencies = item.Dependencies.Count == 0 ? "（无）" : string.Join(Environment.NewLine, item.Dependencies);
        MessageBox.Show(
            this,
            $"程序集：{item.AssemblyName}\n版本：{item.Version}\n架构：{item.Architecture}\nSHA256：{item.Sha256}\n路径：{item.Path}\n\n直接依赖：\n{dependencies}",
            "DLL 引用信息",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RefreshGrid()
    {
        _source.DataSource = _references.OrderBy(item => item.AssemblyName, StringComparer.OrdinalIgnoreCase).ToArray();
        _source.ResetBindings(false);
    }
}
