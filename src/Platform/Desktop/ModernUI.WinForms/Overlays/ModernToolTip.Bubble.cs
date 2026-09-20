using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

public sealed partial class ModernToolTip
{
    private sealed class BubbleForm : ModernOwnedOverlayForm
    {
        private readonly string _text;
        private readonly int _radius;
        private readonly int _arrow;
        private readonly Color _borderColor;
        private readonly ModernToolTipPlacement _preferredPlacement;
        private readonly float _dpiScale;
        private readonly int _maximumWidth;
        private readonly Font _ownedFont;
        private ModernToolTipPlacement _placement;
        private Rectangle _body;
        private int _arrowCenter;

        public BubbleForm(string text, Font font, Color back, Color fore, Color border, int maximumWidth,
            int radius, int arrow, ModernToolTipPlacement placement, float dpiScale)
        {
            _text = text;
            _radius = radius;
            _arrow = arrow;
            _borderColor = border;
            _preferredPlacement = placement;
            _placement = placement;
            _dpiScale = dpiScale;
            _maximumWidth = maximumWidth;
            _ownedFont = new Font(font.FontFamily, font.SizeInPoints, font.Style, GraphicsUnit.Point);
            Font = _ownedFont;
            BackColor = back;
            ForeColor = fore;
            RebuildTextFrame();
            _arrowCenter = placement is ModernToolTipPlacement.Top or ModernToolTipPlacement.Bottom ? Width / 2 : Height / 2;
            // Top-level alpha animation makes DWM compose the owner and tooltip in separate
            // intermediate frames. A tooltip appears as one complete no-activate frame.
            Opacity = 1;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void PrepareHiddenFrame(Control anchor)
        {
            _ = Handle;
            Font = _ownedFont;
            RebuildTextFrame();
            Place(anchor);
            UpdateShape();
        }

        public void CommitFinalFrame(Control anchor)
        {
            Font = _ownedFont;
            RebuildTextFrame();
            Place(anchor);
            UpdateShape();
            Invalidate();
        }

        private void RebuildTextFrame()
        {
            var dpi = Math.Max(96, (int)Math.Round(96 * _dpiScale));
            Size measured;
            if (IsHandleCreated)
            {
                using var graphics = CreateGraphics();
                measured = ModernTextLayout.Measure(graphics, _text, _ownedFont,
                    new Size(_maximumWidth, int.MaxValue), wrap: true);
            }
            else
            {
                measured = ModernTextLayout.MeasurePhysical(_text, _ownedFont, dpi,
                    new Size(_maximumWidth, int.MaxValue), wrap: true);
            }
            // TextRenderer may need one extra CJK cell beyond its NoPadding measurement when
            // WordBreak decides whether the final ideograph stays on the first line. Keep that
            // safety outside the configured text maximum, in the bubble chrome.
            var bodyWidth = Math.Max(Scale(72), measured.Width + Scale(30) + ModernTextLayout.TerminalGlyphSafety(dpi));
            var bodyHeight = Math.Max(Scale(32), measured.Height + Scale(18));
            Size = _preferredPlacement is ModernToolTipPlacement.Top or ModernToolTipPlacement.Bottom
                ? new Size(bodyWidth, bodyHeight + _arrow)
                : new Size(bodyWidth + _arrow, bodyHeight);
        }

        public void Place(Control anchor)
        {
            _placement = _preferredPlacement;
            var target = anchor.RectangleToScreen(anchor.ClientRectangle);
            var working = Screen.FromControl(anchor).WorkingArea;
            if (_placement == ModernToolTipPlacement.Top && target.Top - Height < working.Top) _placement = ModernToolTipPlacement.Bottom;
            else if (_placement == ModernToolTipPlacement.Bottom && target.Bottom + Height > working.Bottom) _placement = ModernToolTipPlacement.Top;
            else if (_placement == ModernToolTipPlacement.Left && target.Left - Width < working.Left) _placement = ModernToolTipPlacement.Right;
            else if (_placement == ModernToolTipPlacement.Right && target.Right + Width > working.Right) _placement = ModernToolTipPlacement.Left;

            var point = _placement switch
            {
                ModernToolTipPlacement.Top => new Point(target.Left + (target.Width - Width) / 2, target.Top - Height - Scale(2)),
                ModernToolTipPlacement.Bottom => new Point(target.Left + (target.Width - Width) / 2, target.Bottom + Scale(2)),
                ModernToolTipPlacement.Left => new Point(target.Left - Width - Scale(2), target.Top + (target.Height - Height) / 2),
                _ => new Point(target.Right + Scale(2), target.Top + (target.Height - Height) / 2)
            };
            Location = new Point(
                ModernCompatibility.Clamp(point.X, working.Left, Math.Max(working.Left, working.Right - Width)),
                ModernCompatibility.Clamp(point.Y, working.Top, Math.Max(working.Top, working.Bottom - Height)));
            _arrowCenter = _placement is ModernToolTipPlacement.Top or ModernToolTipPlacement.Bottom
                ? ModernCompatibility.Clamp(target.Left + target.Width / 2 - Left, _radius + _arrow + 2, Width - _radius - _arrow - 2)
                : ModernCompatibility.Clamp(target.Top + target.Height / 2 - Top, _radius + _arrow + 2, Height - _radius - _arrow - 2);
            UpdateShape();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Opacity = 1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateShape();
            using var brush = new SolidBrush(BackColor);
            using var pen = new Pen(_borderColor, 1) { Alignment = PenAlignment.Inset };
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            var textBounds = Rectangle.Inflate(_body, -Scale(12), -Scale(9));
            ModernTextLayout.Draw(e.Graphics, _text, Font, textBounds, ForeColor,
                wrap: true, verticalCenter: true);
        }

        private int Scale(int logicalPixels) => (int)Math.Round(logicalPixels * _dpiScale);

        private void UpdateShape()
        {
            using var path = CreateShape();
            var old = Region;
            Region = new Region(path);
            old?.Dispose();
        }

        private GraphicsPath CreateShape()
        {
            var edge = Math.Max(1, Scale(1));
            _body = _placement switch
            {
                ModernToolTipPlacement.Top => new Rectangle(edge, edge, Width - edge * 2, Height - _arrow - edge),
                ModernToolTipPlacement.Bottom => new Rectangle(edge, _arrow, Width - edge * 2, Height - _arrow - edge),
                ModernToolTipPlacement.Left => new Rectangle(edge, edge, Width - _arrow - edge, Height - edge * 2),
                _ => new Rectangle(_arrow, edge, Width - _arrow - edge, Height - edge * 2)
            };
            return CreateIntegratedOutline(_body, _radius, _arrow, _arrowCenter, _placement);
        }

        private static GraphicsPath CreateIntegratedOutline(Rectangle body, int radius, int arrow, int arrowCenter,
            ModernToolTipPlacement placement)
        {
            var path = new GraphicsPath();
            var r = Math.Max(1, Math.Min(radius, Math.Min(body.Width, body.Height) / 2));
            var d = r * 2;
            var left = body.Left;
            var top = body.Top;
            var right = body.Right - 1;
            var bottom = body.Bottom - 1;

            if (placement == ModernToolTipPlacement.Top)
            {
                path.StartFigure();
                path.AddLine(left + r, top, right - r, top);
                path.AddArc(right - d, top, d, d, 270, 90);
                path.AddLine(right, top + r, right, bottom - r);
                path.AddArc(right - d, bottom - d, d, d, 0, 90);
                path.AddLine(right - r, bottom, arrowCenter + arrow, bottom);
                path.AddLine(arrowCenter + arrow, bottom, arrowCenter, bottom + arrow);
                path.AddLine(arrowCenter, bottom + arrow, arrowCenter - arrow, bottom);
                path.AddLine(arrowCenter - arrow, bottom, left + r, bottom);
                path.AddArc(left, bottom - d, d, d, 90, 90);
                path.AddLine(left, bottom - r, left, top + r);
                path.AddArc(left, top, d, d, 180, 90);
            }
            else if (placement == ModernToolTipPlacement.Bottom)
            {
                path.StartFigure();
                path.AddLine(left + r, top, arrowCenter - arrow, top);
                path.AddLine(arrowCenter - arrow, top, arrowCenter, top - arrow);
                path.AddLine(arrowCenter, top - arrow, arrowCenter + arrow, top);
                path.AddLine(arrowCenter + arrow, top, right - r, top);
                path.AddArc(right - d, top, d, d, 270, 90);
                path.AddLine(right, top + r, right, bottom - r);
                path.AddArc(right - d, bottom - d, d, d, 0, 90);
                path.AddLine(right - r, bottom, left + r, bottom);
                path.AddArc(left, bottom - d, d, d, 90, 90);
                path.AddLine(left, bottom - r, left, top + r);
                path.AddArc(left, top, d, d, 180, 90);
            }
            else if (placement == ModernToolTipPlacement.Left)
            {
                path.StartFigure();
                path.AddLine(left + r, top, right - r, top);
                path.AddArc(right - d, top, d, d, 270, 90);
                path.AddLine(right, top + r, right, arrowCenter - arrow);
                path.AddLine(right, arrowCenter - arrow, right + arrow, arrowCenter);
                path.AddLine(right + arrow, arrowCenter, right, arrowCenter + arrow);
                path.AddLine(right, arrowCenter + arrow, right, bottom - r);
                path.AddArc(right - d, bottom - d, d, d, 0, 90);
                path.AddLine(right - r, bottom, left + r, bottom);
                path.AddArc(left, bottom - d, d, d, 90, 90);
                path.AddLine(left, bottom - r, left, top + r);
                path.AddArc(left, top, d, d, 180, 90);
            }
            else
            {
                path.StartFigure();
                path.AddLine(left + r, top, right - r, top);
                path.AddArc(right - d, top, d, d, 270, 90);
                path.AddLine(right, top + r, right, bottom - r);
                path.AddArc(right - d, bottom - d, d, d, 0, 90);
                path.AddLine(right - r, bottom, left + r, bottom);
                path.AddArc(left, bottom - d, d, d, 90, 90);
                path.AddLine(left, bottom - r, left, arrowCenter + arrow);
                path.AddLine(left, arrowCenter + arrow, left - arrow, arrowCenter);
                path.AddLine(left - arrow, arrowCenter, left, arrowCenter - arrow);
                path.AddLine(left, arrowCenter - arrow, left, top + r);
                path.AddArc(left, top, d, d, 180, 90);
            }
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _ownedFont.Dispose();
        }
    }
}
