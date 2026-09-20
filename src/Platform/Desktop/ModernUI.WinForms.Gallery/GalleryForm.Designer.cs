// WinForms Designer owns this file and generates fields without nullable initializers.
#nullable disable
#pragma warning disable CS0649

using System.ComponentModel;

namespace ModernUI.WinForms.Gallery;

partial class GalleryForm
{
    private IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        SuspendLayout();
        // 
        // GalleryForm
        // 
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1280, 820);
        Name = "GalleryForm";
        ResumeLayout(false);
    }
}
