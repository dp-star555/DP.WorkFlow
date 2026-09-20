using System.Globalization;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class VisualStatesForm
{
    private ModernDataGridView CreateDataGrid(bool readOnly)
    {
        var grid = new ModernDataGridView
        {
            Width = 420,
            Height = 170,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = !readOnly,
            AllowUserToDeleteRows = !readOnly,
            ReadOnly = readOnly,
            RowHeadersVisible = false,
            SelectionMode = readOnly ? DataGridViewSelectionMode.FullRowSelect : DataGridViewSelectionMode.CellSelect
        };
        BindGalleryText(readOnly ? "GridReadOnlyAccessible" : "GridEditableAccessible", text => grid.AccessibleName = text);
        var targetColumn = new DataGridViewTextBoxColumn { Name = "Target" };
        BindGalleryText("GridTarget", text => targetColumn.HeaderText = text);
        grid.Columns.Add(targetColumn);
        var sourceColumn = new ModernDataGridViewComboBoxColumn
        {
            Name = "Source",
            DataSource = new[] { "Literal", "Binding", "Default" }
        };
        BindGalleryText("GridSource", text => sourceColumn.HeaderText = text);
        grid.Columns.Add(sourceColumn);
        var valueColumn = new DataGridViewTextBoxColumn { Name = "Value" };
        BindGalleryText("GridValue", text => valueColumn.HeaderText = text);
        grid.Columns.Add(valueColumn);
        grid.Rows.Add("Threshold", "Literal", "0.85");
        grid.Rows.Add("Image", "Binding", "Acquire.Image");
        grid.Rows.Add("Retries", "Default", "3");
        grid.CurrentCell = grid.Rows[1].Cells[readOnly ? 0 : 1];
        return grid;
    }

    private ModernListView CreateListView(bool checkBoxes)
    {
        var list = new ModernListView
        {
            Width = 420,
            Height = 160,
            CheckBoxes = checkBoxes
        };
        BindGalleryText(checkBoxes ? "ListDiagnosticsCheckedAccessible" : "ListDiagnosticsAccessible", text => list.AccessibleName = text);
        var level = list.Columns.Add(string.Empty, 80);
        var code = list.Columns.Add(string.Empty, 90);
        var message = list.Columns.Add(string.Empty, 230);
        BindGalleryText("ListLevel", text => level.Text = text);
        BindGalleryText("ListCode", text => code.Text = text);
        BindGalleryText("ListMessage", text => message.Text = text);
        var error = new ListViewItem([string.Empty, "WF001", string.Empty]) { ForeColor = Color.FromArgb(255, 77, 79) };
        var warning = new ListViewItem([string.Empty, "WF014", string.Empty])
        {
            ForeColor = Color.FromArgb(216, 150, 20),
            Checked = checkBoxes
        };
        var information = new ListViewItem([string.Empty, "WF020", string.Empty]);
        BindGalleryText("SeverityError", text => error.SubItems[0].Text = text);
        BindGalleryText("SeverityWarning", text => warning.SubItems[0].Text = text);
        BindGalleryText("SeverityInformation", text => information.SubItems[0].Text = text);
        BindGalleryText("DiagnosticEntryDisconnected", text => error.SubItems[2].Text = text);
        BindGalleryText("DiagnosticOutputUnused", text => warning.SubItems[2].Text = text);
        BindGalleryText("DiagnosticValidationComplete", text => information.SubItems[2].Text = text);
        list.Items.Add(error);
        list.Items.Add(warning);
        list.Items.Add(information);
        list.Items[1].Selected = true;
        return list;
    }

    private ModernListBox CreateListBox(bool multiple, bool enabled = true)
    {
        var list = new ModernListBox
        {
            Width = 280,
            Height = 150,
            SelectionMode = multiple ? SelectionMode.MultiExtended : SelectionMode.One,
            Enabled = enabled
        };
        BindGalleryText(multiple ? "ListBoxMultipleAccessible" : "ListBoxAccessible", text => list.AccessibleName = text);
        list.Items.AddRange(["System", "System.Collections.Generic", "System.Linq", "DP.WorkFlow.Runtime"]);
        if (multiple)
        {
            list.SetSelected(0, true);
            list.SetSelected(2, true);
        }
        else
        {
            list.SelectedIndex = 1;
        }
        return list;
    }

    private ModernTreeView CreateTree(bool checkBoxes)
    {
        var tree = new ModernTreeView
        {
            Width = 410,
            Height = 190,
            CheckBoxes = checkBoxes
        };
        BindGalleryText(checkBoxes ? "TreeCheckedAccessible" : "TreeAccessible", text => tree.AccessibleName = text);
        var vision = tree.Nodes.Add(string.Empty);
        var acquire = vision.Nodes.Add(string.Empty);
        var locate = vision.Nodes.Add(string.Empty);
        var circle = locate.Nodes.Add(string.Empty);
        var template = locate.Nodes.Add(string.Empty);
        var logic = tree.Nodes.Add(string.Empty);
        var condition = logic.Nodes.Add(string.Empty);
        BindGalleryText("TreeVision", text => vision.Text = text);
        BindGalleryText("TreeAcquire", text => acquire.Text = text);
        BindGalleryText("TreeLocate", text => locate.Text = text);
        BindGalleryText("TreeCircle", text => circle.Text = text);
        BindGalleryText("TreeTemplate", text => template.Text = text);
        BindGalleryText("TreeLogic", text => logic.Text = text);
        BindGalleryText("TreeCondition", text => condition.Text = text);
        vision.Expand();
        locate.Expand();
        tree.SelectedNode = locate.Nodes[0];
        if (checkBoxes)
        {
            vision.Nodes[0].Checked = true;
            locate.Nodes[1].Checked = true;
        }
        return tree;
    }

    private ModernTabControl CreateTabs()
    {
        var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
        static void AddIcon(ImageList target, string key, ModernIconKind icon) =>
            target.Images.Add(key, ModernIcons.CreateBitmap(icon, Color.FromArgb(22, 119, 255)));
        AddIcon(images, "diagnostics", ModernIconKind.Info);
        AddIcon(images, "runtime", ModernIconKind.Play);
        AddIcon(images, "tokens", ModernIconKind.Key);
        AddIcon(images, "trace", ModernIconKind.Search);
        var tabs = new ModernTabControl { Width = 880, Height = 58, ImageList = images };
        tabs.Disposed += (_, _) => images.Dispose();
        var diagnostics = new TabPage { ImageKey = "diagnostics" };
        var runtime = new TabPage { ImageKey = "runtime" };
        var tokens = new TabPage { ImageKey = "tokens" };
        var trace = new TabPage { ImageKey = "trace" };
        BindGalleryText("TabDiagnostics", text => diagnostics.Text = text);
        BindGalleryText("TabRuntime", text => runtime.Text = text);
        BindGalleryText("TabTokens", text => tokens.Text = text);
        BindGalleryText("TabTrace", text => trace.Text = text);
        tabs.TabPages.Add(diagnostics);
        tabs.TabPages.Add(runtime);
        tabs.TabPages.Add(tokens);
        tabs.TabPages.Add(trace);
        tabs.SelectedIndex = 1;
        return tabs;
    }
}
