using System.Collections;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace ModernUI.WinForms;

public sealed partial class ModernComboBox
{
    private sealed class ComboBottomChromeSurface : Control
    {
        private readonly ModernComboBox _owner;

        public ComboBottomChromeSurface(ModernComboBox owner)
        {
            _owner = owner;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _owner.InnerComboBox.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var inset = _owner.ScaleLogical(1f);
            var active = _owner.ContainsFocus || _owner.DroppedDown;
            var fullBounds = new RectangleF(inset, inset - Top,
                Math.Max(0, _owner.Width - inset * 2), Math.Max(0, _owner.Height - inset * 2));
            using var canvas = new GdiCanvas(e.Graphics);
            var border = _owner.ResolveBorderColor();
            var strokeWidth = active || ModernValidation.IsEmphasized(_owner.ValidationState) ? _owner.ScaleLogical(1.5f) : _owner.ScaleLogical(1f);
            // A transparent child only asks the parent's background to paint; ModernComboBox draws
            // its white/dark control surface in OnPaint, not OnPaintBackground. Fill the clipped
            // lower rounded interior here before repainting the border, otherwise the host band's
            // background leaks through as a differently colored strip.
            var radius = _owner.ScaleLogical(_owner.Theme.Radius);
            canvas.Fill(_owner.Theme.Control, fullBounds, radius);
            canvas.Draw(border, strokeWidth, fullBounds, radius, centerStroke: true);
        }
    }

    private sealed class ComboSelectedImageSurface : Control
    {
        private ModernTheme _theme = ModernUiSettings.DefaultTheme;
        private Image? _image;

        public ComboSelectedImageSurface()
        {
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public ModernTheme Theme { get => _theme; set { _theme = value; Invalidate(); } }
        public Image? Image { get => _image; set { if (ReferenceEquals(_image, value)) return; _image = value; Invalidate(); } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Parent is ModernComboBox owner) owner.InnerComboBox.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Control);
            if (Image is null) return;
            var size = Math.Min(ModernDpi.ScaleToInt(16, DeviceDpi), Math.Min(Width, Height));
            e.Graphics.DrawImage(Image, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
        }
    }

    private sealed class DisabledComboSurface : Control
    {
        private ModernTheme _theme = ModernUiSettings.DefaultTheme;
        private Image? _image;

        public DisabledComboSurface()
        {
            Visible = false;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public ModernTheme Theme
        {
            get => _theme;
            set { _theme = value; Invalidate(); }
        }

        public Image? Image
        {
            get => _image;
            set { if (ReferenceEquals(_image, value)) return; _image = value; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Control);
            var iconSize = ModernDpi.ScaleToInt(11, DeviceDpi);
            var arrowWidth = ModernDpi.ScaleToInt(24, DeviceDpi);
            var textLeft = ModernDpi.ScaleToInt(6, DeviceDpi);
            if (Image is not null)
            {
                var itemIconSize = ModernDpi.ScaleToInt(16, DeviceDpi);
                e.Graphics.DrawImage(Image, new Rectangle(textLeft, (Height - itemIconSize) / 2,
                    itemIconSize, itemIconSize));
                textLeft += itemIconSize + ModernDpi.ScaleToInt(6, DeviceDpi);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(textLeft, 0,
                    Math.Max(0, Width - arrowWidth - textLeft - ModernDpi.ScaleToInt(8, DeviceDpi)), Height),
                Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var iconBounds = new RectangleF(Width - arrowWidth + (arrowWidth - iconSize) / 2f,
                CenteredChevronTop(Height, iconSize, false), iconSize, iconSize);
            using var canvas = new GdiCanvas(e.Graphics);
            canvas.DrawIcon(ModernIconKind.ChevronDown, Theme.TextDisabled, iconBounds,
                Math.Max(1f, DeviceDpi / 96f * 1.5f));
        }
    }

    private sealed class ComboArrowSurface : Control
    {
        private ModernTheme _theme = ModernUiSettings.DefaultTheme;
        private bool _expanded;

        public ComboArrowSurface()
        {
            TabStop = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public ModernTheme Theme
        {
            get => _theme;
            set { _theme = value; Invalidate(); }
        }

        public bool Expanded
        {
            get => _expanded;
            set { if (_expanded == value) return; _expanded = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var background = Enabled ? Theme.Control : Theme.BorderSecondary;
            e.Graphics.Clear(background);
            var iconSize = ModernDpi.ScaleToInt(11, DeviceDpi);
            var iconBounds = new RectangleF((Width - iconSize) / 2f,
                CenteredChevronTop(Height, iconSize, Expanded), iconSize, iconSize);
            using var canvas = new GdiCanvas(e.Graphics);
            canvas.DrawIcon(Expanded ? ModernIconKind.ChevronUp : ModernIconKind.ChevronDown,
                Enabled ? Theme.TextSecondary : Theme.TextDisabled, iconBounds, Math.Max(1f, DeviceDpi / 96f * 1.5f));
        }
    }
}
