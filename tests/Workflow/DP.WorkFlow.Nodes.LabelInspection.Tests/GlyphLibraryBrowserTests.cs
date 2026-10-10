using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using DP.LabelInspection;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

[Collection("LabelInspectionModernUi")]
public sealed class GlyphLibraryBrowserTests
{
    [Fact]
    public void ClickingLeftThumbnail_ChangesReadonlyRightPreview_WithoutPublishing()
    {
        Run(() =>
        {
            using var rig = new Rig();
            using var browser = new GlyphLibraryControl();
            browser.AttachManager(rig.Store);
            using var window = new Form { ClientSize = new Size(1200, 760), ShowInTaskbar = false };
            window.Controls.Add(browser);
            window.Show();
            window.PerformLayout();
            var gallery = Field<FlowLayoutPanel>(browser, "_gallery");
            var viewer = Field<ImageViewerControl>(browser, "_viewer");
            var split = browser.Controls.OfType<ModernSplitter>().Single();
            Assert.Equal(2, gallery.Controls.Count);
            Assert.Equal(Orientation.Vertical, split.Orientation);
            Assert.True(split.Panel1.Contains(gallery));
            Assert.True(split.Panel2.Contains(viewer));
            Assert.False(viewer.AllowRegionDrawing);
            var picture = gallery.Controls.Cast<Control>().SelectMany(c => c.Controls.OfType<PictureBox>())
                .Single(p => p.AccessibleName == "中");
            typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(picture, new object[] { EventArgs.Empty });
            Assert.Equal("中", browser.SelectedCharacter);
            Assert.Contains("中", Field<Label>(browser, "_previewTitle").Text);
            Assert.Equal("Gray8", viewer.DisplayPixelLayout);
            Assert.Equal(3, rig.Store.Latest(rig.Id));
            Assert.True(split.Panel1.Width >= 220);
            Assert.True(viewer.Width > split.Panel1.Width);
        });
    }

    [Fact]
    public void Thumbnails_PlaceImageAboveCaption_AndAddColumnsWhenSidebarWidens()
    {
        Run(() =>
        {
            using var rig = new Rig();
            int revision = 3;
            foreach (string character in new[] { "B", "C", "D", "E", "F", "G", "H", "I" })
                revision = rig.Store.PutGlyph(rig.Id, revision, character, Frame());
            using var browser = new GlyphLibraryControl();
            browser.AttachManager(rig.Store);
            using var window = new Form { ClientSize = new Size(1200, 760), ShowInTaskbar = false };
            window.Controls.Add(browser);
            window.Show();
            Application.DoEvents();
            var gallery = Field<FlowLayoutPanel>(browser, "_gallery");
            var cards = gallery.Controls.Cast<Control>().ToArray();
            Assert.All(cards, card =>
            {
                var picture = card.Controls.OfType<PictureBox>().Single();
                var caption = card.Controls.OfType<Label>().Single();
                Assert.True(picture.Bottom <= caption.Top, "Thumbnail caption must be beneath the image.");
            });
            int narrowColumns = cards.Count(c => c.Top == cards[0].Top);
            Assert.True(narrowColumns >= 2);
            Assert.True(narrowColumns < cards.Length);
            var split = browser.Controls.OfType<ModernSplitter>().Single();
            split.SplitterDistance = 640;
            window.PerformLayout();
            Application.DoEvents();
            int wideColumns = cards.Count(c => c.Top == cards[0].Top);
            Assert.True(wideColumns > narrowColumns, $"Column count did not grow: {narrowColumns} -> {wideColumns}");
        });
    }

    [Fact]
    public void DeleteSingleGlyph_PublishesNewRevision_AndPreservesOldPixels()
    {
        Run(() =>
        {
            using var rig = new Rig();
            using var browser = new GlyphLibraryControl();
            browser.AttachManager(rig.Store);
            string removed = browser.SelectedCharacter!;
            browser.DeleteSelectedGlyph();
            Assert.DoesNotContain(removed, rig.Store.Load(rig.Id, 4).Glyphs.Keys);
            Assert.Contains(removed, rig.Store.Load(rig.Id, 3).Glyphs.Keys);
            Assert.Single(Field<FlowLayoutPanel>(browser, "_gallery").Controls.Cast<Control>());
            Assert.NotEqual(removed, browser.SelectedCharacter);
            browser.DeleteSelectedGlyph();
            Assert.Null(browser.SelectedCharacter);
            Assert.Empty(Field<FlowLayoutPanel>(browser, "_gallery").Controls.Cast<Control>());
            Assert.Equal(string.Empty, Field<ImageViewerControl>(browser, "_viewer").DisplayPixelLayout);
        });
    }

    [Fact]
    public void DeleteLibrary_RemovesActiveChoice_ButKeepsPinnedHistoricalVersions()
    {
        Run(() =>
        {
            using var rig = new Rig();
            using var browser = new GlyphLibraryControl();
            browser.AttachManager(rig.Store);
            browser.DeleteSelectedLibrary();
            Assert.Empty(rig.Store.ListLibraries());
            Assert.True(rig.Store.ListLibraries(true).Single().Archived);
            Assert.Equal(2, rig.Store.Load(rig.Id, 3).Glyphs.Count);
            Assert.Null(browser.SelectedLibraryId);
            Assert.Null(browser.SelectedCharacter);
            Assert.Empty(Field<FlowLayoutPanel>(browser, "_gallery").Controls.Cast<Control>());
            Assert.Equal(string.Empty, Field<ImageViewerControl>(browser, "_viewer").DisplayPixelLayout);
            Assert.Throws<InvalidOperationException>(browser.DeleteSelectedGlyph);
        });
    }

    [Fact]
    public void StaleBrowserDelete_IsRejected_WithoutRemovingNewerData()
    {
        Run(() =>
        {
            using var rig = new Rig();
            using var browser = new GlyphLibraryControl();
            browser.AttachManager(rig.Store);
            rig.Store.PutGlyph(rig.Id, 3, "B", Frame());
            Assert.Throws<InvalidOperationException>(browser.DeleteSelectedGlyph);
            Assert.Equal(3, rig.Store.Load(rig.Id, 4).Glyphs.Count);
            browser.RefreshLibraries();
            Assert.Equal(3, Field<FlowLayoutPanel>(browser, "_gallery").Controls.Count);
        });
    }

    [Fact]
    public void BuilderRefresh_PreservesDraftCandidates_WhenSwitchingTarget()
    {
        Run(() =>
        {
            using var rig = new Rig();
            string other = rig.Store.CreateLibrary("Other");
            using var builder = new GlyphQuickBuilderControl();
            builder.AttachServices(rig.Store, selectedLibrary: rig.Id);
            builder.SetCandidate(Frame(), "X");
            Assert.Single(builder.Candidates.Rows.Cast<DataGridViewRow>());
            builder.RefreshLibraries(rig.Id);
            Assert.Equal(rig.Id, builder.SelectedLibraryId);
            Assert.Equal("X", builder.Candidates.Rows[0].Cells["Character"].Value);
            // 没有跨图待入库清单：切换目标字库只改变保存位置，当前图候选保留。
            builder.RefreshLibraries(other);
            Assert.Equal(other, builder.SelectedLibraryId);
            Assert.Equal("X", builder.Candidates.Rows[0].Cells["Character"].Value);
        });
    }

    [Fact]
    public void EditingStoredGlyph_OnlyPublishesFromBuilder_AndKeepsBinarization()
    {
        Run(() =>
        {
            using var rig = new Rig();
            rig.Store.PutGlyph(rig.Id, 3, "A", Frame(), "midpoint");
            using var builder = new GlyphQuickBuilderControl();
            builder.AttachServices(rig.Store, selectedLibrary: rig.Id);
            builder.EditStoredGlyph("A");
            Assert.Equal(4, rig.Store.Latest(rig.Id));
            Assert.Equal("A", builder.Candidates.Rows[0].Cells["Character"].Value);
            Assert.Equal("midpoint", Field<ModernSelect>(builder, "_binarization").SelectedItem);
            builder.Candidates.Rows[0].Cells["Use"].Value = true;
            builder.RefreshLibraries(rig.Id);
            Assert.True(Field<ModernCheckbox>(builder, "_replace").Checked);
            Assert.Equal(5, builder.SaveSelected());
            Assert.Equal("midpoint", rig.Store.Load(rig.Id, 5).Glyphs["A"].Binarization);
            Assert.Equal("midpoint", rig.Store.Load(rig.Id, 4).Glyphs["A"].Binarization);
        });
    }

    [Fact]
    public void WorkbenchTabs_ShareLibrarySelectionAndNewRevision_WithoutRecreatingDraft()
    {
        Run(() =>
        {
            using var rig = new Rig();
            string other = rig.Store.CreateLibrary("Other");
            using var workbench = new LabelInspectionControl();
            workbench.AttachLibraryManager(rig.Store);
            using var timer = new System.Windows.Forms.Timer { Interval = 20 };
            Exception? failure = null;
            bool inspected = false;
            timer.Tick += (_, _) =>
            {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text.StartsWith("字库 ·"));
                if (dialog == null) return;
                timer.Stop();
                GlyphQuickBuilderControl? builder = null;
                try
                {
                    inspected = true;
                    var tabs = Descendants(dialog).OfType<ModernTabControl>().Single();
                    var browser = Descendants(dialog).OfType<GlyphLibraryControl>().Single();
                    builder = Descendants(dialog).OfType<GlyphQuickBuilderControl>().Single();
                    browser.RefreshLibraries(other);
                    tabs.SelectedIndex = 1;
                    Assert.Equal(other, builder.SelectedLibraryId);
                    builder.SetCandidate(Frame(), "X");
                    object candidate = builder.Candidates.Rows[0].Tag!;
                    builder.Candidates.Rows[0].Cells["Use"].Value = true;
                    builder.SaveSelected();
                    tabs.SelectedIndex = 0;
                    Assert.Equal(other, browser.SelectedLibraryId);
                    Assert.Equal("X", browser.SelectedCharacter);
                    Assert.Single(Field<FlowLayoutPanel>(browser, "_gallery").Controls.Cast<Control>());
                    Assert.Same(candidate, builder.Candidates.Rows[0].Tag);
                }
                catch (Exception error) { failure = error; }
                finally
                {
                    if (builder != null)
                    {
                        Field<DP.LabelInspection.Core.GlyphDraftSession>(builder, "_draft").ClearPending();
                        builder.SetImage(Frame());
                    }
                    dialog.Close();
                }
            };
            timer.Start();
            workbench.OpenGlyphLibraries();
            Assert.True(inspected);
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        });
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static PixelSnapshot Frame() => new(24, 40, EImagePixelFormat.Gray8,
        Enumerable.Repeat((byte)180, 24 * 40).ToArray());

    private static T Field<T>(object page, string name) => (T)page.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Glyph browser test timed out.");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "glyph-browser-" + Guid.NewGuid().ToString("N"));
        internal InspectionStore Store { get; }
        internal string Id { get; }
        internal Rig()
        {
            Directory.CreateDirectory(_root);
            Store = new InspectionStore(_root, new OpenCvImageCodec());
            Id = Store.CreateLibrary("浏览测试库");
            Store.PutGlyph(Id, 1, "A", Frame());
            Store.PutGlyph(Id, 2, "中", Frame());
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
