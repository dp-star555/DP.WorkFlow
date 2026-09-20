using System.Runtime.CompilerServices;

namespace ModernUI.WinForms;

/// <summary>Keeps native ToolStrip drop-down behavior while applying the shared rounded window shape.</summary>
internal sealed class ModernToolStripDropDownRegion
{
    private static readonly ConditionalWeakTable<ToolStripDropDown, ModernToolStripDropDownRegion> Registrations = new();
    private readonly ToolStripDropDown _dropDown;
    private int _logicalRadius;

    private ModernToolStripDropDownRegion(ToolStripDropDown dropDown, int logicalRadius)
    {
        _dropDown = dropDown;
        _logicalRadius = Math.Max(0, logicalRadius);
        dropDown.HandleCreated += DropDownGeometryChanged;
        dropDown.SizeChanged += DropDownGeometryChanged;
        dropDown.Opened += DropDownGeometryChanged;
        dropDown.Disposed += DropDownDisposed;
        ApplyRegion();
    }

    public static void Attach(ToolStripDropDown dropDown, int logicalRadius)
    {
        var registration = Registrations.GetValue(dropDown,
            key => new ModernToolStripDropDownRegion(key, logicalRadius));
        registration._logicalRadius = Math.Max(0, logicalRadius);
        registration.ApplyRegion();
    }

    private void DropDownGeometryChanged(object? sender, EventArgs e) => ApplyRegion();

    private void ApplyRegion()
    {
        if (_dropDown.IsDisposed || _dropDown.Width <= 0 || _dropDown.Height <= 0) return;
        RoundedNativeControlRegion.Apply(_dropDown, _logicalRadius);
    }

    private void DropDownDisposed(object? sender, EventArgs e)
    {
        _dropDown.HandleCreated -= DropDownGeometryChanged;
        _dropDown.SizeChanged -= DropDownGeometryChanged;
        _dropDown.Opened -= DropDownGeometryChanged;
        _dropDown.Disposed -= DropDownDisposed;
        Registrations.Remove(_dropDown);
    }
}
