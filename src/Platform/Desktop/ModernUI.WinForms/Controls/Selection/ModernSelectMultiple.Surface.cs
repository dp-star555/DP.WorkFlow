using System.Collections;
using System.ComponentModel;

namespace ModernUI.WinForms;

public sealed partial class ModernSelectMultiple
{
    /// <summary>单窗口双缓冲列表画布，避免 Win32 ListBox 对单个 OwnerDraw 行反复擦除背景。</summary>
    private sealed class MultiSelectSurface : Control
    {
        private int _hoveredIndex = -1;
        private int _scrollOffset;
        private int _itemCount;
        private bool _ignoreStationaryMouseMove;
        private Point _keyboardCursorPosition;

        public MultiSelectSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
        }

        public int ItemHeight { get; set; } = 30;
        public int VisibleRows { get; set; } = 8;
        public int InitialSelectedIndex { get; set; }
        public int ItemCount
        {
            get => _itemCount;
            set
            {
                _itemCount = Math.Max(0, value);
                if (_hoveredIndex >= _itemCount) SetHoveredIndex(-1);
                _scrollOffset = ModernCompatibility.Clamp(_scrollOffset, 0, MaxScrollOffset);
                Invalidate();
            }
        }

        private int MaxScrollOffset => Math.Max(0, ItemCount - Math.Max(1, VisibleRows));

        public event Action<Graphics, int, Rectangle>? PaintItem;
        public event Action<object?, int>? HoveredIndexChanged;
        public event Action<object?, int>? ItemClicked;
        public event EventHandler? CloseRequested;

        public void ResetHoveredIndex() => SetHoveredIndex(-1);

        public void InvalidateItem(int index)
        {
            var bounds = GetItemRectangle(index);
            if (bounds.Bottom > 0 && bounds.Top < ClientSize.Height) Invalidate(bounds);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var first = _scrollOffset;
            var last = Math.Min(ItemCount, first + Math.Max(1, VisibleRows) + 1);
            for (var index = first; index < last; index++)
                PaintItem?.Invoke(e.Graphics, index, GetItemRectangle(index));
            PaintScrollIndicator(e.Graphics);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_ignoreStationaryMouseMove)
            {
                if (Cursor.Position == _keyboardCursorPosition) return;
                _ignoreStationaryMouseMove = false;
            }
            SetHoveredIndex(IndexFromPoint(e.Location));
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            SetHoveredIndex(-1);
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var index = IndexFromPoint(e.Location);
            if (index >= 0) ItemClicked?.Invoke(this, index);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScrollOffset == 0) return;
            var lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            var next = ModernCompatibility.Clamp(_scrollOffset - Math.Sign(e.Delta) * lines, 0, MaxScrollOffset);
            if (next == _scrollOffset) return;
            _scrollOffset = next;
            SetHoveredIndex(IndexFromPoint(PointToClient(Cursor.Position)));
            Invalidate();
        }

        public bool HandleKey(Keys keyData)
        {
            _keyboardCursorPosition = Cursor.Position;
            _ignoreStationaryMouseMove = true;
            if (keyData == Keys.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }
            if (DropDownKeyboardNavigation.TryMove(keyData, _hoveredIndex, InitialSelectedIndex,
                    ItemCount, VisibleRows, out var target))
            {
                SetHoveredIndex(target);
                EnsureVisible(_hoveredIndex);
                return true;
            }
            if (keyData is Keys.Enter or Keys.Space && _hoveredIndex >= 0)
            {
                ItemClicked?.Invoke(this, _hoveredIndex);
                return true;
            }
            return false;
        }

        private void EnsureVisible(int index)
        {
            if (index < _scrollOffset) _scrollOffset = index;
            else if (index >= _scrollOffset + VisibleRows) _scrollOffset = index - VisibleRows + 1;
            Invalidate();
        }

        private int IndexFromPoint(Point point)
        {
            if (point.X < 0 || point.X >= ClientSize.Width || point.Y < 0 || point.Y >= ClientSize.Height) return -1;
            var index = _scrollOffset + point.Y / Math.Max(1, ItemHeight);
            return index < ItemCount ? index : -1;
        }

        private Rectangle GetItemRectangle(int index) =>
            new(0, (index - _scrollOffset) * ItemHeight, ClientSize.Width, ItemHeight);

        private void SetHoveredIndex(int index)
        {
            if (_hoveredIndex == index) return;
            var previous = _hoveredIndex;
            _hoveredIndex = index;
            if (previous >= 0) InvalidateItem(previous);
            if (index >= 0) InvalidateItem(index);
            HoveredIndexChanged?.Invoke(this, index);
        }

        private void PaintScrollIndicator(Graphics graphics)
        {
            if (MaxScrollOffset == 0 || ItemCount == 0) return;
            var inset = ModernDpi.Scale(4, DeviceDpi);
            var thumbWidth = ModernDpi.Scale(ModernScrollThumbRenderer.DefaultThickness, DeviceDpi);
            var trackHeight = ClientSize.Height - inset * 2;
            var thumbHeight = Math.Max(ModernDpi.Scale(ModernScrollThumbRenderer.MinimumLength, DeviceDpi),
                trackHeight * VisibleRows / ItemCount);
            var travel = Math.Max(0, trackHeight - thumbHeight);
            var y = inset + travel * _scrollOffset / MaxScrollOffset;
            var x = RightToLeft == RightToLeft.Yes
                ? inset
                : ClientSize.Width - inset - thumbWidth;
            ModernScrollThumbRenderer.Draw(graphics,
                new RectangleF(x, y, thumbWidth, thumbHeight),
                Color.FromArgb(110, ForeColor));
        }
    }
}
