using System.Drawing.Drawing2D;

namespace ModernUI.WinForms;

/// <summary>Identifies the vector icons built into ModernUI.</summary>
public enum ModernIconKind
{
    None,
    ChevronDown,
    ChevronUp,
    ChevronLeft,
    ChevronRight,
    DoubleChevronLeft,
    DoubleChevronRight,
    Settings,
    Info,
    Play,
    Key,
    Search,
    Close,
    Plus,
    Minus,
    Eye,
    EyeOff,
    Loading,
    /// <summary>选择/移动指针。</summary>
    Pointer,
    /// <summary>轴对齐矩形。</summary>
    Rectangle,
    /// <summary>旋转矩形。</summary>
    RotatedRectangle,
    /// <summary>椭圆。</summary>
    Ellipse,
    /// <summary>多边形。</summary>
    Polygon,
    /// <summary>画笔（涂抹）。</summary>
    Brush,
    /// <summary>橡皮。</summary>
    Eraser,
    /// <summary>适应窗口（四角向外）。</summary>
    FitWindow,
    /// <summary>撤销（逆时针箭头）。</summary>
    Undo,
    /// <summary>重做（顺时针箭头）。</summary>
    Redo,
    /// <summary>暂停（双竖条）。</summary>
    Pause,
    /// <summary>停止（实心方块）。</summary>
    Stop,
    /// <summary>继续（竖条加三角）。</summary>
    Resume,
    /// <summary>返回上一级（向上折返箭头）。</summary>
    NavigateUp,
    /// <summary>新建文件。</summary>
    FileNew,
    /// <summary>打开文件夹。</summary>
    FolderOpen,
    /// <summary>保存。</summary>
    Save,
    /// <summary>另存为。</summary>
    SaveAs,
    /// <summary>自动布局（层级节点）。</summary>
    AutoLayout,
    /// <summary>左对齐。</summary>
    AlignLeft,
    /// <summary>右对齐。</summary>
    AlignRight,
    /// <summary>顶端对齐。</summary>
    AlignTop,
    /// <summary>底端对齐。</summary>
    AlignBottom,
    /// <summary>水平等距分布。</summary>
    DistributeHorizontal,
    /// <summary>垂直等距分布。</summary>
    DistributeVertical
}

/// <summary>Specifies where a button icon is placed relative to its text.</summary>
public enum ModernIconPlacement
{
    Left,
    Right
}

/// <summary>提供可用于原生 ImageList 等 adapter seam 的矢量图标位图。</summary>
public static class ModernIcons
{
    /// <summary>按指定像素尺寸创建透明背景图标；调用方负责释放返回的位图。</summary>
    public static Bitmap CreateBitmap(ModernIconKind icon, Color color, int pixelSize = 16)
    {
        pixelSize = Math.Max(1, pixelSize);
        var bitmap = new Bitmap(pixelSize, pixelSize, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        ModernIconRenderer.Draw(graphics, icon, color, new RectangleF(0, 0, pixelSize, pixelSize), Math.Max(1f, pixelSize / 10f));
        return bitmap;
    }
}

/// <summary>Draws the framework's DPI-independent vector icon paths.</summary>
internal static class ModernIconRenderer
{
    public static void Draw(Graphics graphics, ModernIconKind icon, Color color, RectangleF bounds, float strokeWidth)
    {
        if (icon == ModernIconKind.None || bounds.Width <= 0 || bounds.Height <= 0) return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, Math.Max(1f, strokeWidth))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        var left = bounds.Left;
        var top = bounds.Top;
        var width = bounds.Width;
        var height = bounds.Height;
        PointF Point(float x, float y) => new(left + width * x, top + height * y);

        switch (icon)
        {
            case ModernIconKind.ChevronDown:
                graphics.DrawLines(pen, [Point(.2f, .36f), Point(.5f, .66f), Point(.8f, .36f)]);
                break;
            case ModernIconKind.ChevronUp:
                graphics.DrawLines(pen, [Point(.2f, .64f), Point(.5f, .34f), Point(.8f, .64f)]);
                break;
            case ModernIconKind.ChevronLeft:
                graphics.DrawLines(pen, [Point(.64f, .2f), Point(.34f, .5f), Point(.64f, .8f)]);
                break;
            case ModernIconKind.ChevronRight:
                graphics.DrawLines(pen, [Point(.36f, .2f), Point(.66f, .5f), Point(.36f, .8f)]);
                break;
            case ModernIconKind.DoubleChevronLeft:
                graphics.DrawLines(pen, [Point(.52f, .2f), Point(.22f, .5f), Point(.52f, .8f)]);
                graphics.DrawLines(pen, [Point(.8f, .2f), Point(.5f, .5f), Point(.8f, .8f)]);
                break;
            case ModernIconKind.DoubleChevronRight:
                graphics.DrawLines(pen, [Point(.2f, .2f), Point(.5f, .5f), Point(.2f, .8f)]);
                graphics.DrawLines(pen, [Point(.48f, .2f), Point(.78f, .5f), Point(.48f, .8f)]);
                break;
            case ModernIconKind.Settings:
                graphics.DrawEllipse(pen, new RectangleF(left + width * .34f, top + height * .34f, width * .32f, height * .32f));
                for (var index = 0; index < 8; index++)
                {
                    var angle = index * ModernCompatibility.PI / 4f;
                    var sin = ModernCompatibility.Sin(angle);
                    var cos = ModernCompatibility.Cos(angle);
                    graphics.DrawLine(pen,
                        new PointF(left + width * (.5f + cos * .31f), top + height * (.5f + sin * .31f)),
                        new PointF(left + width * (.5f + cos * .43f), top + height * (.5f + sin * .43f)));
                }
                break;
            case ModernIconKind.Info:
                graphics.DrawEllipse(pen, new RectangleF(left + width * .12f, top + height * .12f, width * .76f, height * .76f));
                graphics.DrawLine(pen, Point(.5f, .43f), Point(.5f, .72f));
                graphics.DrawEllipse(pen, new RectangleF(left + width * .49f, top + height * .29f, width * .02f, height * .02f));
                break;
            case ModernIconKind.Play:
                using (var brush = new SolidBrush(color))
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon([Point(.3f, .18f), Point(.78f, .5f), Point(.3f, .82f)]);
                    graphics.FillPath(brush, path);
                }
                break;
            case ModernIconKind.Key:
                graphics.DrawEllipse(pen, new RectangleF(left + width * .12f, top + height * .18f, width * .4f, height * .4f));
                graphics.DrawLine(pen, Point(.46f, .52f), Point(.82f, .82f));
                graphics.DrawLine(pen, Point(.65f, .68f), Point(.75f, .58f));
                break;
            case ModernIconKind.Search:
                graphics.DrawEllipse(pen, new RectangleF(left + width * .14f, top + height * .14f, width * .52f, height * .52f));
                graphics.DrawLine(pen, Point(.58f, .58f), Point(.84f, .84f));
                break;
            case ModernIconKind.Close:
                graphics.DrawLine(pen, Point(.24f, .24f), Point(.76f, .76f));
                graphics.DrawLine(pen, Point(.76f, .24f), Point(.24f, .76f));
                break;
            case ModernIconKind.Plus:
                graphics.DrawLine(pen, Point(.5f, .2f), Point(.5f, .8f));
                graphics.DrawLine(pen, Point(.2f, .5f), Point(.8f, .5f));
                break;
            case ModernIconKind.Minus:
                graphics.DrawLine(pen, Point(.2f, .5f), Point(.8f, .5f));
                break;
            case ModernIconKind.Eye:
            case ModernIconKind.EyeOff:
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(Point(.08f, .5f), Point(.28f, .18f), Point(.72f, .18f), Point(.92f, .5f));
                    path.AddBezier(Point(.92f, .5f), Point(.72f, .82f), Point(.28f, .82f), Point(.08f, .5f));
                    graphics.DrawPath(pen, path);
                }
                graphics.DrawEllipse(pen, new RectangleF(left + width * .39f, top + height * .39f, width * .22f, height * .22f));
                if (icon == ModernIconKind.EyeOff) graphics.DrawLine(pen, Point(.16f, .16f), Point(.84f, .84f));
                break;
            case ModernIconKind.Pointer:
                graphics.DrawPolygon(pen, [Point(.26f, .14f), Point(.26f, .8f), Point(.42f, .64f), Point(.54f, .88f), Point(.64f, .83f), Point(.52f, .6f), Point(.74f, .58f)]);
                break;
            case ModernIconKind.Rectangle:
                graphics.DrawRectangle(pen, left + width * .16f, top + height * .26f, width * .68f, height * .48f);
                break;
            case ModernIconKind.RotatedRectangle:
                graphics.DrawPolygon(pen, [Point(.34f, .12f), Point(.9f, .42f), Point(.66f, .88f), Point(.1f, .58f)]);
                break;
            case ModernIconKind.Ellipse:
                graphics.DrawEllipse(pen, new RectangleF(left + width * .12f, top + height * .24f, width * .76f, height * .52f));
                break;
            case ModernIconKind.Polygon:
                graphics.DrawPolygon(pen, [Point(.5f, .12f), Point(.88f, .4f), Point(.74f, .86f), Point(.26f, .86f), Point(.12f, .4f)]);
                break;
            case ModernIconKind.Brush:
                graphics.DrawLine(pen, Point(.86f, .14f), Point(.46f, .54f));
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(Point(.46f, .54f), Point(.3f, .5f), Point(.22f, .66f), Point(.2f, .86f));
                    path.AddBezier(Point(.2f, .86f), Point(.42f, .86f), Point(.56f, .76f), Point(.46f, .54f));
                    graphics.DrawPath(pen, path);
                }
                break;
            case ModernIconKind.Eraser:
                graphics.DrawPolygon(pen, [Point(.12f, .62f), Point(.5f, .24f), Point(.86f, .6f), Point(.6f, .86f), Point(.36f, .86f)]);
                graphics.DrawLine(pen, Point(.31f, .43f), Point(.67f, .79f));
                graphics.DrawLine(pen, Point(.36f, .86f), Point(.88f, .86f));
                break;
            case ModernIconKind.FitWindow:
                graphics.DrawLines(pen, [Point(.14f, .36f), Point(.14f, .14f), Point(.36f, .14f)]);
                graphics.DrawLines(pen, [Point(.64f, .14f), Point(.86f, .14f), Point(.86f, .36f)]);
                graphics.DrawLines(pen, [Point(.86f, .64f), Point(.86f, .86f), Point(.64f, .86f)]);
                graphics.DrawLines(pen, [Point(.36f, .86f), Point(.14f, .86f), Point(.14f, .64f)]);
                break;
            case ModernIconKind.Undo:
            case ModernIconKind.Redo:
            {
                var undo = icon == ModernIconKind.Undo;
                PointF P(float x, float y) => Point(undo ? x : 1 - x, y);
                using var path = new GraphicsPath();
                path.AddLine(P(.22f, .44f), P(.6f, .44f));
                path.AddBezier(P(.6f, .44f), P(.92f, .44f), P(.92f, .82f), P(.6f, .82f));
                path.AddLine(P(.6f, .82f), P(.4f, .82f));
                graphics.DrawPath(pen, path);
                graphics.DrawLines(pen, [P(.38f, .26f), P(.2f, .44f), P(.38f, .62f)]);
                break;
            }
            case ModernIconKind.Pause:
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillRectangle(brush, left + width * .26f, top + height * .2f, width * .16f, height * .6f);
                    graphics.FillRectangle(brush, left + width * .58f, top + height * .2f, width * .16f, height * .6f);
                }
                break;
            case ModernIconKind.Stop:
                using (var brush = new SolidBrush(color))
                using (var path = Geometry.CreateRoundedRectangle(new RectangleF(left + width * .22f, top + height * .22f, width * .56f, height * .56f), width * .08f))
                    graphics.FillPath(brush, path);
                break;
            case ModernIconKind.Resume:
                using (var brush = new SolidBrush(color))
                using (var path = new GraphicsPath())
                {
                    graphics.FillRectangle(brush, left + width * .18f, top + height * .2f, width * .13f, height * .6f);
                    path.AddPolygon([Point(.42f, .2f), Point(.84f, .5f), Point(.42f, .8f)]);
                    graphics.FillPath(brush, path);
                }
                break;
            case ModernIconKind.NavigateUp:
                graphics.DrawLines(pen, [Point(.3f, .38f), Point(.5f, .18f), Point(.7f, .38f)]);
                graphics.DrawLines(pen, [Point(.5f, .18f), Point(.5f, .62f), Point(.5f, .7f)]);
                graphics.DrawLines(pen, [Point(.5f, .7f), Point(.5f, .82f), Point(.82f, .82f)]);
                break;
            case ModernIconKind.FileNew:
                graphics.DrawPolygon(pen, [Point(.22f, .1f), Point(.58f, .1f), Point(.78f, .3f), Point(.78f, .9f), Point(.22f, .9f)]);
                graphics.DrawLines(pen, [Point(.58f, .1f), Point(.58f, .3f), Point(.78f, .3f)]);
                graphics.DrawLine(pen, Point(.5f, .48f), Point(.5f, .74f));
                graphics.DrawLine(pen, Point(.37f, .61f), Point(.63f, .61f));
                break;
            case ModernIconKind.FolderOpen:
                graphics.DrawLines(pen, [Point(.12f, .78f), Point(.12f, .22f), Point(.38f, .22f), Point(.46f, .32f), Point(.8f, .32f), Point(.8f, .44f)]);
                graphics.DrawPolygon(pen, [Point(.12f, .78f), Point(.26f, .44f), Point(.92f, .44f), Point(.78f, .78f)]);
                break;
            case ModernIconKind.Save:
            case ModernIconKind.SaveAs:
                graphics.DrawPolygon(pen, [Point(.14f, .14f), Point(.7f, .14f), Point(.86f, .3f), Point(.86f, .86f), Point(.14f, .86f)]);
                graphics.DrawRectangle(pen, left + width * .32f, top + height * .14f, width * .3f, height * .2f);
                graphics.DrawRectangle(pen, left + width * .3f, top + height * .56f, width * .4f, height * .3f);
                if (icon == ModernIconKind.SaveAs)
                    using (var brush = new SolidBrush(color))
                        graphics.FillEllipse(brush, left + width * .66f, top + height * .66f, width * .3f, height * .3f);
                break;
            case ModernIconKind.AutoLayout:
                graphics.DrawRectangle(pen, left + width * .36f, top + height * .1f, width * .28f, height * .2f);
                graphics.DrawRectangle(pen, left + width * .1f, top + height * .7f, width * .28f, height * .2f);
                graphics.DrawRectangle(pen, left + width * .62f, top + height * .7f, width * .28f, height * .2f);
                graphics.DrawLines(pen, [Point(.5f, .3f), Point(.5f, .5f), Point(.24f, .5f), Point(.24f, .7f)]);
                graphics.DrawLines(pen, [Point(.5f, .5f), Point(.76f, .5f), Point(.76f, .7f)]);
                break;
            case ModernIconKind.AlignLeft:
            case ModernIconKind.AlignRight:
            case ModernIconKind.AlignTop:
            case ModernIconKind.AlignBottom:
            {
                var vertical = icon is ModernIconKind.AlignLeft or ModernIconKind.AlignRight;
                var far = icon is ModernIconKind.AlignRight or ModernIconKind.AlignBottom;
                PointF A(float along, float across) => vertical ? Point(far ? 1 - across : across, along) : Point(along, far ? 1 - across : across);
                graphics.DrawLine(pen, A(.1f, .14f), A(.9f, .14f));
                using var brush = new SolidBrush(color);
                foreach (var (start, length) in new[] { (.22f, .6f), (.58f, .36f) })
                {
                    var a = A(start, .26f);
                    var b = A(start + .2f, .26f + length);
                    graphics.FillRectangle(brush, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                }
                break;
            }
            case ModernIconKind.DistributeHorizontal:
            case ModernIconKind.DistributeVertical:
            {
                var horizontal = icon == ModernIconKind.DistributeHorizontal;
                PointF D(float along, float across) => horizontal ? Point(along, across) : Point(across, along);
                graphics.DrawLine(pen, D(.1f, .12f), D(.1f, .88f));
                graphics.DrawLine(pen, D(.9f, .12f), D(.9f, .88f));
                using var brush = new SolidBrush(color);
                var a = D(.38f, .26f);
                var b = D(.62f, .74f);
                graphics.FillRectangle(brush, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
                break;
            }
            case ModernIconKind.Loading:
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawArc(pen, RectangleF.Inflate(bounds, -strokeWidth, -strokeWidth), -75, 285);
                break;
        }
    }
}

/// <summary>Provides shared DPI conversion for custom drawing and top-level popup windows.</summary>
internal static class ModernDpi
{
    public static float Scale(int logicalPixels, int deviceDpi) => logicalPixels * Math.Max(1, deviceDpi) / 96f;
    public static int ScaleToInt(int logicalPixels, int deviceDpi) => (int)Math.Round(Scale(logicalPixels, deviceDpi));
}
