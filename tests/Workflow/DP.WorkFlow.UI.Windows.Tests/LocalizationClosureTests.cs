using System.ComponentModel;
using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using System.Xml.Linq;
using ModernPropertyGrid.WinForms;
using ModernUI.Localization;
using ModernUI.WinForms;
using DP.WorkFlow.UI;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class LocalizationClosureTests
{
    [Fact]
    public void NamedPlaceholders_SupportCultureFormatsEscapesAndStrictArguments()
    {
        var text = LocalizationTemplate.Format("{{total}} {count:N2} / {name}",
            CultureInfo.GetCultureInfo("en-US"), new Dictionary<string, object?>
            {
                ["count"] = 1234.5,
                ["name"] = "camera"
            });

        Assert.Equal("{total} 1,234.50 / camera", text);
        Assert.True(LocalizationTemplate.GetPlaceholderNames("{count:N2} {name} {{literal}}").SetEquals(["count", "name"]));
        Assert.Throws<FormatException>(() => LocalizationTemplate.Format("Missing {value}", CultureInfo.InvariantCulture));
        Assert.Throws<FormatException>(() => LocalizationTemplate.GetPlaceholderNames("Broken {value"));
    }

    [Fact]
    public void AllLocalizedResxFiles_HaveSameKeysAndPlaceholdersAsNeutralResources()
    {
        var sourceRoot = FindSourceRoot();
        var translations = Directory.GetFiles(sourceRoot, "*.resx", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".en-US.resx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".zh-CN.resx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(translations);
        foreach (var translation in translations)
        {
            var baseline = translation
                .Replace(".en-US.resx", ".resx", StringComparison.OrdinalIgnoreCase)
                .Replace(".zh-CN.resx", ".resx", StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(baseline), $"Missing neutral resource for {translation}");
            var issues = LocalizationResourceValidator.Validate(ReadResx(baseline), ReadResx(translation));
            Assert.True(issues.Count == 0, $"{translation}: {string.Join("; ", issues.Select(issue => $"{issue.Key}: {issue.Message}"))}");
        }
    }

    [Fact]
    public void PropertyGrid_LocaleSwitchPreservesValueSelectionAndStableCategoryState()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var zh = CultureInfo.GetCultureInfo("zh-CN");
                using var manager = new LocalizationManager(PropertyGridLocalization.CreateContext(zh).Current,
                    (culture, _) => Task.FromResult(PropertyGridLocalization.CreateContext(culture).Current));
                using var host = new Form { ClientSize = new Size(520, 360) };
                var model = new LocalizedFixture { Mode = FixtureMode.Fast };
                using var grid = new ModernPropertyGrid.WinForms.ModernPropertyGrid
                {
                    Dock = DockStyle.Fill,
                    AnimateCategoryExpansion = false,
                    LocalizationContext = manager.Context,
                    PresentationProvider = new FixturePresentationProvider(),
                    SelectedObject = model
                };
                host.Controls.Add(grid);
                host.Show();
                Application.DoEvents();

                Assert.Contains(Descendants(grid).OfType<Label>(), label => label.Text == "名称");
                var nameEditor = Descendants(grid).OfType<ModernInput>().Single(input => input.AccessibleName == "名称");
                nameEditor.Focus();
                Application.DoEvents();
                var category = Descendants(grid).OfType<ModernButton>().Single(button => button.Text.Contains("采集", StringComparison.Ordinal));
                var categoryBody = category.Parent!.Controls[category.Parent.Controls.IndexOf(category) + 1];
                category.PerformClick();
                Application.DoEvents();
                Assert.False(categoryBody.Visible);
                manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US")).GetAwaiter().GetResult();
                Application.DoEvents();

                Assert.Equal(FixtureMode.Fast, model.Mode);
                Assert.Equal("Search properties (Ctrl+F)", Descendants(grid).OfType<ModernInput>().First().PlaceholderText);
                var englishCategory = Descendants(grid).OfType<ModernButton>().Single(button => button.Text.Contains("Acquisition", StringComparison.Ordinal));
                var englishBody = englishCategory.Parent!.Controls[englishCategory.Parent.Controls.IndexOf(englishCategory) + 1];
                Assert.False(englishBody.Visible);
                Assert.Contains(Descendants(grid).OfType<Label>(), label => label.Text.Contains("Internal name: Name", StringComparison.Ordinal));
                var modeSelect = Descendants(grid).OfType<ModernSelect>().Single();
                Assert.Contains("Fast", modeSelect.AccessibilityObject.Value ?? string.Empty, StringComparison.Ordinal);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "PropertyGrid localization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void AlertAndValidationSummary_ExposeLocalizedAccessibleContentAndErrorChildren()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
                using var managerLifetime = (IDisposable)manager;
                using var host = new Form { ClientSize = new Size(500, 180) };
                using var alert = new ModernAlert { Bounds = new Rectangle(10, 10, 460, 60), Text = "保存成功", Description = "配置已经应用" };
                using var input = new ModernInput { Bounds = new Rectangle(10, 90, 180, 34) };
                using var summary = new ModernValidationSummary { Bounds = new Rectangle(210, 84, 260, 70), LocalizationContext = manager.Context };
                using var provider = new ModernValidationProvider { LocalizationContext = manager.Context };
                summary.Provider = provider;
                host.Controls.AddRange([alert, input, summary]);
                host.Show();
                provider.SetValidation(input, ModernValidationState.Error, "设备地址格式无效");
                Application.DoEvents();

                Assert.Equal(AccessibleRole.Alert, alert.AccessibilityObject.Role);
                Assert.Contains("保存成功", alert.AccessibilityObject.Name, StringComparison.Ordinal);
                Assert.Contains("配置已经应用", alert.AccessibilityObject.Name, StringComparison.Ordinal);
                Assert.Equal(1, summary.AccessibilityObject.GetChildCount());
                var issue = Assert.IsAssignableFrom<AccessibleObject>(summary.AccessibilityObject.GetChild(0));
                Assert.Equal("设备地址格式无效", issue.Name);
                Assert.Equal("定位到验证问题", issue.DefaultAction);
                issue.DoDefaultAction();
                Assert.True(input.ContainsFocus);

                manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US")).GetAwaiter().GetResult();
                Application.DoEvents();
                Assert.Equal("1 validation issues found", summary.AccessibleName);
                Assert.Equal("Go to validation issue", summary.AccessibilityObject.GetChild(0)!.DefaultAction);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Accessibility localization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void DurationInput_LocalizesUnitsWithoutChangingTimeSpanValue()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
                using var managerLifetime = (IDisposable)manager;
                using var duration = new ModernDurationInput
                {
                    LocalizationContext = manager.Context,
                    Unit = ModernDurationUnit.Minutes,
                    Value = TimeSpan.FromMinutes(90)
                };
                var unit = duration.Controls.OfType<ModernSelect>().Single();
                Assert.Equal("分钟", unit.AccessibilityObject.Value);

                manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US")).GetAwaiter().GetResult();
                Assert.Equal("Minutes", unit.AccessibilityObject.Value);
                Assert.Equal(TimeSpan.FromMinutes(90), duration.Value);
                Assert.Equal(ModernDurationUnit.Minutes, duration.Unit);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Duration localization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void CommandBar_AccessibilityExposesVisibleCommandsAndLocalizedOverflow()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
                using var managerLifetime = (IDisposable)manager;
                using var host = new Form { ClientSize = new Size(520, 100) };
                using var bar = new ModernCommandBar { Bounds = new Rectangle(10, 10, 480, 44), LocalizationContext = manager.Context };
                bar.Commands.Add(new ModernCommand { Text = "运行" });
                bar.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
                bar.Commands.Add(new ModernCommand { Text = "设置" });
                bar.Commands.Add(new ModernCommand { Text = "诊断" });
                host.Controls.Add(bar);
                host.Show();
                Application.DoEvents();

                Assert.Equal(3, bar.AccessibilityObject.GetChildCount());
                Assert.Equal("运行", bar.AccessibilityObject.GetChild(0)!.Name);
                bar.Width = 120;
                Application.DoEvents();
                Assert.True(bar.OverflowVisible);
                var children = Enumerable.Range(0, bar.AccessibilityObject.GetChildCount())
                    .Select(index => bar.AccessibilityObject.GetChild(index)!).ToArray();
                Assert.Contains(children, child => child.Name == "更多操作");

                manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US")).GetAwaiter().GetResult();
                Application.DoEvents();
                children = Enumerable.Range(0, bar.AccessibilityObject.GetChildCount())
                    .Select(index => bar.AccessibilityObject.GetChild(index)!).ToArray();
                Assert.Contains(children, child => child.Name == "More actions");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "CommandBar accessibility test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Dialog_ExposesAccessibleContentAndUsesLocalizationTextDirection()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("ar-SA"));
                using var managerLifetime = (IDisposable)manager;
                using var host = new Form { ClientSize = new Size(320, 160) };
                host.Show();
                Form? observed = null;
                using var closeTimer = new System.Windows.Forms.Timer { Interval = 30 };
                closeTimer.Tick += (_, _) =>
                {
                    observed = Application.OpenForms.Cast<Form>()
                        .FirstOrDefault(form => !ReferenceEquals(form, host));
                    if (observed is null) return;
                    closeTimer.Stop();
                    observed.DialogResult = DialogResult.Cancel;
                };
                closeTimer.Start();

                ModernDialog.Show(host, new ModernDialogOptions
                {
                    Title = "Delete workflow",
                    Message = "This operation cannot be undone.",
                    LocalizationContext = manager.Context
                });

                Assert.NotNull(observed);
                Assert.Equal(AccessibleRole.Dialog, observed!.AccessibilityObject.Role);
                Assert.Equal("Delete workflow", observed.AccessibilityObject.Name);
                Assert.Equal("This operation cannot be undone.", observed.AccessibilityObject.Description);
                Assert.Equal(RightToLeft.Yes, observed.RightToLeft);
                Assert.True(observed.RightToLeftLayout);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Dialog accessibility test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void Dialog_RestoresPreviouslyFocusedControl()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                using var host = new Form { ClientSize = new Size(320, 160) };
                using var input = new TextBox { Bounds = new Rectangle(20, 20, 180, 28) };
                host.Controls.Add(input);
                host.Show();
                input.Focus();
                Application.DoEvents();
                using var closeTimer = new System.Windows.Forms.Timer { Interval = 30 };
                closeTimer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(form => !ReferenceEquals(form, host));
                    if (dialog is null) return;
                    closeTimer.Stop();
                    dialog.DialogResult = DialogResult.Cancel;
                };
                closeTimer.Start();
                ModernDialog.Show(host, new ModernDialogOptions { Title = "Confirm", Message = "Continue?" });
                Application.DoEvents();
                Assert.True(input.ContainsFocus);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Dialog focus restoration test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void RuntimeMonitor_LocaleSwitchPreservesFiltersTabAndDateValidation()
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try
            {
                var localizationManager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
                using var managerLifetime = (IDisposable)localizationManager;
                using var host = new Form { ClientSize = new Size(980, 360) };
                using var monitor = new WorkflowRuntimeMonitorControl
                {
                    Dock = DockStyle.Fill,
                    LocalizationContext = localizationManager.Context
                };
                host.Controls.Add(monitor);
                host.Show();
                Application.DoEvents();

                var tabs = Descendants(monitor).OfType<ModernTabControl>().Single();
                var filter = Descendants(monitor).OfType<ModernInput>().Single();
                tabs.SelectedIndex = 3;
                monitor.TraceFilter = "Node-A";
                monitor.TraceStartDate = DateTime.Today.AddDays(-100);
                monitor.TraceEndDate = DateTime.Today;
                Application.DoEvents();
                Assert.False(monitor.TraceRangeIsValid);

                localizationManager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US")).GetAwaiter().GetResult();
                Application.DoEvents();

                Assert.Equal(3, tabs.SelectedIndex);
                Assert.Equal("Node-A", monitor.TraceFilter);
                Assert.Equal(DateTime.Today.AddDays(-100), monitor.TraceStartDate);
                Assert.False(monitor.TraceRangeIsValid);
                Assert.Equal("No runtime data", monitor.SummaryText);
                Assert.Equal("Filter node, token, scope, step, or message", filter.PlaceholderText);
                Assert.Contains(Descendants(monitor).OfType<ModernCommandBar>().Single().Commands,
                    command => command.Text == "Export CSV");
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Runtime monitor localization test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void RuntimeMonitorModel_NormalizesOpenDateRangeWithoutChangingBusinessData()
    {
        var model = new WorkflowRuntimeMonitorModel();
        var start = DateTime.Today;
        var end = start.AddDays(-7);
        model.SetTraceDateRange(start, end);
        Assert.Equal(end, model.TraceStartDate);
        Assert.Equal(start, model.TraceEndDate);
        using var writer = new StringWriter();
        model.ExportTraceCsv(writer, new WorkflowTraceCsvHeaders("序号", "时间", "节点", "令牌", "作用域", "步骤", "消息"));
        Assert.StartsWith("\"序号\",\"时间\",\"节点\"", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeStateText_LocalizesStableEnumsAndPreservesCustomTraceSteps()
    {
        var manager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
        using var managerLifetime = (IDisposable)manager;
        var executionState = E_WorkflowExecutionState.Faulted;
        var nodeState = E_NodeState.Running;

        Assert.Equal("故障", WorkflowRuntimeText.ExecutionState(manager.Context, executionState));
        Assert.Equal("执行中", WorkflowRuntimeText.NodeState(manager.Context, nodeState));
        Assert.Equal("节点开始", WorkflowRuntimeText.TraceStep(manager.Context, "NodeStarted"));
        Assert.Equal("Vendor.CustomStep", WorkflowRuntimeText.TraceStep(manager.Context, "Vendor.CustomStep"));

        await manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal("Faulted", WorkflowRuntimeText.ExecutionState(manager.Context, executionState));
        Assert.Equal("Running", WorkflowRuntimeText.NodeState(manager.Context, nodeState));
        Assert.Equal("Node started", WorkflowRuntimeText.TraceStep(manager.Context, "NodeStarted"));
        Assert.Equal(E_WorkflowExecutionState.Faulted, executionState);
        Assert.Equal(E_NodeState.Running, nodeState);
        Assert.All(Enum.GetValues<E_WorkflowExecutionState>(), state =>
            Assert.False(string.IsNullOrWhiteSpace(WorkflowRuntimeText.ExecutionState(manager.Context, state))));
        Assert.All(Enum.GetValues<E_NodeState>(), state =>
            Assert.False(string.IsNullOrWhiteSpace(WorkflowRuntimeText.NodeState(manager.Context, state))));
    }

    [Fact]
    public void ApplicationError_RequiresStableCodeAndKeepsNamedArguments()
    {
        Assert.Throws<ArgumentException>(() => new DP.WorkFlow.ApplicationError(" "));
        var error = new DP.WorkFlow.ApplicationError(DP.WorkFlow.WorkflowErrorCodes.RuntimeExportFailed,
            new Dictionary<string, object?> { ["message"] = "disk full" }, "trace-1");
        Assert.Equal("RuntimeExportFailed", error.Code);
        Assert.Equal("disk full", error.Arguments!["message"]);
        Assert.Equal("trace-1", error.TraceId);
        var manager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("en-US"));
        using var managerLifetime = (IDisposable)manager;
        var arguments = error.Arguments!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        arguments["traceId"] = error.TraceId;
        Assert.Equal("Could not export trace: disk full\r\nTrace ID: trace-1",
            manager.Context.Text(new TextKey("workflowUi", error.Code), arguments));
    }

    private static Dictionary<string, string> ReadResx(string path) => XDocument.Load(path).Root!
        .Elements("data").ToDictionary(element => (string)element.Attribute("name")!, element => element.Element("value")?.Value ?? string.Empty, StringComparer.Ordinal);

    private static string FindSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (IsSourceRoot(candidate)) return candidate;
            candidate = Path.Combine(directory.FullName, "DP.WorkFlow", "src");
            if (IsSourceRoot(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Cannot locate DP.WorkFlow/src.");
    }

    private static bool IsSourceRoot(string candidate) =>
        Directory.Exists(Path.Combine(candidate, "Workflow"))
        && Directory.Exists(Path.Combine(candidate, "Platform"));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private enum FixtureMode { Fast, Accurate }
    private sealed class LocalizedFixture
    {
        [Category("acquisition")]
        public string Name { get; set; } = "Camera";
        [Category("acquisition")]
        public FixtureMode Mode { get; set; }
    }

    private sealed class FixturePresentationProvider : IPropertyPresentationProvider
    {
        public PropertyPresentation GetPresentation(object owner, PropertyDescriptor property, CultureInfo culture)
        {
            var english = culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase);
            return new PropertyPresentation("fixture." + property.Name,
                property.Name == nameof(LocalizedFixture.Name) ? english ? "Name" : "名称" : english ? "Mode" : "模式",
                "acquisition", english ? "Acquisition" : "采集",
                english ? "Localized property description." : "本地化属性说明。", null);
        }

        public string FormatValue(object owner, PropertyDescriptor property, object? value, CultureInfo culture)
        {
            if (value is FixtureMode mode)
                return culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                    ? mode.ToString() : mode == FixtureMode.Fast ? "高速" : "精确";
            return Convert.ToString(value, culture) ?? string.Empty;
        }
    }
}
