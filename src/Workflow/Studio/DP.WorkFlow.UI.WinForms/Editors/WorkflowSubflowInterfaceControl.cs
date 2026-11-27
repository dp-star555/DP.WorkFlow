using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点窗口“输入输出”页：编辑复合节点子流程的输入和输出。
/// 输入的来源选父画布中本节点之前的节点输出（类型跟随来源），或使用手动值（整数、小数、布尔、文本）；
/// 子画布中通过“启动节点.名称”读取。输出的来源选子画布中的节点输出，父画布中通过“本节点.名称”读取。
/// </summary>
/// <remarks>
/// 页面在节点窗口左边的窄栏里，表格放不下名称、来源、类型、值几列。所以每一项画成一张小卡片，
/// 一行一个字段（名称 / 来源 / 类型 / 值），来源文字放不下时换行，整页竖向滚动。
/// </remarks>
internal sealed class WorkflowSubflowInterfaceControl : UserControl
{
    private const string Drop = "  ▾";
    private static readonly E_WorkflowSubflowValueType[] ValueTypes = Polyfill.GetEnumValues<E_WorkflowSubflowValueType>();

    private readonly WorkflowSubflowInterfacePageModel _model;
    private readonly TableLayoutPanel _inputs = List();
    private readonly TableLayoutPanel _outputs = List();
    private readonly ToolTip _tips = new();
    private string? _shape;
    private bool _refreshQueued;

    public WorkflowSubflowInterfaceControl(WorkflowSubflowInterfacePageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(4)
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.Controls.Add(Header("输入", "添加输入",
            "来源选父画布中本节点之前的节点输出，类型自动跟随来源；不选来源时使用手动值。子画布中绑定“启动节点.名称”读取。",
            () => _model.AddInput()));
        body.Controls.Add(_inputs);
        body.Controls.Add(Header("输出", "添加输出",
            "来源选子画布中的节点输出，类型自动跟随来源。父画布中绑定“本节点.名称”读取。",
            () => _model.AddOutput()));
        body.Controls.Add(_outputs);
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(body);
        Controls.Add(scroller);

        _model.Changed += OnModelChanged;
        RefreshRows();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _model.Changed -= OnModelChanged;
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        // 修改常由卡片里的输入框提交触发，推迟到事件结束后刷新，避免在事件里释放正在用的控件。
        if (IsDisposed || _refreshQueued) return;
        _refreshQueued = true;
        if (IsHandleCreated) BeginInvoke(RefreshRows);
        else RefreshRows();
    }

    private void RefreshRows()
    {
        _refreshQueued = false;
        if (IsDisposed) return;
        // 只有增删项、或输入在“绑定 / 手动值”之间切换时才重建卡片；改名、改值只更新文字，焦点不丢。
        var shape = string.Join(",", _model.Inputs.Select(input => input.IsBound ? "b" : "m")) + "|" + _model.Outputs.Count;
        if (shape != _shape)
        {
            _shape = shape;
            Rebuild();
        }
        else UpdateTexts();
    }

    private void Rebuild()
    {
        SuspendLayout();
        try
        {
            Fill(_inputs, _model.Inputs.Count, "还没有输入。", index => InputCard(_model.Inputs[index]));
            Fill(_outputs, _model.Outputs.Count, "还没有输出。", index => OutputCard(_model.Outputs[index]));
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void Fill(TableLayoutPanel list, int count, string empty, Func<int, Control> card)
    {
        var old = list.Controls.Cast<Control>().ToArray();
        list.Controls.Clear();
        foreach (var control in old) control.Dispose();
        list.RowStyles.Clear();
        if (count == 0)
        {
            list.Controls.Add(Hint(empty));
            return;
        }
        for (var i = 0; i < count; i++)
        {
            list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list.Controls.Add(card(i));
        }
    }

    private void UpdateTexts()
    {
        for (var i = 0; i < _model.Inputs.Count && i < _inputs.Controls.Count; i++)
        {
            if (_inputs.Controls[i].Tag is not Card card) continue;
            var input = _model.Inputs[i];
            card.SetName(input.Name);
            card.Source.Text = input.SourceText + Drop;
            card.Type.Text = input.IsBound ? input.TypeText + "（跟随来源）" : WorkflowSubflowInput.DisplayName(input.ValueType) + Drop;
            card.SetValue(input.ValueText);
        }
        for (var i = 0; i < _model.Outputs.Count && i < _outputs.Controls.Count; i++)
        {
            if (_outputs.Controls[i].Tag is not Card card) continue;
            var output = _model.Outputs[i];
            card.SetName(output.Name);
            card.Source.Text = output.SourceText + Drop;
            card.Type.Text = output.TypeText;
        }
    }

    private Control InputCard(WorkflowSubflowInputRow input)
    {
        var index = input.Index;
        var card = new Card(this, input.Name, name => _model.RenameInput(index, name), () => _model.RemoveInput(index));
        card.Source.Text = input.SourceText + Drop;
        card.Source.Click += (_, _) => ShowMenu(card.Source, menu =>
        {
            Add(menu, "选择父画布中的来源…", () =>
            {
                using var dialog = new WorkflowBindingSelectorDialog(_model.GetInputCandidates(), _model.Inputs[index].Source);
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                    _model.SetInputSource(index, candidate.ToBindingKey());
            });
            Add(menu, "使用手动值", () => _model.SetInputSource(index, null));
        });
        if (input.IsBound)
        {
            // 绑定来源时类型跟随来源，不能修改，也没有手动值。
            card.Type.Text = input.TypeText + "（跟随来源）";
            card.Type.ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary;
            card.Type.Cursor = Cursors.Default;
        }
        else
        {
            card.Type.Text = WorkflowSubflowInput.DisplayName(input.ValueType) + Drop;
            card.Type.Click += (_, _) => ShowMenu(card.Type, menu =>
            {
                foreach (var type in ValueTypes)
                    Add(menu, WorkflowSubflowInput.DisplayName(type), () => _model.SetInputValueType(index, type), type == _model.Inputs[index].ValueType);
            });
            card.AddValue(input.ValueText, text => _model.SetInputValue(index, text));
        }
        return card.Root;
    }

    private Control OutputCard(WorkflowSubflowOutputRow output)
    {
        var index = output.Index;
        var card = new Card(this, output.Name, name => _model.RenameOutput(index, name), () => _model.RemoveOutput(index));
        card.Source.Text = output.SourceText + Drop;
        card.Source.Click += (_, _) =>
        {
            using var dialog = new WorkflowBindingSelectorDialog(_model.GetOutputCandidates(), _model.Outputs[index].Source);
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                _model.SetOutputSource(index, candidate.ToBindingKey());
        };
        card.Type.Text = output.TypeText;
        card.Type.ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary;
        card.Type.Cursor = Cursors.Default;
        return card.Root;
    }

    /// <summary>
    /// 一项输入或输出：带边框的小卡片，左列是字段名，右列是内容。
    /// 名称、值是输入框（回车或离开时提交）；来源、类型是可点的文字（带 ▾），点开菜单或选择窗口。
    /// </summary>
    private sealed class Card
    {
        private readonly WorkflowSubflowInterfaceControl _owner;
        private readonly ModernInput _name;
        private ModernInput? _value;

        public Card(WorkflowSubflowInterfaceControl owner, string name, Action<string> rename, Action remove)
        {
            _owner = owner;
            Root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                Padding = new Padding(10, 8, 6, 8),
                Margin = new Padding(0, 0, 0, 6),
                Tag = this
            };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Root.Paint += (_, e) =>
            {
                using var pen = new Pen(WorkflowWinFormsTheme.Modern.BorderSecondary);
                e.Graphics.DrawRectangle(pen, 0, 0, Root.Width - 1, Root.Height - 1);
            };
            Root.Resize += (_, _) => Root.Invalidate();

            _name = Input(name, rename);
            var delete = WorkflowSizing.Fit(new ModernButton
            {
                Text = "删除",
                Theme = WorkflowWinFormsTheme.Modern,
                ButtonType = ModernButtonType.Text,
                Anchor = AnchorStyles.Right
            });
            delete.Click += (_, _) => remove();
            Source = Link();
            Type = Link();
            AddRow("名称", _name, delete);
            AddRow("来源", Source);
            AddRow("类型", Type);
        }

        public TableLayoutPanel Root { get; }

        public Label Source { get; }

        public Label Type { get; }

        public void AddValue(string text, Action<string> commit)
        {
            _value = Input(text, commit);
            AddRow("值", _value);
        }

        public void SetName(string text) => SetText(_name, text);

        public void SetValue(string text)
        {
            if (_value is not null) SetText(_value, text);
        }

        // 正在编辑的框不覆盖，免得打字时被刷新打断。
        private static void SetText(ModernInput input, string text)
        {
            if (!input.ContainsFocus && input.Text != text) input.Text = text;
        }

        private void AddRow(string caption, Control content, Control? trailing = null)
        {
            var row = Root.RowCount++;
            Root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary,
                Margin = new Padding(0, 6, 10, 6)
            };
            Root.Controls.Add(label, 0, row);
            Root.Controls.Add(content, 1, row);
            if (trailing is null) Root.SetColumnSpan(content, 2);
            else Root.Controls.Add(trailing, 2, row);
        }

        private ModernInput Input(string text, Action<string> commit)
        {
            var input = new ModernInput
            {
                Text = text,
                Theme = WorkflowWinFormsTheme.Modern,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = _owner.LogicalToDeviceUnits(32),
                Margin = new Padding(0, 2, 0, 2)
            };
            input.TextCommitted += (_, _) => commit(input.Text);
            return input;
        }

        // 可点的文字：随宽度换行，长来源也能看全；鼠标停留显示完整文字。
        private Label Link()
        {
            var label = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Cursor = Cursors.Hand,
                ForeColor = WorkflowWinFormsTheme.Modern.Primary,
                Margin = new Padding(0, 6, 0, 6)
            };
            label.TextChanged += (_, _) => _owner._tips.SetToolTip(label, label.Text);
            return label;
        }
    }

    // 在被点的文字下面弹出主题菜单。
    private void ShowMenu(Control anchor, Action<ModernContextMenu> fill)
    {
        var menu = new ModernContextMenu { Theme = WorkflowWinFormsTheme.Modern };
        fill(menu);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private static void Add(ToolStrip menu, string text, Action action, bool selected = false) =>
        menu.Items.Add(new ToolStripMenuItem(text, null, (_, _) => action()) { Checked = selected });

    private static TableLayoutPanel List()
    {
        var list = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = new Padding(0)
        };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return list;
    }

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary,
        Margin = new Padding(0, 4, 0, 8)
    };

    // 标题行：粗体标题 + 文字按钮“添加…”；说明另起一行，灰字、随宽度换行。
    private Control Header(string title, string addText, string hint, Action add)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 6, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var label = new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
        label.Font = new Font(Font, FontStyle.Bold);
        var button = WorkflowSizing.Fit(new ModernButton
        {
            Text = addText,
            Theme = WorkflowWinFormsTheme.Modern,
            ButtonType = ModernButtonType.Text,
            Icon = ModernIconKind.Plus,
            Anchor = AnchorStyles.Left
        });
        button.Click += (_, _) => add();
        var note = Hint(hint);
        note.Margin = new Padding(0, 2, 0, 0);
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(button, 1, 0);
        panel.Controls.Add(note, 0, 1);
        panel.SetColumnSpan(note, 2);
        return panel;
    }
}
