using ModernUI.WinForms;
using DP.WorkFlow.UI;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>
/// 节点窗口“输入输出”页：编辑复合节点子流程的输入和输出。
/// 输入的来源选父画布中本节点之前的节点输出（类型跟随来源），或使用手动值（整数、小数、布尔、文本）；
/// 子画布中通过“启动节点.名称”读取。输出的来源选子画布中的节点输出，父画布中通过“本节点.名称”读取。
/// </summary>
/// <remarks>
/// 页面在节点窗口左边的窄栏里，表格放不下名称、来源、类型、值几列。所以每一项是可展开收起的一行：
/// 收起时显示名称和一句摘要，展开后一行一个字段（名称 / 来源 / 类型 / 值），整页竖向滚动。
/// 修改后不重建控件：按位置复用已有的行、只更新文字，增删项时只加 / 减末尾的行，避免闪烁。
/// </remarks>
internal sealed class WorkflowSubflowInterfaceControl : UserControl
{
    private const string Drop = "  ▾";
    private static readonly E_WorkflowSubflowValueType[] ValueTypes = Polyfill.GetEnumValues<E_WorkflowSubflowValueType>();

    private readonly WorkflowSubflowInterfacePageModel _model;
    private readonly TableLayoutPanel _inputs = List();
    private readonly TableLayoutPanel _outputs = List();
    private readonly Label _noInputs = Hint("还没有输入。");
    private readonly Label _noOutputs = Hint("还没有输出。");
    private readonly ToolTip _tips = new();
    private readonly Panel _scroller = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true };
    private bool _refreshQueued;
    private bool _loaded;

    public WorkflowSubflowInterfaceControl(WorkflowSubflowInterfacePageModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        DoubleBuffered = true;
        var body = new BufferedTable
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
        body.Controls.Add(_noInputs);
        body.Controls.Add(_inputs);
        body.Controls.Add(Header("输出", "添加输出",
            "来源选子画布中的节点输出，类型自动跟随来源。父画布中绑定“本节点.名称”读取。",
            () => _model.AddOutput()));
        body.Controls.Add(_noOutputs);
        body.Controls.Add(_outputs);
        _scroller.Controls.Add(body);
        Controls.Add(_scroller);

        _model.Changed += OnModelChanged;
        RefreshRows();
        _loaded = true;
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
        // 修改常由行里的输入框提交触发，推迟到事件结束后刷新，避免在事件里释放正在用的控件。
        if (IsDisposed || _refreshQueued) return;
        _refreshQueued = true;
        if (IsHandleCreated) BeginInvoke(RefreshRows);
        else RefreshRows();
    }

    private void RefreshRows()
    {
        _refreshQueued = false;
        if (IsDisposed) return;
        _scroller.SuspendLayout();
        try
        {
            Sync(_inputs, _noInputs, _model.Inputs.Count, () => new Item(this, input: true));
            Sync(_outputs, _noOutputs, _model.Outputs.Count, () => new Item(this, input: false));
            for (var i = 0; i < _model.Inputs.Count; i++) ItemAt(_inputs, i).Show(_model.Inputs[i]);
            for (var i = 0; i < _model.Outputs.Count; i++) ItemAt(_outputs, i).Show(_model.Outputs[i]);
        }
        finally
        {
            _scroller.ResumeLayout(true);
        }
    }

    private static Item ItemAt(TableLayoutPanel list, int index) => (Item)list.Controls[index].Tag!;

    // 行数和模型对齐：多了去掉末尾的，少了在末尾加（新加的一项展开，方便直接填写）。
    private void Sync(TableLayoutPanel list, Label empty, int count, Func<Item> create)
    {
        list.SuspendLayout();
        try
        {
            while (list.Controls.Count > count)
            {
                var last = list.Controls[list.Controls.Count - 1];
                list.Controls.Remove(last);
                last.Dispose();
                list.RowStyles.RemoveAt(list.RowStyles.Count - 1);
            }
            while (list.Controls.Count < count)
            {
                var item = create();
                item.Index = list.Controls.Count;
                item.SetOpen(_loaded);
                list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                list.Controls.Add(item.Root);
            }
            empty.Visible = count == 0;
            list.Visible = count > 0;
        }
        finally
        {
            list.ResumeLayout(true);
        }
    }

    private void ChooseInputSource(Item item) => ShowMenu(item.Source, menu =>
    {
        var index = item.Index;
        Add(menu, "选择父画布中的来源…", () =>
        {
            using var dialog = new WorkflowBindingSelectorDialog(_model.GetInputCandidates(), _model.Inputs[index].Source);
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
                _model.SetInputSource(index, candidate.ToBindingKey());
        });
        Add(menu, "使用手动值", () => _model.SetInputSource(index, null));
    });

    private void ChooseInputType(Item item)
    {
        var index = item.Index;
        if (_model.Inputs[index].IsBound) return;
        ShowMenu(item.Type, menu =>
        {
            foreach (var type in ValueTypes)
                Add(menu, WorkflowSubflowInput.DisplayName(type), () => _model.SetInputValueType(index, type), type == _model.Inputs[index].ValueType);
        });
    }

    private void ChooseOutputSource(Item item)
    {
        var index = item.Index;
        using var dialog = new WorkflowBindingSelectorDialog(_model.GetOutputCandidates(), _model.Outputs[index].Source);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.SelectedCandidate is { } candidate)
            _model.SetOutputSource(index, candidate.ToBindingKey());
    }

    /// <summary>
    /// 一项输入或输出：可展开收起的一行。收起时只有标题行（▸ 名称  摘要，右边删除），
    /// 点标题行展开下面的字段：名称、值是输入框（回车或离开时提交）；来源、类型是可点的文字（带 ▾）。
    /// 项与项之间只画一条分隔线。
    /// </summary>
    private sealed class Item
    {
        private readonly WorkflowSubflowInterfaceControl _owner;
        private readonly bool _input;
        private readonly TableLayoutPanel _details;
        private readonly Label _chevron;
        private readonly Label _title;
        private readonly Label _summary;
        private readonly ModernInput _name;
        private readonly ModernInput _value;
        private readonly Label _valueCaption;

        public Item(WorkflowSubflowInterfaceControl owner, bool input)
        {
            _owner = owner;
            _input = input;
            Root = new BufferedTable
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0, 0, 0, 1),
                Tag = this
            };
            Root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Root.Paint += (_, e) =>
            {
                using var pen = new Pen(WorkflowWinFormsTheme.Modern.BorderSecondary);
                e.Graphics.DrawLine(pen, 0, Root.Height - 1, Root.Width, Root.Height - 1);
            };

            var header = new BufferedTable
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _chevron = new Label { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary, Margin = new Padding(0, 8, 4, 8) };
            _title = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 10, 8) };
            _title.Font = new Font(owner.Font, FontStyle.Bold);
            _summary = new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary,
                Margin = new Padding(0)
            };
            var delete = WorkflowSizing.Fit(new ModernButton
            {
                Text = "删除",
                Theme = WorkflowWinFormsTheme.Modern,
                ButtonType = ModernButtonType.Text,
                Anchor = AnchorStyles.Right
            });
            delete.Click += (_, _) =>
            {
                if (_input) owner._model.RemoveInput(Index);
                else owner._model.RemoveOutput(Index);
            };
            header.Controls.Add(_chevron, 0, 0);
            header.Controls.Add(_title, 1, 0);
            header.Controls.Add(_summary, 2, 0);
            header.Controls.Add(delete, 3, 0);

            _details = new BufferedTable
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(18, 0, 4, 8)
            };
            _details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _name = Input(text =>
            {
                if (_input) owner._model.RenameInput(Index, text);
                else owner._model.RenameOutput(Index, text);
            });
            Source = Link();
            Source.Click += (_, _) =>
            {
                if (_input) owner.ChooseInputSource(this);
                else owner.ChooseOutputSource(this);
            };
            Type = Link();
            if (_input) Type.Click += (_, _) => owner.ChooseInputType(this);
            _value = Input(text => owner._model.SetInputValue(Index, text));
            AddRow("名称", _name);
            AddRow("来源", Source);
            AddRow("类型", Type);
            _valueCaption = AddRow("值", _value);

            Root.Controls.Add(header, 0, 0);
            Root.Controls.Add(_details, 0, 1);
            void Toggle(object? sender, EventArgs e) => SetOpen(!_details.Visible);
            header.Click += Toggle;
            _chevron.Click += Toggle;
            _title.Click += Toggle;
            _summary.Click += Toggle;
        }

        public TableLayoutPanel Root { get; }

        public Label Source { get; }

        public Label Type { get; }

        /// <summary>在列表中的位置，也是模型里这一项的序号。</summary>
        public int Index { get; set; }

        public void SetOpen(bool open)
        {
            _details.Visible = open;
            SetText(_chevron, open ? "▾" : "▸");
        }

        public void Show(WorkflowSubflowInputRow input)
        {
            SetName(input.Name);
            SetText(Source, input.SourceText + Drop);
            if (input.IsBound)
            {
                // 绑定来源时类型跟随来源，不能修改，也没有手动值。
                SetSummary($"{input.SourceText} · {input.TypeText}");
                SetText(Type, input.TypeText + "（跟随来源）");
                Type.ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary;
                Type.Cursor = Cursors.Default;
            }
            else
            {
                var type = WorkflowSubflowInput.DisplayName(input.ValueType);
                SetSummary($"{type} = {input.ValueText}");
                SetText(Type, type + Drop);
                Type.ForeColor = WorkflowWinFormsTheme.Modern.Primary;
                Type.Cursor = Cursors.Hand;
                SetInput(_value, input.ValueText);
            }
            _value.Visible = _valueCaption.Visible = !input.IsBound;
        }

        public void Show(WorkflowSubflowOutputRow output)
        {
            SetName(output.Name);
            SetSummary(output.Source.HasValue ? $"{output.SourceText} · {output.TypeText}" : "未选择来源");
            SetText(Source, output.SourceText + Drop);
            SetText(Type, output.TypeText);
            Type.ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary;
            Type.Cursor = Cursors.Default;
            _value.Visible = _valueCaption.Visible = false;
        }

        private void SetName(string text)
        {
            SetText(_title, text);
            SetInput(_name, text);
        }

        private void SetSummary(string text)
        {
            if (SetText(_summary, text)) _owner._tips.SetToolTip(_summary, text);
        }

        // 文字没变就不赋值，免得重新排版引起闪动。
        private static bool SetText(Control control, string text)
        {
            if (control.Text == text) return false;
            control.Text = text;
            return true;
        }

        // 正在编辑的框不覆盖，免得打字时被刷新打断。
        private static void SetInput(ModernInput input, string text)
        {
            if (!input.ContainsFocus) SetText(input, text);
        }

        private Label AddRow(string caption, Control content)
        {
            var row = _details.RowCount++;
            _details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                ForeColor = WorkflowWinFormsTheme.Modern.TextSecondary,
                Margin = new Padding(0, 6, 10, 6)
            };
            _details.Controls.Add(label, 0, row);
            _details.Controls.Add(content, 1, row);
            return label;
        }

        private ModernInput Input(Action<string> commit)
        {
            var input = new ModernInput
            {
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

    /// <summary>双缓冲的表格布局，增删行、改文字时不闪。</summary>
    private sealed class BufferedTable : TableLayoutPanel
    {
        public BufferedTable()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    private sealed class BufferedPanel : Panel
    {
        public BufferedPanel() => DoubleBuffered = true;
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
        var list = new BufferedTable
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
