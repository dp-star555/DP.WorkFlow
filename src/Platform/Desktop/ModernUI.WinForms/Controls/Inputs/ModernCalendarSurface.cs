using System.Globalization;

namespace ModernUI.WinForms;

/// <summary>日期选择器使用的托管日历画布，集中处理布局、禁用日期和键盘导航。</summary>
internal sealed class ModernCalendarSurface : Control
{
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _selectedDate = DateTime.Today;
    private DateTime? _rangeStart;
    private DateTime? _rangeEnd;
    private DateTime? _hoveredDate;
    private float _hoverProgress;
    private IDisposable? _hoverAnimation;

    public ModernCalendarSurface()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(300, 286);
        AccessibleRole = AccessibleRole.Table;
    }

    public ModernTheme Theme { get; set; } = ModernTheme.Light;
    public CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("zh-CN");
    public string SelectDateAction { get; set; } = "Select date";
    public DateTime MinimumDate { get; set; } = DateTimePicker.MinimumDateTime.Date;
    public DateTime MaximumDate { get; set; } = DateTimePicker.MaximumDateTime.Date;
    public Func<DateTime, bool>? DateEnabledPredicate { get; set; }
    public DateTime SelectedDate
    {
        get => _selectedDate;
        set => SynchronizeSelection(value, _rangeStart, _rangeEnd);
    }

    internal void SynchronizeSelection(DateTime selectedDate, DateTime? rangeStart, DateTime? rangeEnd)
    {
        _selectedDate = selectedDate.Date;
        _displayMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
        _rangeStart = rangeStart?.Date;
        _rangeEnd = rangeEnd?.Date;
        if (_rangeStart is { } start && _rangeEnd is { } end && start > end)
            (_rangeStart, _rangeEnd) = (end, start);
        AccessibleName = selectedDate.ToLongDateString();
        Invalidate();
    }

    public event Action<DateTime>? DateSelected;
    public event EventHandler? CloseRequested;

    protected override AccessibleObject CreateAccessibilityInstance() => new CalendarAccessibleObject(this);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Elevated);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var headerHeight = ScaleLogical(44);
        var headerSide = ScaleLogical(44);
        TextRenderer.DrawText(e.Graphics, _displayMonth.ToString("Y", Culture), Font,
            new Rectangle(headerSide, 0, Width - headerSide * 2, headerHeight), Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var rtl = RightToLeft == RightToLeft.Yes;
        DrawChevron(e.Graphics, new Rectangle(ScaleLogical(10), ScaleLogical(10), ScaleLogical(28), ScaleLogical(26)), rtl);
        DrawChevron(e.Graphics, new Rectangle(Width - ScaleLogical(38), ScaleLogical(10), ScaleLogical(28), ScaleLogical(26)), !rtl);
        var gridTop = headerHeight;
        var cellWidth = Width / 7f;
        var cellHeight = (Height - gridTop) / 7f;
        var weekdays = Culture.DateTimeFormat.AbbreviatedDayNames;
        var firstDay = (int)Culture.DateTimeFormat.FirstDayOfWeek;
        for (var column = 0; column < 7; column++)
            TextRenderer.DrawText(e.Graphics, weekdays[(firstDay + column) % 7], Font,
                Rectangle.Round(new RectangleF(column * cellWidth, gridTop, cellWidth, cellHeight)), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var firstOffset = ((int)_displayMonth.DayOfWeek - firstDay + 7) % 7;
        var days = DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month);
        for (var day = 1; day <= days; day++)
        {
            var slot = firstOffset + day - 1;
            var row = slot / 7 + 1;
            var column = slot % 7;
            var bounds = RectangleF.Inflate(new RectangleF(column * cellWidth, gridTop + row * cellHeight, cellWidth, cellHeight),
                -ScaleLogical(4), -ScaleLogical(3));
            var date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
            var enabled = IsDateEnabled(date);
            var circle = CenterSquare(bounds);
            var inRange = IsInCommittedRange(date);
            var rangeEndpoint = IsCommittedRangeEndpoint(date);
            if (inRange && _rangeStart != _rangeEnd)
            {
                var rangeBounds = new RectangleF(column * cellWidth, bounds.Top, cellWidth, bounds.Height);
                if (date == _rangeStart)
                {
                    rangeBounds.X += rangeBounds.Width / 2f;
                    rangeBounds.Width /= 2f;
                }
                else if (date == _rangeEnd)
                {
                    rangeBounds.Width /= 2f;
                }
                using var rangeBrush = new SolidBrush(Theme.PrimaryBackground);
                e.Graphics.FillRectangle(rangeBrush, rangeBounds);
            }
            if (rangeEndpoint || date == SelectedDate)
            {
                using var brush = new SolidBrush(Theme.Primary);
                e.Graphics.FillEllipse(brush, circle);
            }
            else if (enabled && date == _hoveredDate && _hoverProgress > 0)
            {
                var hoverColor = Color.FromArgb((int)Math.Round(255 * _hoverProgress), Theme.ControlHover);
                using var hoverBrush = new SolidBrush(hoverColor);
                e.Graphics.FillEllipse(hoverBrush, circle);
            }
            var isToday = date == DateTime.Today;
            TextRenderer.DrawText(e.Graphics, day.ToString(), Font, Rectangle.Round(bounds),
                enabled ? rangeEndpoint || date == SelectedDate ? Color.White : isToday ? Theme.Primary : Theme.Text : Theme.TextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (isToday && date != SelectedDate && !rangeEndpoint)
            {
                var dotSize = Math.Max(2, ScaleLogical(2));
                using var dot = new SolidBrush(Theme.Primary);
                e.Graphics.FillEllipse(dot, bounds.Left + (bounds.Width - dotSize) / 2f,
                    bounds.Bottom - dotSize - ScaleLogical(1), dotSize, dotSize);
            }
        }
    }

    protected override void WndProc(ref Message message)
    {
        const int mouseMove = 0x0200;
        if (message.Msg == mouseMove)
        {
            var packed = unchecked((long)message.LParam);
            var point = new Point(unchecked((short)(packed & 0xffff)),
                unchecked((short)((packed >> 16) & 0xffff)));
            UpdateHoveredDate(point);
        }
        base.WndProc(ref message);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        UpdateHoveredDate(PointToClient(Cursor.Position));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHoveredDate(e.Location);
    }

    private void UpdateHoveredDate(Point location)
    {
        var next = TryDateFromPoint(location, out var date) && IsDateEnabled(date) ? date : (DateTime?)null;
        if (next == _hoveredDate) return;
        _hoverAnimation?.Dispose();
        var changedDate = next.HasValue && _hoveredDate != next;
        _hoveredDate = next;
        var from = changedDate ? 0f : _hoverProgress;
        _hoverProgress = from;
        var target = next.HasValue ? 1f : 0f;
        if (!ModernUiSettings.EffectiveAnimationsEnabled)
        {
            _hoverProgress = target;
            Invalidate();
            return;
        }
        _hoverAnimation = ModernAnimation.Start(this, from, target, Theme.AnimationDuration,
            value => { _hoverProgress = value; Invalidate(); });
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_hoveredDate.HasValue && _hoverProgress <= 0) return;
        _hoverAnimation?.Dispose();
        _hoveredDate = null;
        _hoverAnimation = ModernAnimation.Start(this, _hoverProgress, 0, Theme.AnimationDuration,
            value => { _hoverProgress = value; Invalidate(); });
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var headerHeight = ScaleLogical(44);
        var navigationHitWidth = ScaleLogical(46);
        if (e.Y < headerHeight)
        {
            var rtl = RightToLeft == RightToLeft.Yes;
            if (e.X < navigationHitWidth) ChangeMonth(rtl ? 1 : -1);
            else if (e.X > Width - navigationHitWidth) ChangeMonth(rtl ? -1 : 1);
            return;
        }
        if (TryDateFromPoint(e.Location, out var date) && IsDateEnabled(date)) SelectDate(date);
    }

    public bool HandleKey(Keys keyData)
    {
        if (keyData == Keys.Escape) { CloseRequested?.Invoke(this, EventArgs.Empty); return true; }
        var next = keyData switch
        {
            Keys.Left => SelectedDate.AddDays(-1),
            Keys.Right => SelectedDate.AddDays(1),
            Keys.Up => SelectedDate.AddDays(-7),
            Keys.Down => SelectedDate.AddDays(7),
            Keys.PageUp => SelectedDate.AddMonths(-1),
            Keys.PageDown => SelectedDate.AddMonths(1),
            Keys.Home => new DateTime(SelectedDate.Year, SelectedDate.Month, 1),
            Keys.End => new DateTime(SelectedDate.Year, SelectedDate.Month, DateTime.DaysInMonth(SelectedDate.Year, SelectedDate.Month)),
            _ => DateTime.MinValue
        };
        if (keyData == Keys.Enter && IsDateEnabled(SelectedDate)) { SelectDate(SelectedDate); return true; }
        if (next == DateTime.MinValue) return false;
        next = next < MinimumDate ? MinimumDate : next > MaximumDate ? MaximumDate : next;
        for (var attempt = 0; attempt < 366 && !IsDateEnabled(next); attempt++)
            next = keyData is Keys.Left or Keys.Up or Keys.PageUp ? next.AddDays(-1) : next.AddDays(1);
        if (IsDateEnabled(next)) SelectedDate = next;
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) =>
        HandleKey(keyData) || base.ProcessCmdKey(ref msg, keyData);

    private bool TryDateFromPoint(Point point, out DateTime date)
    {
        date = default;
        var cellWidth = Width / 7f;
        var headerHeight = ScaleLogical(44);
        var cellHeight = (Height - headerHeight) / 7f;
        var row = (int)((point.Y - headerHeight) / cellHeight) - 1;
        if (row < 0) return false;
        var column = ModernCompatibility.Clamp((int)(point.X / cellWidth), 0, 6);
        var firstOffset = ((int)_displayMonth.DayOfWeek - (int)Culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var day = row * 7 + column - firstOffset + 1;
        if (day < 1 || day > DateTime.DaysInMonth(_displayMonth.Year, _displayMonth.Month)) return false;
        date = new DateTime(_displayMonth.Year, _displayMonth.Month, day);
        return true;
    }
    private Rectangle GetDateBounds(DateTime date)
    {
        if (date.Year != _displayMonth.Year || date.Month != _displayMonth.Month) return Rectangle.Empty;
        var cellWidth = Width / 7f;
        var headerHeight = ScaleLogical(44);
        var cellHeight = (Height - headerHeight) / 7f;
        var firstOffset = ((int)_displayMonth.DayOfWeek - (int)Culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var slot = firstOffset + date.Day - 1;
        return Rectangle.Round(new RectangleF((slot % 7) * cellWidth,
            headerHeight + (slot / 7 + 1) * cellHeight, cellWidth, cellHeight));
    }

    private DateTime DisplayMonth => _displayMonth;

    private sealed class CalendarAccessibleObject(ModernCalendarSurface owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.Table;
        public override int GetChildCount()
        {
            var month = owner.DisplayMonth;
            return 7 + DateTime.DaysInMonth(month.Year, month.Month);
        }
        public override AccessibleObject? GetChild(int index)
        {
            if (index < 0 || index >= GetChildCount()) return null;
            if (index < 7) return new CalendarHeaderAccessibleObject(owner, this, index);
            var month = owner.DisplayMonth;
            return new CalendarDateAccessibleObject(owner, this,
                new DateTime(month.Year, month.Month, index - 6));
        }
    }

    private sealed class CalendarHeaderAccessibleObject(
        ModernCalendarSurface owner, AccessibleObject parent, int column) : AccessibleObject
    {
        public override string? Name
        {
            get
            {
                var firstDay = (int)owner.Culture.DateTimeFormat.FirstDayOfWeek;
                return owner.Culture.DateTimeFormat.DayNames[(firstDay + column) % 7];
            }
        }
        public override AccessibleObject? Parent => parent;
        public override AccessibleRole Role => AccessibleRole.ColumnHeader;
        public override Rectangle Bounds
        {
            get
            {
                var headerHeight = owner.ScaleLogical(44);
                var cellWidth = owner.Width / 7f;
                var cellHeight = (owner.Height - headerHeight) / 7f;
                return owner.RectangleToScreen(Rectangle.Round(new RectangleF(column * cellWidth,
                    headerHeight, cellWidth, cellHeight)));
            }
        }
    }

    private sealed class CalendarDateAccessibleObject(
        ModernCalendarSurface owner, AccessibleObject parent, DateTime date) : AccessibleObject
    {
        public override string? Name => date.ToString("D", owner.Culture);
        public override AccessibleObject? Parent => parent;
        public override AccessibleRole Role => AccessibleRole.Cell;
        public override Rectangle Bounds => owner.RectangleToScreen(owner.GetDateBounds(date));
        public override string? DefaultAction => owner.IsDateEnabled(date) ? owner.SelectDateAction : null;
        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Selectable | AccessibleStates.Focusable;
                if (!owner.IsDateEnabled(date)) state |= AccessibleStates.Unavailable;
                if (date == owner.SelectedDate) state |= AccessibleStates.Selected | AccessibleStates.Focused;
                return state;
            }
        }
        public override AccessibleObject? Navigate(AccessibleNavigation navigationDirection)
        {
            var offset = navigationDirection switch
            {
                AccessibleNavigation.Left => -1,
                AccessibleNavigation.Right => 1,
                AccessibleNavigation.Up => -7,
                AccessibleNavigation.Down => 7,
                AccessibleNavigation.Previous => -1,
                AccessibleNavigation.Next => 1,
                _ => 0
            };
            if (offset == 0) return base.Navigate(navigationDirection);
            var target = date.AddDays(offset);
            return target.Month == owner.DisplayMonth.Month && target.Year == owner.DisplayMonth.Year
                ? new CalendarDateAccessibleObject(owner, parent, target)
                : null;
        }
        public override void DoDefaultAction()
        {
            if (owner.IsDateEnabled(date)) owner.SelectDate(date);
        }
    }

    private int ScaleLogical(int logical) => ModernDpi.ScaleToInt(logical, DeviceDpi);
    private bool IsInCommittedRange(DateTime date) =>
        _rangeStart is { } start && _rangeEnd is { } end && date >= start && date <= end;
    private bool IsCommittedRangeEndpoint(DateTime date) => date == _rangeStart || date == _rangeEnd;
    private bool IsDateEnabled(DateTime date) => date >= MinimumDate && date <= MaximumDate && (DateEnabledPredicate?.Invoke(date) ?? true);
    private void SelectDate(DateTime date) { SelectedDate = date; DateSelected?.Invoke(date); }
    private void ChangeMonth(int offset)
    {
        var month = _displayMonth.AddMonths(offset);
        if (month.AddMonths(1).AddDays(-1) < MinimumDate || month > MaximumDate) return;
        _displayMonth = month;
        Invalidate();
    }
    private void DrawChevron(Graphics graphics, Rectangle bounds, bool right)
    {
        using var pen = new Pen(Theme.TextSecondary, 1.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        var center = new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
        var direction = right ? 1 : -1;
        graphics.DrawLines(pen, [new PointF(center.X - direction * 3, center.Y - 5), new PointF(center.X + direction * 2, center.Y), new PointF(center.X - direction * 3, center.Y + 5)]);
    }
    private static RectangleF CenterSquare(RectangleF bounds)
    {
        var size = Math.Min(bounds.Width, bounds.Height);
        return new RectangleF(bounds.Left + (bounds.Width - size) / 2, bounds.Top + (bounds.Height - size) / 2, size, size);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hoverAnimation?.Dispose();
        base.Dispose(disposing);
    }
}
