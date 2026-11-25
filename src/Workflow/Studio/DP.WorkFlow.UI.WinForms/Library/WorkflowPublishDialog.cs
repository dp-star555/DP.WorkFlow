using System.Globalization;
using DP.WorkFlow.Persistence.Json;
using ModernUI.WinForms;

namespace DP.WorkFlow.UI.WinForms;

/// <summary>发布确认框：上面是这次发布改了什么，下面填发布说明。流程库页和设计页共用。</summary>
public sealed class WorkflowPublishDialog : Form
{
    private readonly ModernInput _note = new() { Dock = DockStyle.Top, MaxLength = 200, Height = 34, PlaceholderText = "这次改了什么" };

    private WorkflowPublishDialog(string name, string changes)
    {
        Text = $"发布 · {name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 420);
        MinimumSize = new Size(420, 300);
        ShowInTaskbar = false;
        ShowIcon = false;
        MinimizeBox = false;
        MaximizeBox = false;
        Padding = new Padding(10);
        var changesBox = new ModernTextArea { Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = changes };
        var hint = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 40,
            Text = "发布后替换正式流程文件，记一个版本，没有流程在运行时重新加载。"
        };
        var noteLabel = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 22, Text = "发布说明（记进版本历史）：" };
        var ok = WorkflowSizing.Fit(new ModernButton { Text = "发布", ButtonType = ModernButtonType.Primary, DialogResult = DialogResult.OK });
        var cancel = WorkflowSizing.Fit(new ModernButton { Text = "取消", DialogResult = DialogResult.Cancel });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.AddRange(new Control[] { cancel, ok });
        var lower = new Panel { Dock = DockStyle.Bottom, Height = 64 };
        lower.Controls.Add(_note);
        lower.Controls.Add(noteLabel);
        Controls.Add(changesBox);
        Controls.Add(hint);
        Controls.Add(lower);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>
    /// 走一遍发布：先检查（不过就列出原因），再确认并填说明，最后发布。
    /// </summary>
    /// <param name="owner">父窗口。</param>
    /// <param name="publisher">发布器。</param>
    /// <param name="name">库里的名字。</param>
    /// <returns>发布后的版本；取消或没通过检查时为空。</returns>
    /// <exception cref="InvalidOperationException">发布被准入拒绝或改文件出错。</exception>
    public static async Task<WorkflowLibraryVersion?> RunAsync(IWin32Window? owner, WorkflowDraftPublisher publisher, string name)
    {
        if (publisher is null) throw new ArgumentNullException(nameof(publisher));
        var issues = publisher.Check(name);
        if (issues.Count > 0)
        {
            MessageBox.Show(owner, string.Join(Environment.NewLine, issues.Take(30)) + (issues.Count > 30 ? Environment.NewLine + $"……共 {issues.Count} 条" : string.Empty),
                $"{name} 不能发布", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        using var dialog = new WorkflowPublishDialog(name, publisher.DescribeChanges(name));
        WorkflowWinFormsTheme.Follow(dialog);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return null;
        return await publisher.PublishAsync(name, dialog._note.Text).ConfigureAwait(true);
    }

    /// <summary>版本的一行说明，例如“v3（张工）”。</summary>
    /// <param name="version">版本。</param>
    public static string Describe(WorkflowLibraryVersion version) =>
        "v" + version.Version.ToString(CultureInfo.InvariantCulture) + (version.SavedBy is { Length: > 0 } by ? $"（{by}）" : string.Empty);
}
