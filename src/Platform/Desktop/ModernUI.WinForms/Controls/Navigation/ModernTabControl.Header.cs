using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

public sealed partial class ModernTabControl
{
    private sealed class TabControlCollectionAdapter(ModernTabControl owner) : Control.ControlCollection(owner)
    {
        public override void Add(Control? value)
        {
            if (value is TabPage page)
            {
                owner._nativeTabs.TabPages.Add(page);
                return;
            }
            base.Add(value);
        }
    }

    private sealed class HeaderStrip : Control
    {
        private readonly ModernTabControl _owner;
        private int _hoveredIndex = -1;
        private RectangleF _indicatorBounds = RectangleF.Empty;
        private RectangleF _selectionSurfaceBounds = RectangleF.Empty;
        private IDisposable? _indicatorAnimation;
        private IDisposable? _compositedRenderingScope;
        private int _selectionFromIndex = -1;
        private int _selectionToIndex = -1;
        private float _selectionProgress = 1;
        private bool _isSelectionAnimating;
        private ModernTheme _theme = ModernUiSettings.DefaultTheme;

        public HeaderStrip(ModernTabControl owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public ModernTheme Theme
        {
            get => _theme;
            set { _theme = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background);
            using var divider = new Pen(Theme.Border, Math.Max(1, ModernDpi.ScaleToInt(1, DeviceDpi)));
            var dividerY = _owner.Alignment == TabAlignment.Bottom ? 0 : Height - 1;
            e.Graphics.DrawLine(divider, 0, dividerY, Width, dividerY);

            if (_selectionSurfaceBounds.IsEmpty && _owner.SelectedIndex >= 0)
                _selectionSurfaceBounds = _owner.GetHeaderItemBounds(_owner.SelectedIndex);
            if (!_selectionSurfaceBounds.IsEmpty)
            {
                using var selectedBackground = new SolidBrush(Theme.Container);
                e.Graphics.FillRectangle(selectedBackground, _selectionSurfaceBounds);
            }

            for (var index = 0; index < _owner.TabCount; index++)
            {
                var bounds = _owner.GetHeaderItemBounds(index);
                if (bounds.IsEmpty) continue;
                var selected = index == _owner.SelectedIndex;
                if (index == _hoveredIndex && !selected)
                {
                    using var hoverBackground = new SolidBrush(Theme.ControlHover);
                    e.Graphics.FillRectangle(hoverBackground, bounds);
                }
                var page = _owner.TabPages[index];
                var image = _owner.GetPageImage(page);
                var textWidth = TextRenderer.MeasureText(page.Text, Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                var iconSize = image is null ? 0 : _owner.ScaleLogical(16);
                var gap = image is null ? 0 : _owner.ScaleLogical(6);
                var contentWidth = Math.Min(bounds.Width, textWidth + iconSize + gap);
                var contentX = bounds.X + Math.Max(0, (bounds.Width - contentWidth) / 2);
                if (image is not null)
                {
                    var imageBounds = new Rectangle(contentX, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
                    e.Graphics.DrawImage(image, imageBounds);
                    contentX += iconSize + gap;
                }
                var textColor = selected ? Theme.Primary : Theme.TextSecondary;
                if (_isSelectionAnimating)
                {
                    if (index == _selectionFromIndex)
                        textColor = Geometry.Blend(Theme.Primary, Theme.TextSecondary, _selectionProgress);
                    else if (index == _selectionToIndex)
                        textColor = Geometry.Blend(Theme.TextSecondary, Theme.Primary, _selectionProgress);
                }
                TextRenderer.DrawText(e.Graphics, page.Text, Font,
                    new Rectangle(contentX, bounds.Y, Math.Max(0, bounds.Right - contentX), bounds.Height),
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            if (_indicatorBounds.IsEmpty && _owner.SelectedIndex >= 0)
                _indicatorBounds = GetIndicatorBounds(_owner.SelectedIndex);
            if (!_indicatorBounds.IsEmpty)
            {
                using var indicator = new SolidBrush(Theme.Primary);
                e.Graphics.FillRectangle(indicator, _indicatorBounds);
            }

            if (_owner.Focused && _owner.ShowFocusCues && _owner.SelectedIndex >= 0)
                ControlPaint.DrawFocusRectangle(e.Graphics,
                    Rectangle.Inflate(_owner.GetHeaderItemBounds(_owner.SelectedIndex), -4, -4),
                    Theme.Text, Color.Transparent);
        }

        public void AnimateIndicator(int previousIndex, int selectedIndex)
        {
            _indicatorAnimation?.Dispose();
            _compositedRenderingScope?.Dispose();
            _compositedRenderingScope = null;
            _isSelectionAnimating = false;
            var target = GetIndicatorBounds(selectedIndex);
            var targetSurface = (RectangleF)_owner.GetHeaderItemBounds(selectedIndex);
            if (target.IsEmpty || targetSurface.IsEmpty)
            {
                _indicatorBounds = RectangleF.Empty;
                _selectionSurfaceBounds = RectangleF.Empty;
                Invalidate();
                return;
            }
            var start = !_indicatorBounds.IsEmpty
                ? _indicatorBounds
                : GetIndicatorBounds(previousIndex);
            if (start.IsEmpty) start = target;
            var startSurface = !_selectionSurfaceBounds.IsEmpty
                ? _selectionSurfaceBounds
                : (RectangleF)_owner.GetHeaderItemBounds(previousIndex);
            if (startSurface.IsEmpty) startSurface = targetSurface;
            if (start == target && startSurface == targetSurface)
            {
                _indicatorBounds = target;
                _selectionSurfaceBounds = targetSurface;
                _selectionProgress = 1;
                Invalidate();
                return;
            }

            _selectionFromIndex = previousIndex;
            _selectionToIndex = selectedIndex;
            _selectionProgress = 0;
            _isSelectionAnimating = true;
            _compositedRenderingScope = SuspendAncestorCompositedRendering();
            _indicatorAnimation = ModernAnimation.Start(this, 0, 1, _owner.SelectionAnimationDuration,
                progress =>
                {
                    _selectionProgress = progress;
                    _indicatorBounds = Interpolate(start, target, progress);
                    _selectionSurfaceBounds = Interpolate(startSurface, targetSurface, progress);
                    PresentAnimationFrame();
                }, ModernAnimationEasing.EaseInOutCubic, () =>
                {
                    _indicatorAnimation = null;
                    _compositedRenderingScope?.Dispose();
                    _compositedRenderingScope = null;
                    _isSelectionAnimating = false;
                    _selectionProgress = 1;
                    _selectionSurfaceBounds = targetSurface;
                    Invalidate();
                });
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!_isSelectionAnimating && _owner.SelectedIndex >= 0)
            {
                _indicatorBounds = GetIndicatorBounds(_owner.SelectedIndex);
                _selectionSurfaceBounds = _owner.GetHeaderItemBounds(_owner.SelectedIndex);
            }
        }

        private IDisposable? SuspendAncestorCompositedRendering()
        {
            for (Control? ancestor = _owner.Parent; ancestor is not null; ancestor = ancestor.Parent)
                if (ancestor is ModernScrollView scrollView)
                    return scrollView.SuspendCompositedRendering();
            return null;
        }

        private void PresentAnimationFrame() => Invalidate();

        private static RectangleF Interpolate(RectangleF from, RectangleF to, float progress) => new(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress,
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);

        private RectangleF GetIndicatorBounds(int index)
        {
            var bounds = _owner.GetHeaderItemBounds(index);
            if (bounds.IsEmpty) return RectangleF.Empty;
            var thickness = Math.Max(2, ModernDpi.ScaleToInt(2, DeviceDpi));
            var inset = ModernDpi.ScaleToInt(8, DeviceDpi);
            return _owner.Alignment == TabAlignment.Bottom
                ? new RectangleF(bounds.Left + inset, 0, Math.Max(0, bounds.Width - inset * 2), thickness)
                : new RectangleF(bounds.Left + inset, bounds.Bottom - thickness,
                    Math.Max(0, bounds.Width - inset * 2), thickness);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = _owner.HeaderIndexFromPoint(e.Location);
            if (_hoveredIndex == index) return;
            _hoveredIndex = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoveredIndex = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _indicatorAnimation?.Dispose();
                _compositedRenderingScope?.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var index = _owner.HeaderIndexFromPoint(e.Location);
            if (index < 0) return;
            _owner.SelectedIndex = index;
            _owner.Focus();
        }
    }

    private sealed class HiddenHeaderTabControl : TabControl
    {
        public HiddenHeaderTabControl()
        {
            Appearance = TabAppearance.FlatButtons;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(0, 1);
            Multiline = false;
        }
    }
}
