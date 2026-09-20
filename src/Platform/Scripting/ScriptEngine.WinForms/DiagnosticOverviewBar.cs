using Microsoft.CodeAnalysis;

namespace ScriptEngine.WinForms;

/// <summary>在编辑器右侧按文档行位置绘制错误和警告标记。</summary>
internal sealed class DiagnosticOverviewBar : Control
{
    private readonly List<OverviewMarker> _markers = new();
    private int _lineCount = 1;

    public DiagnosticOverviewBar()
    {
        Name = "ScriptDiagnosticOverview";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public event EventHandler<int>? LineRequested;

    public Color ErrorColor { get; set; } = Color.FromArgb(245, 80, 80);

    public Color WarningColor { get; set; } = Color.FromArgb(245, 180, 60);

    public void SetDiagnostics(IEnumerable<RoslynScriptDiagnostic> diagnostics, int lineCount)
    {
        _lineCount = Math.Max(1, lineCount);
        _markers.Clear();
        _markers.AddRange(diagnostics
            .Where(item => item.Origin == RoslynScriptDiagnosticOrigin.UserSource
                           && item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(item => new OverviewMarker(
                Math.Max(0, item.Line - 1),
                item.Severity == DiagnosticSeverity.Error))
            .GroupBy(item => item.Line)
            .Select(group => group.Any(item => item.IsError)
                ? new OverviewMarker(group.Key, true)
                : new OverviewMarker(group.Key, false))
            .OrderBy(item => item.Line));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.Clear(BackColor);
        using var border = new Pen(Color.FromArgb(
            48,
            ForeColor.R,
            ForeColor.G,
            ForeColor.B));
        eventArgs.Graphics.DrawLine(border, 0, 0, 0, Height);
        if (_markers.Count == 0 || Height <= 2) return;

        foreach (var marker in _markers)
        {
            var y = GetMarkerY(marker.Line);
            using var brush = new SolidBrush(marker.IsError ? ErrorColor : WarningColor);
            eventArgs.Graphics.FillRectangle(brush, 2, y, Math.Max(2, Width - 3), 3);
        }
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button != MouseButtons.Left || _markers.Count == 0) return;
        var nearest = _markers
            .OrderBy(marker => Math.Abs(GetMarkerY(marker.Line) - eventArgs.Y))
            .First();
        LineRequested?.Invoke(this, nearest.Line);
    }

    private int GetMarkerY(int line)
    {
        if (_lineCount <= 1) return 1;
        var ratio = Math.Min(_lineCount - 1, Math.Max(0, line)) / (double)(_lineCount - 1);
        return 1 + (int)Math.Round(ratio * Math.Max(0, Height - 4));
    }

    private sealed class OverviewMarker
    {
        public OverviewMarker(int line, bool isError)
        {
            Line = line;
            IsError = isError;
        }

        public int Line { get; }
        public bool IsError { get; }
    }
}
