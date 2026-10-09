using System.Reflection;
using System.Windows.Forms;
using DP.LabelInspection;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

// Application.OpenForms 是进程共享状态；模态窗口断言不能与其他UI用例并发。
[CollectionDefinition("LabelInspectionModernUi", DisableParallelization = true)]
public sealed class LabelInspectionModernUiCollection { }

[Collection("LabelInspectionModernUi")]
public sealed class LabelInspectionModernStyleTests
{
    [Fact]
    public void Results_UseModernTabsListAndSplitter()
    {
        Run(() =>
        {
            using var page = new LabelInspectionControl();
            Assert.IsType<ModernListView>(Field(page, "_evidence"));
            Assert.IsType<ModernSplitter>(Field(page, "_split"));
            var gallery = Assert.IsType<FlowLayoutPanel>(Field(page, "_glyphGallery"));
            Assert.IsType<ModernScrollView>(gallery.Parent);
            Assert.False(gallery.AutoScroll);
            var tabs = Assert.Single(ApplicationControls(page).OfType<ModernTabControl>());
            Assert.Same(ModernTheme.Dark, tabs.Theme);
            Assert.Equal(new[] { "检查证据", "缺陷标记 / 单字" }, tabs.TabPages.Cast<TabPage>().Select(p => p.Text));
            tabs.SelectedIndex = 1;
            Assert.Equal("缺陷标记 / 单字", tabs.SelectedTab!.Text);
        });
    }

    [Fact]
    public void GlyphLibrary_UsesModernToolbarReadonlyPreviewAndThumbnailMatrix()
    {
        Run(() =>
        {
            using var page = new GlyphLibraryControl();
            Assert.IsType<ModernSelect>(Field(page, "_libraries"));
            var toolbar = Assert.Single(ApplicationControls(page).OfType<ModernToolStrip>());
            Assert.Same(ModernTheme.Dark, toolbar.Theme);
            var commands = toolbar.Items.OfType<ToolStripButton>().Select(b => b.Text).ToArray();
            Assert.Contains("删除单字", commands);
            Assert.Contains("删除字库", commands);
            Assert.DoesNotContain("读取版本", commands);
            Assert.DoesNotContain("确认单字并保存新版本", commands);
            Assert.DoesNotContain("新建类别", commands);
            var split = Assert.Single(ApplicationControls(page).OfType<ModernSplitter>());
            Assert.Equal(Orientation.Vertical, split.Orientation);
            Assert.False(Assert.IsType<ImageViewerControl>(Field(page, "_viewer")).AllowRegionDrawing);
            var gallery = Assert.IsType<FlowLayoutPanel>(Field(page, "_gallery"));
            Assert.IsType<ModernScrollView>(gallery.Parent);
            Assert.False(gallery.AutoScroll);
            Assert.Equal(FlowDirection.LeftToRight, gallery.FlowDirection);
            Assert.True(gallery.WrapContents);
        });
    }

    [Fact]
    public void MultiImageBuilder_UsesModernEditorsTablesAndScrollers()
    {
        Run(() =>
        {
            using var page = new GlyphQuickBuilderControl();
            foreach (var name in new[] { "_libraries", "_mode", "_regionsBox" })
                Assert.IsType<ModernSelect>(Field(page, name));
            Assert.IsType<ModernInput>(Field(page, "_text"));
            Assert.IsType<ModernTextArea>(Field(page, "_coverage"));
            Assert.IsType<ModernListBox>(Field(page, "_saved"));
            Assert.IsType<ModernCheckbox>(Field(page, "_replace"));
            foreach (var name in new[] { "_recognize", "_segment", "_extractAll", "_cancelButton" })
                Assert.IsType<ModernButton>(Field(page, name));
            var candidates = Assert.IsType<ModernDataGridView>(page.Candidates);
            var pending = Assert.IsType<ModernDataGridView>(Field(page, "_pending"));
            Assert.False(candidates.ReadOnly);
            Assert.True(pending.ReadOnly);
            Assert.Equal(65, candidates.RowTemplate.Height);
            Assert.Same(ModernTheme.Dark, candidates.Theme);
            Assert.Equal(2, ApplicationControls(page).OfType<ModernSplitter>().Count());
        });
    }

    [Fact]
    public void ModernPages_AfterRealLayout_KeepButtonsAndContentPanelsUsable()
    {
        Run(() =>
        {
            foreach (Control page in new Control[] { new GlyphLibraryControl(), new GlyphQuickBuilderControl() })
            {
                using (page)
                using (var window = new Form { ClientSize = new System.Drawing.Size(1400, 850), ShowInTaskbar = false })
                {
                    window.Controls.Add(page);
                    window.Show();
                    window.PerformLayout();
                    var buttons = ApplicationControls(page).OfType<ModernButton>().ToArray();
                    Assert.True(buttons.Length > 0 || page is GlyphLibraryControl);
                    Assert.All(buttons, button =>
                    {
                        Assert.True(button.Width >= 48, $"Button '{button.Text}' collapsed: {button.Size}");
                        Assert.True(button.Height >= 28, $"Button '{button.Text}' is clipped: {button.Size}");
                    });
                    foreach (var split in ApplicationControls(page).OfType<ModernSplitter>())
                    {
                        if (split.Orientation == Orientation.Vertical)
                            Assert.True(split.Panel2.Width >= 250, "Pending list panel was squeezed by the splitter default.");
                        else
                            Assert.True(split.Panel2.Height >= 120, "Gallery/candidate panel was squeezed by the splitter default.");
                    }
                }
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlyphDialog_WithoutHostIdleStyling_UsesModernTabsAndRequestedPage(bool builder)
    {
        Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "label-style-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store = new InspectionStore(root, new OpenCvImageCodec());
                using var page = new LabelInspectionControl();
                page.AttachLibraryManager(store);
                using var timer = new System.Windows.Forms.Timer { Interval = 20 };
                Exception? failure = null;
                bool inspected = false;
                timer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text.StartsWith("字库 ·"));
                    if (dialog is null) return;
                    timer.Stop();
                    try
                    {
                        inspected = true;
                        var tabs = Assert.Single(ApplicationControls(dialog).OfType<ModernTabControl>());
                        Assert.Same(ModernTheme.Dark, tabs.Theme);
                        Assert.Equal(builder ? "多图制库" : "单字库", tabs.SelectedTab!.Text);
                        Assert.Equal(ModernTheme.Dark.Background, dialog.BackColor);
                    }
                    catch (Exception error) { failure = error; }
                    finally { dialog.Close(); }
                };
                timer.Start();
                page.OpenGlyphLibraries(builder);
                Assert.True(inspected);
                Assert.Null(failure);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    private static object Field(object page, string name) => page.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    // Modern控件的内部原生编辑器保留输入法/键盘语义，不属于应用遗漏的控件。
    private static IEnumerable<Control> ApplicationControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            if (child is ModernTabControl tabs)
            {
                foreach (TabPage page in tabs.TabPages)
                    foreach (var descendant in ApplicationControls(page)) yield return descendant;
            }
            else if (child is ModernSplitter split)
            {
                foreach (var descendant in ApplicationControls(split.Panel1)) yield return descendant;
                foreach (var descendant in ApplicationControls(split.Panel2)) yield return descendant;
            }
            else if (child.GetType().Namespace != typeof(ModernTheme).Namespace)
                foreach (var descendant in ApplicationControls(child)) yield return descendant;
        }
    }

    private static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Label UI test timed out.");
        Assert.Null(failure);
    }
}
