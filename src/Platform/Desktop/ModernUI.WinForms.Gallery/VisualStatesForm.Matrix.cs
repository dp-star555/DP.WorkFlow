using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private Control CreateStateMatrix()
    {
        var content = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Margin = Padding.Empty,
            Padding = new Padding(24, 12, 24, 20)
        };

        content.Controls.Add(CreateBand("BandButtons", 104,
            Sample("Default", LocalizedText(new ModernButton(), "ButtonDefault")),
            Sample("Primary", LocalizedText(new ModernButton { ButtonType = ModernButtonType.Primary }, "ButtonPrimary")),
            Sample("Danger", LocalizedText(new ModernButton { ButtonType = ModernButtonType.Danger }, "ButtonDanger")),
            Sample("Loading", LocalizedText(new ModernButton { ButtonType = ModernButtonType.Primary, Loading = true, Width = 112 }, "ButtonLoading")),
            Sample("Disabled", LocalizedText(new ModernButton { Enabled = false }, "Disabled"))));

        content.Controls.Add(CreateBand("BandSelectionStates", 104,
            Sample("Switch Off", new ModernSwitch()),
            Sample("Switch On", new ModernSwitch { Checked = true }),
            Sample("Disabled", new ModernSwitch { Checked = true, Enabled = false }),
            Sample("Unchecked", LocalizedText(new ModernCheckbox { Width = 92 }, "Unchecked")),
            Sample("Checked", LocalizedText(new ModernCheckbox { Checked = true, Width = 92 }, "Checked")),
            Sample("Mixed", LocalizedText(new ModernCheckbox { ThreeState = true, CheckState = CheckState.Indeterminate, Width = 104 }, "Mixed"))));

        content.Controls.Add(CreateBand("BandRadioSegmented", 104,
            Sample("Unchecked", LocalizedText(new ModernRadioButton { Width = 108 }, "Unchecked")),
            Sample("Checked", LocalizedText(new ModernRadioButton { Checked = true, Width = 108 }, "Checked")),
            Sample("Disabled", LocalizedText(new ModernRadioButton { Checked = true, Enabled = false, Width = 108 }, "Disabled")),
            Sample("Segmented", CreateSegmented(), 410)));

        content.Controls.Add(CreateBand("BandDataGrid", 104,
            Sample("Editable mapping", CreateDataGrid(false), 440),
            Sample("ReadOnly selection", CreateDataGrid(true), 440)));

        content.Controls.Add(CreateBand("BandListView", 104,
            Sample("Diagnostics details", CreateListView(false), 440),
            Sample("Native checkboxes", CreateListView(true), 440)));

        content.Controls.Add(CreateBand("BandList", 104,
            Sample("Single selection", CreateListBox(false), 300),
            Sample("MultiExtended", CreateListBox(true), 300),
            Sample("Disabled", CreateListBox(false, false), 300)));

        content.Controls.Add(CreateBand("BandTree", 104,
            Sample("Selection + hierarchy", CreateTree(false), 430),
            Sample("Native checkboxes", CreateTree(true), 430)));

        content.Controls.Add(CreateBand("BandTabs", 104,
            Sample("Native TabPages", CreateTabs(), 900)));

        content.Controls.Add(CreateBand("BandTextInput", 104,
            Sample("Normal", new ModernInput { Text = "Line Camera 01", Width = 172 }, 188),
            Sample("Placeholder", LocalizedPlaceholder(new ModernInput { Width = 172 }, "InputDevicePlaceholder"), 188),
            Sample("ReadOnly", LocalizedText(new ModernInput { ReadOnly = true, Width = 172 }, "InputReadOnly"), 188),
            Sample("Error", LocalizedText(new ModernInput { HasError = true, Width = 172 }, "InputInvalid"), 188),
            Sample("Disabled", LocalizedText(new ModernInput { Enabled = false, Width = 172 }, "InputDisabled"), 188)));

        content.Controls.Add(CreateBand("BandTextArea", 104,
            Sample("Normal", new ModernTextArea { Text = "Line 1\r\nLine 2", Width = 218, Height = 80 }, 230),
            Sample("No Wrap", LocalizedText(new ModernTextArea { WordWrap = false, ScrollBars = ScrollBars.Both, Width = 218, Height = 80 }, "TextAreaNoWrap"), 230),
            Sample("ReadOnly", LocalizedText(new ModernTextArea { ReadOnly = true, Width = 218, Height = 80 }, "TextAreaReadOnly"), 230),
            Sample("Error", LocalizedText(new ModernTextArea { HasError = true, Width = 218, Height = 80 }, "TextAreaInvalid"), 230)));

        content.Controls.Add(CreateBand("BandCombo", 104,
            Sample("Free Text", CreateComboBox("Company.Product"), 230),
            Sample("Matched", CreateComboBox("System.Linq"), 230),
            Sample("Error", CreateComboBox("Invalid.Namespace", hasError: true), 230),
            Sample("Disabled", CreateComboBox("System", enabled: false), 230)));

        content.Controls.Add(CreateBand("BandNumber", 104,
            Sample("Normal", CreateNumber(12.5m, 0, 100), 206),
            Sample("Minimum", CreateNumber(0, 0, 100), 206),
            Sample("Maximum", CreateNumber(100, 0, 100), 206),
            Sample("Disabled", CreateNumber(50, 0, 100, false), 206)));

        content.Controls.Add(CreateBand("BandSelect", 104,
            Sample("Selected", CreateSelect(0), 188),
            Sample("Placeholder", CreateSelect(-1), 188),
            Sample("Disabled", CreateSelect(1, false), 188),
            Sample("Multiple", CreateMultiSelect(true), 188),
            Sample("Empty", CreateMultiSelect(false), 188)));

        content.Controls.Add(CreateBand("BandFeedback", 104,
            Sample("Progress", new ModernProgressBar { Width = 180, Value = 68, ShowPercentage = true, Height = 28 }, 196),
            Sample("Indeterminate", new ModernProgressBar { Width = 180, Indeterminate = true, Height = 28 }, 196),
            Sample("Spinner", LocalizedText(new ModernSpinner { Spinning = true, Width = 140 }, "Loading"), 156),
            Sample("Badge", CreateBadges(), 180),
            Sample("Slider", new ModernSlider { Width = 200, Value = 42, ShowValue = true }, 216)));

        content.Controls.Add(CreateBand("BandNavigation", 104,
            Sample("Collapsible", CreateCollapsible(), 250),
            Sample("Pagination", new ModernPagination { Width = 360, TotalCount = 236, PageSize = 20, PageIndex = 3 }, 376),
            Sample("Context menu", CreateContextMenuButton(), 150),
            Sample("Status bar", CreateStatusBar(), 270)));

        content.Controls.Add(CreateBand("BandCommand", 104,
            Sample("Command bar", CreateCommandBar(), 420),
            Sample("Alert", CreateLocalizedAlert(), 326),
            Sample("Message / Dialog", CreateFeedbackButtons(), 250)));

        content.Controls.Add(CreateBand("BandDateTime", 104,
            Sample("Date", new ModernDatePicker { Width = 170, Value = new DateTime(2026, 3, 14) }, 186),
            Sample("Time", new ModernTimePicker { Width = 150, Value = new TimeSpan(16, 25, 30) }, 166),
            Sample("Duration", new ModernDurationInput { Width = 230, Value = TimeSpan.FromMinutes(90), Unit = ModernDurationUnit.Minutes }, 246),
            Sample("Tag selection", CreateTagMultiSelect(), 280)));

        content.Controls.Add(CreateBand("BandRangeValidation", 104,
            Sample("Date range", new ModernDateRangePicker { Width = 330, StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 14) }, 346),
            Sample("Validation summary", CreateValidationDemo(), 500)));

        content.Controls.Add(CreateBand("BandExtendedFeedback", 104,
            Sample("Empty state", CreateLocalizedEmptyState(), 296),
            Sample("Notification", CreateNotificationButton(), 180)));

        _stateMatrix = content;
        _stateViewport = new ModernScrollView
        {
            Dock = DockStyle.Fill,
            Content = content
        };
        return _stateViewport;
    }
}
