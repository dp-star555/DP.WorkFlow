using System.Drawing;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.UI.Windows.Tests;

public sealed class WorkflowImageViewportTests
{
    [Fact]
    public void FitZoomAndPan_PreserveImageCoordinateMapping()
    {
        using var viewport = new WorkflowImageViewport
        {
            ClientSize = new Size(400, 200),
            Image = new Bitmap(100, 100)
        };

        Assert.Equal(new RectangleF(100, 0, 200, 200), viewport.ImageBounds);
        AssertPoint(new PointF(50, 50), viewport.ClientToImage(new PointF(200, 100)));

        viewport.ZoomAt(2, new PointF(200, 100));

        Assert.Equal(2, viewport.ZoomFactor, 3);
        Assert.Equal(new RectangleF(0, -100, 400, 400), viewport.ImageBounds);
        AssertPoint(new PointF(50, 50), viewport.ClientToImage(new PointF(200, 100)));

        viewport.PanBy(20, 10);

        Assert.Equal(new RectangleF(20, -90, 400, 400), viewport.ImageBounds);
        AssertPoint(new PointF(45, 47.5f), viewport.ClientToImage(new PointF(200, 100)));
    }

    [Fact]
    public void ReplacingSameSizeFrame_KeepsViewWhileNewSizeResetsToFit()
    {
        using var viewport = new WorkflowImageViewport
        {
            ClientSize = new Size(400, 200),
            Image = new Bitmap(100, 100)
        };
        viewport.ZoomAt(2, new PointF(200, 100));
        viewport.PanBy(20, 10);
        var old = viewport.Image;

        viewport.Image = new Bitmap(100, 100);
        old.Dispose();

        Assert.Equal(2, viewport.ZoomFactor, 3);
        Assert.Equal(new RectangleF(20, -90, 400, 400), viewport.ImageBounds);
        old = viewport.Image;

        viewport.Image = new Bitmap(200, 100);
        old.Dispose();

        Assert.Equal(1, viewport.ZoomFactor, 3);
        Assert.Equal(new RectangleF(0, 0, 400, 200), viewport.ImageBounds);
    }

    private static void AssertPoint(PointF expected, PointF actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }
}
