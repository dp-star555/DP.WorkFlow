using System.Reflection;
using System.Windows.Forms;
using DP.LabelInspection;
using DP.LabelInspection.Contracts;
using DP.LabelInspection.Core;
using DP.LabelInspection.Runtime;
using DP.LabelInspection.Storage;
using ModernUI.WinForms;
using PixelRect = DP.Vision.Algorithms.PixelBounds;

namespace DP.WorkFlow.Tests;

/// <summary>通过实际STA消息循环和原生字库控件锁定同图多ROI行为。</summary>
public sealed class GlyphMultiRoiWorkbenchTests
{
    [Fact]
    public Task AttachingService_ImmediatelyEnablesAllExtractionButtons() =>
        RunStaAsync(form =>
        {
            using var rig = new Rig(form, new FixtureService());
            rig.Page.SetRegion(new PixelRect(0, 0, 64, 32));
            Assert.True(Field<ModernButton>(rig.Page, "_recognize").Enabled);
            Assert.True(Field<ModernButton>(rig.Page, "_segment").Enabled);
            Assert.True(Field<ModernButton>(rig.Page, "_extractAll").Enabled);
            return Task.CompletedTask;
        });

    [Fact]
    public Task RecognizeThenEdit_StagesWithoutAdditionalCharacterConfirmation() =>
        RunStaAsync(async form =>
        {
            using var rig = new Rig(form, new FixtureService());
            rig.Page.SetRegion(new PixelRect(0, 0, 64, 32));
            await rig.Page.ExtractAsync();
            rig.Page.Candidates.Rows[0].Cells["Character"].Value = "中";
            rig.Page.Candidates.Rows[0].Cells["Use"].Value = true;
            rig.Page.StageSelected();
            Assert.Equal(1, rig.Page.PendingCount);
            rig.Page.SavePending();
            Assert.Contains("中", rig.Store.Load(rig.Id, 2).Glyphs.Keys);
            string json = rig.Store.ExportLibrary(rig.Id, 2);
            Assert.Contains("user_selected", json);
            Assert.DoesNotContain("human_reviewed", json);
        });

    [Fact]
    public Task TwoRealExtractions_KeepOtherRoiEditsAndCheckedRows() =>
        RunStaAsync(async form =>
        {
            using var rig = new Rig(form);
            using var backend = new OpenCvInspectionBackend();
            using var engine = new InspectionEngine(backend);
            rig.Page.AttachServices(rig.Store, engine, rig.Id);
            rig.Page.SetRegion(new PixelRect(0, 0, 64, 32));
            string first = rig.Page.SelectedRegionId!;
            await rig.Page.ExtractAsync("A");
            var candidate = (GlyphDraftCandidate)rig.Page.Candidates.Rows[0].Tag!;
            rig.Page.Candidates.Rows[0].Cells["Character"].Value = "中";
            rig.Page.Candidates.Rows[0].Cells["Use"].Value = true;
            rig.Page.SetRegion(new PixelRect(0, 40, 64, 32));
            Assert.Equal("中", Labels(rig.Page).Single());
            await rig.Page.ExtractAsync("B");
            Assert.Equal(new[] { "中", "B" }, Labels(rig.Page));
            Assert.Equal(candidate.Id, ((GlyphDraftCandidate)rig.Page.Candidates.Rows[0].Tag!).Id);
            Assert.Equal(true, rig.Page.Candidates.Rows[0].Cells["Use"].Value);
            string secondCandidate = ((GlyphDraftCandidate)rig.Page.Candidates.Rows[1].Tag!).Id;
            rig.Page.SelectRegion(first);
            Assert.Equal("A", Field<ModernInput>(rig.Page, "_text").Text);
            await rig.Page.ExtractAsync("：");
            Assert.Equal(new[] { "：", "B" }, Labels(rig.Page));
            Assert.Equal(
                secondCandidate,
                ((GlyphDraftCandidate)rig.Page.Candidates.Rows[1].Tag!).Id
            );
        });

    [Fact]
    public Task BatchExtraction_UsesIndependentManualTexts_ContinuesFailureAndPreservesOldCandidates() =>
        RunStaAsync(async form =>
        {
            var service = new FixtureService();
            using var rig = new Rig(form, service);
            rig.Page.SetRegion(new PixelRect(0, 0, 64, 32));
            string first = rig.Page.SelectedRegionId!;
            Field<ModernInput>(rig.Page, "_text").Text = "中";
            rig.Page.SetRegion(new PixelRect(0, 40, 64, 32));
            string second = rig.Page.SelectedRegionId!;
            await rig.Page.ExtractAsync("：");
            string existing = ((GlyphDraftCandidate)rig.Page.Candidates.Rows[0].Tag!).Id;
            rig.Page.SetRegion(new PixelRect(0, 80, 64, 32));
            string third = rig.Page.SelectedRegionId!;
            Field<ModernInput>(rig.Page, "_text").Text = "文";
            service.FailAtY = 40;
            service.Calls.Clear();
            await rig.Page.ExtractAllAsync();
            Assert.Equal(new[] { "中", "：", "文" }, service.Calls.Select(c => c.Text));
            Assert.Equal(3, rig.Page.Candidates.Rows.Count);
            Assert.Contains(
                rig.Page.Candidates.Rows.Cast<DataGridViewRow>(),
                r => ((GlyphDraftCandidate)r.Tag!).Id == existing
            );
            Assert.NotNull(rig.Page.Regions.Single(r => r.Id == second).Error);
            Assert.Null(rig.Page.Regions.Single(r => r.Id == third).Error);
            rig.Page.SelectRegion(first);
            Assert.Equal("中", Field<ModernInput>(rig.Page, "_text").Text);
            rig.Page.SelectRegion(second);
            Assert.Equal("：", Field<ModernInput>(rig.Page, "_text").Text);
        });

    [Fact]
    public Task CancelBatch_ProtectsInFlightSnapshot_AndStopsRemainingRois() =>
        RunStaAsync(async form =>
        {
            var service = new FixtureService { WaitAtY = 40 };
            using var rig = new Rig(form, service);
            foreach (int y in new[] { 0, 40, 80 })
                rig.Page.SetRegion(new PixelRect(0, y, 64, 32));
            var pending = rig.Page.ExtractAllAsync();
            await service.Entered.Task;
            Assert.True(rig.Page.IsBusy);
            Assert.Equal(new[] { "A" }, Labels(rig.Page));
            Assert.Throws<InvalidOperationException>(() =>
                rig.Page.SetRegion(new PixelRect(0, 0, 64, 32))
            );
            Assert.Throws<InvalidOperationException>(() =>
                rig.Page.SelectRegion(rig.Page.Regions[0].Id)
            );
            Assert.Throws<InvalidOperationException>(() =>
                rig.Page.AddManualCandidate(new PixelRect(2, 2, 16, 20))
            );
            var cancel = rig.Page.CancelAndWaitAsync();
            Assert.False(cancel.IsCompleted);
            service.Release.TrySetResult(true); // provider deliberately ignores cancellation; page must not publish its late result
            await cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.False(rig.Page.IsBusy);
            Assert.Equal(new[] { "A" }, Labels(rig.Page));
            Assert.Equal(2, service.Calls.Count);
            Assert.Null(rig.Page.Regions[2].Extraction);
            Assert.NotNull(rig.Page.Regions[1].Error);
        });

    [Fact]
    public Task MultiRoiCandidates_PublishWithSourceRoi_AndSurviveNextImageAsPending() =>
        RunStaAsync(async form =>
        {
            using var rig = new Rig(form, new FixtureService());
            foreach (int y in new[] { 0, 40, 80 })
                rig.Page.SetRegion(new PixelRect(0, y, 64, 32));
            await rig.Page.ExtractAllAsync();
            foreach (DataGridViewRow row in rig.Page.Candidates.Rows)
                row.Cells["Use"].Value = true;
            rig.Page.StageSelected();
            Assert.Equal(3, rig.Page.PendingCount);
            rig.Page.SetImage(Frame());
            Assert.Empty(rig.Page.Regions);
            Assert.Empty(Labels(rig.Page));
            Assert.Equal(3, rig.Page.PendingCount);
            Assert.Equal(2, rig.Page.SavePending());
            var json = rig.Store.ExportLibrary(rig.Id, 2);
            Assert.Contains("source_roi_id", json);
            Assert.Contains("ROI 1", json);
            Assert.Contains("ROI 3", json);
            Assert.Equal(3, rig.Store.Load(rig.Id, 2).Glyphs.Count);
        });

    [Fact]
    public Task DuplicateLabel_OnlySelectedRoiProvidesPendingSource() =>
        RunStaAsync(async form =>
        {
            using var rig = new Rig(form, new FixtureService());
            foreach (int y in new[] { 0, 40 })
            {
                rig.Page.SetRegion(new PixelRect(0, y, 64, 32));
                await rig.Page.ExtractAsync("中");
            }
            rig.Page.Candidates.Rows[1].Cells["Use"].Value = true;
            rig.Page.StageSelected();
            var pending = Field<DataGridView>(rig.Page, "_pending");
            Assert.Contains("ROI 2", Convert.ToString(pending.Rows[0].Cells[2].Value)!);
            rig.Page.SavePending();
            string json = rig.Store.ExportLibrary(rig.Id, 2);
            Assert.Contains("ROI 2", json);
            Assert.DoesNotContain("ROI 1", json);
        });

    [Fact]
    public Task EmptySegmentation_DoesNotEraseExistingRoiCandidates() =>
        RunStaAsync(async form =>
        {
            var service = new FixtureService();
            using var rig = new Rig(form, service);
            rig.Page.SetRegion(new PixelRect(0, 0, 64, 32));
            await rig.Page.ExtractAsync("中");
            service.EmptyAtY = 0;
            await rig.Page.ExtractAsync("文");
            Assert.Equal(new[] { "中" }, Labels(rig.Page));
            Assert.NotNull(rig.Page.Regions.Single().Error);
            Assert.NotNull(rig.Page.LastExtraction);
            Assert.Empty(rig.Page.LastExtraction!.Segmentation.Characters);
        });

    [Fact]
    public Task AnomalyBatchTraining_UnicodeCandidatesShowBoundaryWithoutCoverageDialog() =>
        RunStaAsync(async form =>
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "anomaly-boundary-ui-" + Guid.NewGuid().ToString("N")
            );
            try
            {
                var store = new InspectionStore(root, new OpenCvImageCodec());
                using var page = new AnomalyBatchTrainingControl { Dock = DockStyle.Fill };
                form.Controls.Add(page);
                page.AttachServices(
                    store.AnomalyLibraries,
                    new RegionAnomalyDetector(),
                    new BoundaryService()
                );
                page.SetRecipe(
                    new[]
                    {
                        new InspectionRegion(
                            "序列号",
                            ERegionKind.Text,
                            new PixelRect(0, 0, 64, 32),
                            true
                        ),
                    }
                );
                page.AddImage(Frame(), "label.png");
                var sample = page.Session.AddSample(
                    page.Session.Images[0],
                    page.Session.Models[0],
                    new PixelRect(0, 0, 64, 32)
                );
                await page.ExtractAsync(); // exact path previously failed while RefreshAll called CharacterCoverage
                Field<ImageViewerControl>(page, "_viewer").SelectedRegionIndex = -1;
                Field<ImageViewerControl>(page, "_viewer").SelectedRegionIndex = 0;
                var characters = Field<DataGridView>(page, "_characters");
                Assert.Equal(3, characters.Rows.Count);
                Assert.All(
                    characters.Rows.Cast<DataGridViewRow>(),
                    row => Assert.False(row.Cells["Use"].ReadOnly)
                );
                Assert.Equal(3, page.Session.CharacterCoverage().Count);
                Assert.Null(sample.Problem);
                Assert.Contains("中文", Field<Label>(page, "_status").Text);
                Assert.Null(page.LastPublished);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

    private sealed class BoundaryService : IGlyphCandidateService
    {
        public Task<GlyphCandidateExtraction> ExtractGlyphCandidatesAsync(
            DP.Vision.IImageSource frame,
            PixelRect bounds,
            string? confirmedText = null,
            CancellationToken token = default
        ) =>
            Task.FromResult(
                new GlyphCandidateExtraction(
                    null,
                    null,
                    new CharacterSegmentation(
                        "provisional",
                        "fixture",
                        "projection",
                        3,
                        new[] { "A", "-", "中" }.Select(
                            (label, i) =>
                                new CharacterPatch(
                                    label,
                                    i,
                                    new PixelRect(i * 20, 4, 12, 24),
                                    new PixelSnapshot(
                                        12,
                                        24,
                                        EImagePixelFormat.Gray8,
                                        new byte[288]
                                    )
                                )
                        )
                    )
                )
            );
    }

    private static string[] Labels(GlyphQuickBuilderControl page) =>
        page
            .Candidates.Rows.Cast<DataGridViewRow>()
            .Select(r => Convert.ToString(r.Cells["Character"].Value)!)
            .ToArray();

    private static T Field<T>(object value, string name) =>
        (T)
            value
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(value)!;

    private static PixelSnapshot Frame()
    {
        var pixels = Enumerable.Repeat((byte)255, 64 * 120).ToArray();
        foreach (int y0 in new[] { 6, 46, 86 })
            for (int y = y0; y < y0 + 20; y++)
            for (int x = 6; x < 26; x++)
                pixels[y * 64 + x] = 0;
        return new PixelSnapshot(64, 120, EImagePixelFormat.Gray8, pixels);
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "multi-roi-ui-" + Guid.NewGuid().ToString("N")
        );
        internal InspectionStore Store { get; }
        internal GlyphQuickBuilderControl Page { get; }
        internal string Id { get; }

        internal Rig(Form form, IGlyphCandidateService? service = null)
        {
            Store = new InspectionStore(_root, new OpenCvImageCodec());
            Id = Store.CreateLibrary("multi");
            Page = new GlyphQuickBuilderControl();
            form.Controls.Add(Page);
            Page.AttachServices(Store, service, Id);
            Page.SetImage(Frame());
        }

        public void Dispose()
        {
            Page.Dispose();
            Directory.Delete(_root, true);
        }
    }

    private sealed class FixtureService : IGlyphCandidateService
    {
        internal int? FailAtY,
            EmptyAtY,
            WaitAtY;
        internal List<(PixelRect Bounds, string? Text)> Calls { get; } = new();
        internal TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GlyphCandidateExtraction> ExtractGlyphCandidatesAsync(
            DP.Vision.IImageSource frame,
            PixelRect bounds,
            string? confirmedText = null,
            CancellationToken token = default
        )
        {
            Calls.Add((bounds, confirmedText));
            if (bounds.Y == WaitAtY)
            {
                Entered.TrySetResult(true);
                await Release.Task;
            }
            if (bounds.Y == FailAtY)
                throw new InvalidOperationException("fixture failed ROI");
            string label = confirmedText ?? ((char)('A' + bounds.Y / 40)).ToString();
            var patch = new CharacterPatch(
                label,
                0,
                new PixelRect(bounds.X + 4, bounds.Y + 4, 16, 20),
                new PixelSnapshot(16, 20, EImagePixelFormat.Gray8, new byte[320])
            );
            return new GlyphCandidateExtraction(
                null,
                confirmedText,
                new CharacterSegmentation(
                    bounds.Y == EmptyAtY ? "uncertain" : "provisional",
                    "fixture",
                    "projection",
                    1,
                    bounds.Y == EmptyAtY ? Array.Empty<CharacterPatch>() : new[] { patch }
                )
            );
        }
    }

    private static Task RunStaAsync(Func<Form, Task> action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                ShowInTaskbar = false,
                Width = 1320,
                Height = 880,
            };
            form.Shown += async (_, _) =>
            {
                try
                {
                    await action(form);
                    completion.TrySetResult();
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
                finally
                {
                    form.Close();
                }
            };
            Application.Run(form);
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
