namespace ModernUI.WinForms;

public sealed partial class ModernComboBox
{
    private sealed class ComboDropDownSurface : Control
    {
        private int _hoveredIndex = -1;
        private int _scrollOffset;
        private int _itemCount;

        public ComboDropDownSurface()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int ItemHeight { get; set; } = 30;
        public int VisibleRows { get; set; } = 8;
        public int InitialSelectedIndex { get; set; } = -1;
        public int ItemCount
        {
            get => _itemCount;
            set
            {
                _itemCount = Math.Max(0, value);
                if (_hoveredIndex >= _itemCount) _hoveredIndex = -1;
                _scrollOffset = ModernCompatibility.Clamp(_scrollOffset, 0, MaxScrollOffset);
                Invalidate();
            }
        }

        private int MaxScrollOffset => Math.Max(0, ItemCount - Math.Max(1, VisibleRows));

        public event Action<Graphics, int, Rectangle, bool>? PaintItem;
        public event Action<int>? ItemClicked;
        public event EventHandler? CloseRequested;

        public void ResetHover()
        {
            _hoveredIndex = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var last = Math.Min(ItemCount, _scrollOffset + Math.Max(1, VisibleRows) + 1);
            for (var index = _scrollOffset; index < last; index++)
                PaintItem?.Invoke(e.Graphics, index, GetItemRectangle(index), index == _hoveredIndex);
            PaintScrollIndicator(e.Graphics);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
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
            if (index >= 0) ItemClicked?.Invoke(index);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * Math.Max(1, SystemInformation.MouseWheelScrollLines));
        }

        public bool HandleKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }
            if (DropDownKeyboardNavigation.TryMove(keyData, _hoveredIndex, InitialSelectedIndex,
                    ItemCount, VisibleRows, out var target))
            {
                SetHoveredIndex(target);
                EnsureVisible(target);
                return true;
            }
            if (keyData is Keys.Enter or Keys.Space && _hoveredIndex >= 0)
            {
                ItemClicked?.Invoke(_hoveredIndex);
                return true;
            }
            return false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
            HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

        private void EnsureVisible(int index)
        {
            if (index < _scrollOffset) _scrollOffset = index;
            else if (index >= _scrollOffset + VisibleRows) _scrollOffset = index - VisibleRows + 1;
            Invalidate();
        }

        private void ScrollBy(int delta)
        {
            var next = ModernCompatibility.Clamp(_scrollOffset + delta, 0, MaxScrollOffset);
            if (next == _scrollOffset) return;
            _scrollOffset = next;
            SetHoveredIndex(IndexFromPoint(PointToClient(Cursor.Position)));
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
            if (previous >= 0) Invalidate(GetItemRectangle(previous));
            if (index >= 0) Invalidate(GetItemRectangle(index));
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
            var x = RightToLeft == RightToLeft.Yes ? inset : ClientSize.Width - inset - thumbWidth;
            ModernScrollThumbRenderer.Draw(graphics, new RectangleF(x, y, thumbWidth, thumbHeight),
                Color.FromArgb(110, ForeColor));
        }
    }

    private void ShowManagedDropDown()
    {
        if (!Enabled || _dropDown.Visible || Items.Count == 0) return;
        _comboBox.Focus();
        var rows = ModernCompatibility.Clamp(Items.Count, 1, Math.Max(1, MaxDropDownItems));
        _dropDownSurface.ItemHeight = Math.Max(ScaleLogical(30), ScaleLogical(Theme.ControlHeight));
        _dropDownSurface.ItemCount = Items.Count;
        _dropDownSurface.VisibleRows = rows;
        _dropDownSurface.InitialSelectedIndex = SelectedIndex;
        _dropDownSurface.RightToLeft = RightToLeft;
        _dropDown.Show(this,
            new Size(ResolveManagedDropDownWidth(), rows * _dropDownSurface.ItemHeight + ScaleLogical(8)),
            ScaleLogical(2), Math.Max(0, Theme.AnimationDuration));
        DropDown?.Invoke(this, EventArgs.Empty);
        _arrow.Expanded = true;
        InvalidateChrome();
    }

    private void CloseManagedDropDown() => _dropDown.Close();

    private int ResolveManagedDropDownWidth() =>
        _dropDownWidth > 0 ? ScaleLogical(_dropDownWidth) : Math.Max(1, Width - Padding.Horizontal);

    private void CommitManagedSelection(int index)
    {
        if ((uint)index >= (uint)Items.Count) return;
        if (!ReadOnly)
        {
            _comboBox.SelectedIndex = index;
            _userEditPending = false;
            SelectionChangeCommitted?.Invoke(this, EventArgs.Empty);
        }
        CloseManagedDropDown();
    }

    private void DrawManagedDropDownItem(Graphics graphics, int index, Rectangle bounds, bool hovered)
    {
        if ((uint)index >= (uint)Items.Count) return;
        using (var surface = new SolidBrush(Theme.Elevated)) graphics.FillRectangle(surface, bounds);
        var selected = index == SelectedIndex;
        var highlight = selected ? Theme.PrimaryBackground : hovered ? Theme.ControlHover : Color.Empty;
        if (!highlight.IsEmpty)
        {
            var highlightBounds = RectangleF.Inflate(bounds, -ScaleLogical(4), -ScaleLogical(2));
            using var path = Geometry.CreateRoundedRectangle(highlightBounds,
                Math.Max(3, ScaleLogical(Theme.Radius) - 2));
            using var brush = new SolidBrush(highlight);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.FillPath(brush, path);
        }

        var item = Items[index];
        var rightToLeft = RightToLeft == RightToLeft.Yes;
        var textLeft = bounds.Left + ScaleLogical(12);
        var textRight = bounds.Right - ScaleLogical(12);
        if (_itemImages.Resolve(item) is { } image)
        {
            var iconSize = ScaleLogical(18);
            var imageLeft = rightToLeft ? textRight - iconSize : textLeft;
            graphics.DrawImage(image, new Rectangle(imageLeft,
                bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize));
            if (rightToLeft) textRight = imageLeft - ScaleLogical(8);
            else textLeft += iconSize + ScaleLogical(8);
        }
        TextRenderer.DrawText(graphics, GetItemText(item), Font,
            new Rectangle(textLeft, bounds.Top, Math.Max(0, textRight - textLeft), bounds.Height),
            selected ? Theme.Primary : Theme.Text,
            (rightToLeft ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
