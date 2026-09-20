using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using ModernUI.Localization;
using ModernUI.WinForms;

namespace ModernPropertyGrid.WinForms;

public sealed partial class ModernPropertyGrid
{
    private void WireSelection(Control root, Control row, PropertyDescriptor property)
    {
        void Select() => SelectRow((PropertyRowPanel)row, property);
        void RefreshHover(bool entered)
        {
            if (row is not PropertyRowPanel propertyRow) return;
            if (entered)
            {
                propertyRow.Hovered = true;
                return;
            }
            if (!propertyRow.IsHandleCreated) { propertyRow.Hovered = false; return; }
            propertyRow.BeginInvoke(() =>
            {
                if (!propertyRow.IsDisposed)
                    propertyRow.Hovered = propertyRow.ClientRectangle.Contains(propertyRow.PointToClient(Cursor.Position));
            });
        }
        root.MouseDown += (_, _) => Select();
        root.Enter += (_, _) => Select();
        root.MouseEnter += (_, _) => RefreshHover(true);
        root.MouseLeave += (_, _) => RefreshHover(false);
        foreach (Control child in root.Controls) WireSelection(child, row, property);
    }

    private void SelectRow(PropertyRowPanel row, PropertyDescriptor property)
    {
        if (_selectedRow is PropertyRowPanel previous && !ReferenceEquals(previous, row)) previous.Selected = false;
        _selectedRow = row;
        row.Selected = true;
        _selectedPropertyKey = Presentation(property).PropertyKey;
        ShowDetails(property);
    }

    private void ShowDetails(PropertyDescriptor property)
    {
        var presentation = Presentation(property);
        _details.ForeColor = _theme.TextSecondary;
        _details.Text = T(PropertyGridTextKeys.DetailsValue, new Dictionary<string, object?>
        {
            ["property"] = presentation.DisplayName,
            ["type"] = property.PropertyType.Name,
            ["description"] = string.IsNullOrWhiteSpace(presentation.Description)
                ? T(PropertyGridTextKeys.DetailsNoDescription) : presentation.Description,
            ["name"] = property.Name
        });
    }

    private Label CreateHint(string text) => new()
    {
        Text = text,
        Height = ScaleLogical(82),
        Padding = new Padding(ScaleLogical(12)),
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = _theme.TextSecondary
    };
}
