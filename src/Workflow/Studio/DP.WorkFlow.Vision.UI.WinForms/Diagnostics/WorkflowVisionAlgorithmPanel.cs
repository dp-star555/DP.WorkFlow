using DP.WorkFlow.UI;

namespace DP.WorkFlow.Vision.UI.WinForms;

/// <summary>插件清单与可取消的配方资源检查。</summary>
public sealed class WorkflowVisionAlgorithmPanel : UserControl
{
    private readonly WorkflowVisionAlgorithmDiagnostics _diagnostics;
    private readonly Func<WorkflowDocument?> _document;
    private readonly Func<WorkflowDesignerSession?> _session;
    private readonly Label _status = new() { Dock = DockStyle.Bottom, AutoSize = false, Height = 26 };
    private readonly Button _check = new() { Text = "检查配方资源", AutoSize = true };
    private readonly Button _cancel = new() { Text = "取消检查", AutoSize = true, Enabled = false };
    private CancellationTokenSource? _cancellation;

    /// <summary>宿主提供根配方及当前选中节点所在会话。</summary>
    public WorkflowVisionAlgorithmPanel(WorkflowVisionAlgorithmDiagnostics diagnostics, Func<WorkflowDocument?> document,
        Func<WorkflowDesignerSession?> session)
    {
        _diagnostics = diagnostics; _document = document; _session = session;
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoGenerateColumns = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        foreach (var column in new[] { ("ImplementationId", "实现"), ("Engine", "引擎"), ("Capability", "能力"), ("Version", "版本"), ("Features", "特征"), ("Source", "来源") })
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = column.Item1, HeaderText = column.Item2 });
        grid.DataSource = diagnostics.Catalog.Implementations.Select(d => new
        {
            d.ImplementationId, d.Engine, Capability = d.DisplayName, d.Version, Features = string.Join(", ", d.Features),
            Source = diagnostics.Catalog.Origins.TryGetValue(d.ImplementationId, out var origin) ? origin.AssemblyPath : ""
        }).ToArray();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
        var migrate = new Button { Text = "升级所选节点配置", AutoSize = true };
        var export = new Button { Text = "导出复核报告", AutoSize = true };
        toolbar.Controls.AddRange(new Control[] { _check, _cancel, migrate, export });
        Controls.Add(grid); Controls.Add(_status); Controls.Add(toolbar);
        _check.Click += async (_, _) => await CheckAsync();
        _cancel.Click += (_, _) => _cancellation?.Cancel();
        migrate.Click += (_, _) => Perform(() =>
        {
            var current = _session() ?? throw new InvalidOperationException("没有打开的配方。");
            _diagnostics.MigrateNode(current, current.SelectedNodeId ?? throw new InvalidOperationException("请先选择算法节点。"));
        });
        export.Click += (_, _) => Perform(() =>
        {
            var current = _document() ?? throw new InvalidOperationException("没有打开的配方。");
            using var dialog = new SaveFileDialog { Filter = "JSON 报告|*.json", FileName = "algorithm-review.json" };
            if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, _diagnostics.ExportReport(current));
        });
        diagnostics.Changed += OnChanged;
        _status.Text = diagnostics.Status;
    }
    private void Perform(Action action)
    { try { action(); } catch (Exception error) { MessageBox.Show(this, error.Message, "算法配置", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private async Task CheckAsync()
    {
        var document = _document();
        if (document == null || _cancellation != null) return;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        _check.Enabled = false; _cancel.Enabled = true;
        try { await _diagnostics.CheckAsync(document, cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsDisposed) MessageBox.Show(this, error.Message, "资源检查失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _cancellation = null; if (!IsDisposed) { _check.Enabled = true; _cancel.Enabled = false; } }
    }
    private void OnChanged(object? sender, EventArgs e)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(new Action(() => OnChanged(sender, e))); return; }
        _status.Text = _diagnostics.Status;
    }
    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    { if (disposing) { _diagnostics.Changed -= OnChanged; _cancellation?.Cancel(); } base.Dispose(disposing); }
}
