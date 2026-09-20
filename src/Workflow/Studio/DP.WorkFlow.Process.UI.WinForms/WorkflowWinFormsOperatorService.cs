using System.Windows.Forms;

namespace DP.WorkFlow.OperatorUI.WinForms;

/// <summary>
/// 真实WinForms人工选择/确认适配器。必须在所属窗体UI线程创建；不强制提前创建窗体句柄。
/// 提示采用非模态所属窗口，保留主界面的停止入口；结束、取消、关闭宿主时取消对应任务。
/// </summary>
public sealed class WorkflowWinFormsOperatorService : IWorkflowOperatorService, IDisposable
{
    private readonly Form _owner;
    private readonly WorkflowOperatorInteraction _interaction = new();
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private Form? _dialog;
    private Guid? _shownPrompt;
    private int _refreshQueued;
    private int _disposed;

    /// <summary>在窗体UI线程装配交互服务，可在主窗体尚未Show时创建。</summary>
    /// <param name="owner">提示窗口所属主窗体。</param>
    public WorkflowWinFormsOperatorService(Form owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        if (owner.IsDisposed) throw new ObjectDisposedException(nameof(owner));
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || owner.InvokeRequired)
            throw new InvalidOperationException("人工交互适配器必须在所属窗体STA线程创建。");
        _interaction.PromptChanged += OnPromptChanged;
        owner.HandleCreated += OnOwnerReady;
        owner.HandleDestroyed += OnOwnerHandleDestroyed;
        owner.VisibleChanged += OnOwnerReady;
        owner.Disposed += OnOwnerDisposed;
    }

    /// <summary>当前任务只读快照；不存在默认选择。</summary>
    public WorkflowOperatorPrompt? CurrentPrompt => _interaction.CurrentPrompt;

    /// <inheritdoc />
    public ValueTask<string> AskChoiceAsync(string title, string message, IReadOnlyList<OperatorChoiceOption> options, CancellationToken cancellationToken) =>
        _interaction.AskChoiceAsync(title, message, options, cancellationToken);

    /// <inheritdoc />
    public ValueTask ConfirmStepAsync(string title, string message, CancellationToken cancellationToken) =>
        _interaction.ConfirmStepAsync(title, message, cancellationToken);

    private void OnPromptChanged(object? sender, EventArgs e) => QueueRefresh();
    private void OnOwnerReady(object? sender, EventArgs e) => QueueRefresh();
    private void OnOwnerDisposed(object? sender, EventArgs e) { Dispose(); CleanupUi(); }
    private void OnOwnerHandleDestroyed(object? sender, EventArgs e)
    {
        Volatile.Write(ref _refreshQueued, 0);
        CloseDialog();
    }

    private void QueueRefresh()
    {
        if (_owner.IsDisposed) { Dispose(); return; }
        if (!_owner.IsHandleCreated) return;
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            _owner.BeginInvoke((Action)(() =>
            {
                Volatile.Write(ref _refreshQueued, 0);
                if (Volatile.Read(ref _disposed) != 0) CleanupUi(); else RefreshUi();
            }));
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _refreshQueued, 0);
            if (_owner.IsDisposed) Dispose();
        }
    }

    private void RefreshUi()
    {
        if (!_owner.Visible) { CloseDialog(); return; }
        var prompt = _interaction.CurrentPrompt;
        if (_dialog is not null && _shownPrompt == prompt?.Id) return;
        CloseDialog();
        prompt = _interaction.CurrentPrompt;
        if (prompt is null || Volatile.Read(ref _disposed) != 0) return;
        var dialog = CreateDialog(prompt);
        _dialog = dialog;
        _shownPrompt = prompt.Id;
        dialog.FormClosed += (_, _) =>
        {
            if (!ReferenceEquals(_dialog, dialog)) return;
            _dialog = null; _shownPrompt = null;
            _interaction.TryDismiss(prompt.Id);
        };
        try { dialog.Show(_owner); }
        catch (Exception failure)
        {
            _interaction.TryDismiss(prompt.Id);
            CloseDialog();
            System.Diagnostics.Debug.WriteLine(failure);
        }
    }

    private Form CreateDialog(WorkflowOperatorPrompt prompt)
    {
        var form = new Form
        {
            Name = "WorkflowOperatorPrompt",
            Text = string.IsNullOrWhiteSpace(prompt.Title) ? "人工处理" : prompt.Title,
            AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new System.Drawing.Size(560, 300),
            MinimumSize = new System.Drawing.Size(440, 260),
            StartPosition = FormStartPosition.CenterParent,
            ShowInTaskbar = false,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var contentFont = new System.Drawing.Font(System.Drawing.SystemFonts.MessageBoxFont?.Name ?? "Segoe UI", 10F);
        form.Font = contentFont;
        form.Disposed += (_, _) => contentFont.Dispose();
        var message = new TextBox
        {
            Name = "OperatorMessage", Text = prompt.Message.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"), Multiline = true, ReadOnly = true,
            WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None, BackColor = System.Drawing.SystemColors.Window,
            TabStop = false
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
        body.Controls.Add(message);
        var footer = new Label
        {
            Text = "仅提交人工选择或确认，不替代设备状态检查。关闭窗口不会默认同意。",
            Dock = DockStyle.Bottom, AutoSize = false, Height = 44, Padding = new Padding(12, 6, 12, 0)
        };
        var buttons = new FlowLayoutPanel
        {
            Name = "OperatorActions", Dock = DockStyle.Bottom, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12), WrapContents = true,
            MaximumSize = new System.Drawing.Size(0, 150), AutoScroll = true
        };
        void AddButton(string text, string key, Func<bool> respond)
        {
            var button = new Button
            {
                Text = text, Name = key, Tag = key, AutoSize = true, UseMnemonic = false,
                MinimumSize = new System.Drawing.Size(120, 36), Margin = new Padding(4), UseVisualStyleBackColor = true
            };
            button.Click += (_, _) =>
            {
                if (respond())
                    foreach (Control control in buttons.Controls) control.Enabled = false;
            };
            buttons.Controls.Add(button);
        }
        if (prompt.Kind == WorkflowOperatorPromptKind.Confirmation)
            AddButton("处理完成", "Confirm", () => _interaction.TryConfirm(prompt.Id));
        else
            foreach (var option in prompt.Options)
                AddButton(option.Text, option.Key, () => _interaction.TryChoose(prompt.Id, option.Key));
        form.Controls.Add(body);
        form.Controls.Add(footer);
        form.Controls.Add(buttons);
        // 不指定默认确认按钮，避免上一个窗口残留的Enter直接确认新任务。
        form.Shown += (_, _) => { message.Focus(); message.Select(0, 0); };
        return form;
    }

    private void CloseDialog()
    {
        var dialog = _dialog;
        _dialog = null;
        _shownPrompt = null;
        if (dialog is null) return;
        dialog.Close();
        dialog.Dispose();
    }

    private void CleanupUi()
    {
        _owner.HandleCreated -= OnOwnerReady;
        _owner.HandleDestroyed -= OnOwnerHandleDestroyed;
        _owner.VisibleChanged -= OnOwnerReady;
        _owner.Disposed -= OnOwnerDisposed;
        CloseDialog();
    }

    /// <summary>取消当前及排队的人工任务，并在UI线程关闭窗口；可从停止流程的后台线程调用。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _interaction.PromptChanged -= OnPromptChanged;
        _interaction.Dispose();
        if (Environment.CurrentManagedThreadId == _uiThreadId) CleanupUi(); else QueueRefresh();
    }
}
