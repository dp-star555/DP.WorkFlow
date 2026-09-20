using System.ComponentModel;
using System.Drawing;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ModernUI.Localization;
using System.Windows.Forms;
using ModernUI.WinForms;
using DP.WorkFlow.UI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class ModernControlBehaviorTests
{
    [Fact]
    public void ModernButton_AccessibleDefaultActionInvokesClick()
    {
        RunInSta(() =>
        {
            using var button = new ModernButton { Text = "Compile" };
            var clicks = 0;
            button.Click += (_, _) => clicks++;

            Assert.Equal(AccessibleRole.PushButton, button.AccessibilityObject.Role);
            Assert.Equal("Press", button.AccessibilityObject.DefaultAction);
            button.AccessibilityObject.DoDefaultAction();

            Assert.Equal(1, clicks);
        });
    }

    [Theory]
    [InlineData(typeof(ModernInput), nameof(ModernInput.Text))]
    [InlineData(typeof(ModernMaskedInput), nameof(ModernMaskedInput.Text))]
    [InlineData(typeof(ModernRichTextBox), nameof(ModernRichTextBox.Text))]
    [InlineData(typeof(ModernInputNumber), nameof(ModernInputNumber.Value))]
    [InlineData(typeof(ModernComboBox), nameof(ModernComboBox.Text))]
    [InlineData(typeof(ModernSelect), nameof(ModernSelect.SelectedValue))]
    [InlineData(typeof(ModernSwitch), nameof(ModernSwitch.Checked))]
    [InlineData(typeof(ModernSlider), nameof(ModernSlider.Value))]
    [InlineData(typeof(ModernDatePicker), nameof(ModernDatePicker.Value))]
    [InlineData(typeof(ModernTimePicker), nameof(ModernTimePicker.Value))]
    [InlineData(typeof(ModernDurationInput), nameof(ModernDurationInput.Value))]
    public void FoundationEditorsExposeBindableDefaultValueMetadata(Type controlType, string propertyName)
    {
        RunInSta(() =>
        {
            using var control = Assert.IsAssignableFrom<Control>(Activator.CreateInstance(controlType));
            var defaultBinding = Assert.IsType<DefaultBindingPropertyAttribute>(
                TypeDescriptor.GetAttributes(control)[typeof(DefaultBindingPropertyAttribute)]);
            var property = Assert.IsAssignableFrom<PropertyDescriptor>(TypeDescriptor.GetProperties(control)[propertyName]);

            Assert.Equal(propertyName, defaultBinding.Name);
            var bindable = Assert.IsType<BindableAttribute>(property.Attributes[typeof(BindableAttribute)]);
            Assert.True(bindable.Bindable, $"{controlType.Name}.{propertyName} must be bindable.");
        });
    }

    [Theory]
    [InlineData(typeof(ModernComboBox), nameof(ModernComboBox.SelectedValue))]
    [InlineData(typeof(ModernDateRangePicker), nameof(ModernDateRangePicker.StartDate))]
    [InlineData(typeof(ModernDateRangePicker), nameof(ModernDateRangePicker.EndDate))]
    public void FoundationEditorAlternativeValuesExposeBindingMetadata(Type controlType, string propertyName)
    {
        RunInSta(() =>
        {
            using var control = Assert.IsAssignableFrom<Control>(Activator.CreateInstance(controlType));
            var property = Assert.IsAssignableFrom<PropertyDescriptor>(TypeDescriptor.GetProperties(control)[propertyName]);
            var bindable = Assert.IsType<BindableAttribute>(property.Attributes[typeof(BindableAttribute)]);
            Assert.True(bindable.Bindable, $"{controlType.Name}.{propertyName} must be bindable.");
        });
    }

    [Fact]
    public void FoundationAlternativeBindableValuesNotifyWinFormsPropertyDescriptors()
    {
        RunInSta(() =>
        {
            using var combo = new ModernComboBox { ValueMember = nameof(BoundChoice.Id) };
            using var select = new ModernSelect { ValueMember = nameof(BoundChoice.Id) };
            using var range = new ModernDateRangePicker();
            var options = new object[] { new BoundChoice(1, "One"), new BoundChoice(2, "Two") };
            combo.Items.AddRange(options);
            select.Items.AddRange(options);
            var comboChanges = 0;
            var selectChanges = 0;
            var startChanges = 0;
            var endChanges = 0;
            TypeDescriptor.GetProperties(combo)[nameof(ModernComboBox.SelectedValue)]!
                .AddValueChanged(combo, (_, _) => comboChanges++);
            TypeDescriptor.GetProperties(select)[nameof(ModernSelect.SelectedValue)]!
                .AddValueChanged(select, (_, _) => selectChanges++);
            TypeDescriptor.GetProperties(range)[nameof(ModernDateRangePicker.StartDate)]!
                .AddValueChanged(range, (_, _) => startChanges++);
            TypeDescriptor.GetProperties(range)[nameof(ModernDateRangePicker.EndDate)]!
                .AddValueChanged(range, (_, _) => endChanges++);

            combo.SelectedIndex = 1;
            select.SelectedIndex = 1;
            range.StartDate = new DateTime(2026, 8, 10);
            range.EndDate = new DateTime(2026, 8, 12);

            Assert.Equal((1, 1, 1, 1), (comboChanges, selectChanges, startChanges, endChanges));
        });
    }

    [Fact]
    public void FoundationEditors_ReadOnlyBlocksUserChangesButAllowsProgrammaticUpdates()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(640, 180) };
            using var number = new ModernInputNumber { Bounds = new Rectangle(10, 10, 180, 34), Value = 5, ReadOnly = true };
            using var slider = new ModernSlider { Bounds = new Rectangle(10, 55, 220, 32), Value = 25, ReadOnly = true };
            using var toggle = new ModernSwitch { Bounds = new Rectangle(10, 100, 58, 28), Checked = true, ReadOnly = true };
            using var combo = new ModernComboBox { Bounds = new Rectangle(250, 10, 180, 34), ReadOnly = true };
            using var select = new ModernSelect { Bounds = new Rectangle(250, 55, 180, 34), ReadOnly = true };
            using var multiple = new ModernSelectMultiple { Bounds = new Rectangle(250, 100, 260, 34), ReadOnly = true };
            combo.Items.AddRange(["A", "B"]);
            select.Items.AddRange(["A", "B"]);
            multiple.Items.AddRange(["A", "B"]);
            host.Controls.AddRange([number, slider, toggle, combo, select, multiple]);
            host.Show();

            number.Controls.OfType<ModernButton>().Single(button => button.AccessibleName == "增加").PerformClick();
            slider.Focus();
            SendKey(slider, Keys.Right);
            toggle.Focus();
            SendKey(toggle, Keys.Space);

            Assert.Equal(5, number.Value);
            Assert.Equal(25, slider.Value);
            Assert.True(toggle.Checked);
            number.Value = 7;
            slider.Value = 30;
            toggle.Checked = false;
            combo.SelectedIndex = 1;
            select.SelectedIndex = 1;
            multiple.SetItemChecked(1, true);
            Assert.Equal(7, number.Value);
            Assert.Equal(30, slider.Value);
            Assert.False(toggle.Checked);
            Assert.Equal(1, combo.SelectedIndex);
            Assert.Equal(1, select.SelectedIndex);
            Assert.Single(multiple.SelectedItems);
        });
    }

    [Fact]
    public void TimePickerSpinnerRaisesOneCommitForOneUserStep()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 90) };
            using var picker = new ModernTimePicker
            {
                Bounds = new Rectangle(10, 10, 180, 34),
                Value = new TimeSpan(10, 20, 30)
            };
            var commits = 0;
            picker.ValueCommitted += (_, _) => commits++;
            host.Controls.Add(picker);
            host.Show();
            PumpMessages();

            var spinner = picker.Controls.Cast<Control>().Single(control =>
                control.GetType().Name == "ModernTimeSpinnerSurface");
            ClickControl(spinner, new Point(spinner.Width / 2, spinner.Height / 4));

            Assert.Equal(1, commits);
        });
    }

    [Fact]
    public void DatePickerKeyboardCommitRespectsReadOnlyAndProgrammaticAssignments()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 90) };
            using var picker = new ModernDatePicker
            {
                Bounds = new Rectangle(10, 10, 180, 34),
                Value = new DateTime(2026, 8, 10)
            };
            var commits = 0;
            picker.ValueCommitted += (_, _) => commits++;
            host.Controls.Add(picker);
            host.Show();

            picker.Value = new DateTime(2026, 8, 11);
            Assert.Equal(0, commits);
            picker.InnerPicker.Focus();
            PostKey(picker.InnerPicker, Keys.Up);
            Assert.Equal(1, commits);

            picker.ReadOnly = true;
            var readOnlyValue = picker.Value;
            PostKey(picker.InnerPicker, Keys.Up);
            Assert.Equal(readOnlyValue, picker.Value);
            Assert.Equal(1, commits);
        });
    }

    [Fact]
    public void ComboBoxReadOnlyBlocksCollapsedKeyboardAndWheelSelectionButAllowsProgrammaticSelection()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 90) };
            using var combo = new ModernComboBox
            {
                Bounds = new Rectangle(10, 10, 200, 34),
                ReadOnly = true
            };
            combo.Items.AddRange(["A", "B", "C"]);
            combo.SelectedIndex = 0;
            host.Controls.Add(combo);
            host.Show();
            combo.InnerComboBox.Focus();

            PostKey(combo.InnerComboBox, Keys.Down);
            PostMouseWheel(combo.InnerComboBox, -120);
            Assert.Equal(0, combo.SelectedIndex);

            combo.SelectedIndex = 2;
            Assert.Equal(2, combo.SelectedIndex);
        });
    }

    [Fact]
    public void FoundationEditors_CommittedEventsOnlyRepresentUserTransactions()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 120) };
            using var input = new ModernInput { Bounds = new Rectangle(10, 10, 180, 34) };
            using var number = new ModernInputNumber { Bounds = new Rectangle(210, 10, 180, 34) };
            using var toggle = new ModernSwitch { Bounds = new Rectangle(10, 60, 58, 28) };
            var textCommits = 0;
            var numberCommits = 0;
            var checkedCommits = 0;
            input.TextCommitted += (_, _) => textCommits++;
            number.ValueCommitted += (_, _) => numberCommits++;
            toggle.CheckedCommitted += (_, _) => checkedCommits++;
            host.Controls.AddRange([input, number, toggle]);
            host.Show();

            input.Text = "program";
            number.Value = 12;
            toggle.Checked = true;
            Assert.Equal((0, 0, 0), (textCommits, numberCommits, checkedCommits));

            input.InnerTextBox.Focus();
            input.InnerTextBox.Text = "user";
            SendKey(input.InnerTextBox, Keys.Enter);
            var numberText = number.Controls.OfType<ModernInput>().Single().InnerTextBox;
            numberText.Focus();
            numberText.Text = "15";
            SendKey(numberText, Keys.Enter);
            toggle.Focus();
            PostKey(toggle, Keys.Space);

            Assert.Equal((1, 1, 1), (textCommits, numberCommits, checkedCommits));
        });
    }

    [Fact]
    public void FirstUserCommitIsNotSuppressedByAnEmptyOrNullSentinel()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(460, 90) };
            using var input = new ModernInput { Bounds = new Rectangle(10, 10, 180, 34), Text = "program" };
            using var number = new ModernInputNumber
            {
                Bounds = new Rectangle(220, 10, 180, 34),
                AllowNull = true,
                NullableValue = null
            };
            var textCommits = 0;
            var numberCommits = 0;
            input.TextCommitted += (_, _) => textCommits++;
            number.ValueCommitted += (_, _) => numberCommits++;
            host.Controls.AddRange([input, number]);
            host.Show();

            input.InnerTextBox.Focus();
            input.InnerTextBox.Text = string.Empty;
            SendKey(input.InnerTextBox, Keys.Enter);

            var numberText = number.Controls.OfType<ModernInput>().Single().InnerTextBox;
            numberText.Focus();
            numberText.Text = "1";
            numberText.Text = string.Empty;
            SendKey(numberText, Keys.Enter);

            Assert.Equal((1, 1), (textCommits, numberCommits));
        });
    }

    [Fact]
    public void RichTextBoxKeepsReadableFontScaleAfterHandleCreationAt96Dpi()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 180), Font = new Font("Microsoft YaHei UI", 9F) };
            using var rich = new ModernRichTextBox
            {
                Bounds = new Rectangle(12, 12, 360, 120),
                Text = "维护记录\r\n支持 RTF、链接、选择和撤销。"
            };
            host.Controls.Add(rich);
            host.Show();
            PumpMessages();

            Assert.Equal(host.Font.SizeInPoints, rich.InnerTextBox.Font.SizeInPoints, 2);
            Assert.Equal(host.Font.Height, rich.InnerTextBox.Font.Height);
            rich.SelectAll();
            Assert.NotNull(rich.SelectionFont);
            Assert.Equal(host.Font.SizeInPoints, rich.SelectionFont!.SizeInPoints, 2);
            rich.Select(0, 0);
            var characterPosition = rich.InnerTextBox.GetPositionFromCharIndex(1);
            Assert.True(characterPosition.X >= 5,
                $"Rich text glyph advance is unexpectedly small: x={characterPosition.X}, font={rich.InnerTextBox.Font}.");
        });
    }

    [Fact]
    public void NativeTextEditorsCommitOnlyFocusedValidUserTransactions()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(560, 250) };
            using var masked = new ModernMaskedInput { Bounds = new Rectangle(10, 10, 180, 34), Mask = "000" };
            using var rich = new ModernRichTextBox { Bounds = new Rectangle(10, 60, 300, 100) };
            using var combo = new ModernComboBox { Bounds = new Rectangle(220, 10, 220, 34) };
            using var sink = new Button { Bounds = new Rectangle(330, 80, 100, 30), Text = "Focus" };
            combo.Items.AddRange(["A", "B"]);
            var maskedCommits = 0;
            var richCommits = 0;
            var comboCommits = 0;
            masked.TextCommitted += (_, _) => maskedCommits++;
            rich.TextCommitted += (_, _) => richCommits++;
            combo.TextCommitted += (_, _) => comboCommits++;
            host.Controls.AddRange([masked, rich, combo, sink]);
            host.Show();

            masked.Text = "111";
            rich.Text = "program";
            combo.Text = "program";
            Assert.Equal((0, 0, 0), (maskedCommits, richCommits, comboCommits));

            masked.InnerTextBox.Focus();
            masked.InnerTextBox.Text = "12";
            SendKey(masked.InnerTextBox, Keys.Enter);
            Assert.Equal(0, maskedCommits);
            masked.InnerTextBox.Text = "123";
            SendKey(masked.InnerTextBox, Keys.Enter);

            rich.InnerTextBox.Focus();
            rich.InnerTextBox.Text = "user rich text";
            sink.Focus();
            PumpMessages();

            combo.InnerComboBox.Focus();
            combo.InnerComboBox.Text = "custom value";
            SendKey(combo.InnerComboBox, Keys.Enter);

            Assert.Equal((1, 1, 1), (maskedCommits, richCommits, comboCommits));
        });
    }

    [Fact]
    public void InputNumber_FormatsNullableValuesAndRejectsOutOfRangeWhenConfigured()
    {
        RunInSta(() =>
        {
            using var number = new ModernInputNumber
            {
                AllowNull = true,
                NullableValue = null,
                FormatString = "N3",
                PrefixText = "≈ ",
                SuffixText = " mm"
            };
            Assert.Null(number.NullableValue);
            Assert.Equal(string.Empty, number.Controls.OfType<ModernInput>().Single().Text);

            number.NullableValue = 1234.5m;
            Assert.Equal("≈ 1,234.500 mm", number.Controls.OfType<ModernInput>().Single().Text);
            number.RangeValueBehavior = ModernRangeValueBehavior.Reject;
            number.Minimum = 0;
            number.Maximum = 10;
            Assert.Throws<ArgumentOutOfRangeException>(() => number.Value = 11);
        });
    }

    [Fact]
    public void TreeView_CheckPropagationModeCanKeepNodeChecksIndependent()
    {
        RunInSta(() =>
        {
            using var tree = new ModernTreeView
            {
                CheckBoxes = true,
                CheckPropagationMode = ModernTreeCheckPropagationMode.Independent
            };
            var parent = tree.Nodes.Add("Parent");
            var child = parent.Nodes.Add("Child");

            parent.Checked = true;

            Assert.True(parent.Checked);
            Assert.False(child.Checked);
            Assert.Equal(CheckState.Checked, tree.GetNodeCheckState(parent));
        });
    }

    [Fact]
    public void Button_CustomImageRendersWithoutTransferringImageOwnership()
    {
        RunInSta(() =>
        {
            using var image = new Bitmap(12, 12);
            using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Magenta);
            using (var button = new ModernButton { Size = new Size(120, 34), Text = "Image", Image = image })
            using (var bitmap = new Bitmap(button.Width, button.Height))
            {
                button.DrawToBitmap(bitmap, button.ClientRectangle);
                Assert.Contains(Pixels(bitmap, button.ClientRectangle), color => color.ToArgb() == Color.Magenta.ToArgb());
            }
            Assert.Equal(12, image.Width);
        });
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataControlsExposeHorizontalModernScrollChromeForWideContent(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(240, 200) };
            using Control control = controlKind switch
            {
                "ListBox" => CreateWideListBox(),
                "ListView" => CreateWideListView(),
                "TreeView" => CreateWideTreeView(),
                _ => CreateWideGrid()
            };
            control.Bounds = new Rectangle(10, 10, 190, 160);
            host.Controls.Add(control);
            host.Show();
            Application.DoEvents();

            Assert.Contains(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
        });

        static ModernListBox CreateWideListBox()
        {
            var control = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 520 };
            control.Items.Add("A very long list box item that must scroll horizontally");
            return control;
        }
        static ModernListView CreateWideListView()
        {
            var control = new ModernListView();
            control.Columns.Add("Wide", 520);
            control.Items.Add("A very long list view item");
            return control;
        }
        static ModernTreeView CreateWideTreeView()
        {
            var control = new ModernTreeView();
            control.Nodes.Add("A very long tree node that must scroll horizontally");
            return control;
        }
        static ModernDataGridView CreateWideGrid()
        {
            var control = new ModernDataGridView { AllowUserToAddRows = false };
            control.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wide", Width = 520 });
            control.Rows.Add("A very long grid value");
            return control;
        }
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataControlHorizontalChromeScrollsTheRetainedNativeContent(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 210) };
            using Control control = controlKind switch
            {
                "ListBox" => CreateScrollableListBox(),
                "ListView" => CreateScrollableListView(),
                "TreeView" => CreateScrollableTreeView(),
                _ => CreateScrollableGrid()
            };
            control.Bounds = new Rectangle(10, 10, 200, 160);
            host.Controls.Add(control);
            host.Show();
            PumpMessages();

            var horizontal = Assert.Single(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            var before = GetPosition(control);
            var thumb = FindScrollThumbCenter(horizontal, control.BackColor);
            var destination = new Point(Math.Min(horizontal.Width - 8, thumb.X + 70), thumb.Y);
            SendMouseMessage(horizontal, 0x0201, horizontal.PointToScreen(thumb), 1);
            SendMouseMessage(horizontal, 0x0200, horizontal.PointToScreen(destination), 1);
            SendMouseMessage(horizontal, 0x0202, horizontal.PointToScreen(destination), 0);
            PumpMessages();

            Assert.True(GetPosition(control) > before,
                $"{controlKind} retained native horizontal position did not advance.");
        });

        static int GetPosition(Control control) => control is ModernDataGridView grid
            ? grid.HorizontalScrollingOffset
            : GetScrollPos(control.Handle, 0);

        static ModernListBox CreateScrollableListBox()
        {
            var control = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 620 };
            control.Items.Add("A very long list box item that must scroll horizontally");
            return control;
        }
        static ModernListView CreateScrollableListView()
        {
            var control = new ModernListView();
            control.Columns.Add("Wide", 620);
            control.Items.Add("A very long list view item");
            return control;
        }
        static ModernTreeView CreateScrollableTreeView()
        {
            var control = new ModernTreeView();
            control.Nodes.Add(new string('W', 120));
            return control;
        }
        static ModernDataGridView CreateScrollableGrid()
        {
            var control = new ModernDataGridView { AllowUserToAddRows = false };
            control.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wide", Width = 620 });
            control.Rows.Add("A very long grid value");
            return control;
        }
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataHorizontalChromeStaysAtTheFinalBottomEdgeDuringRepeatedResize(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 360) };
            using Control control = CreateWideControl(controlKind);
            control.Bounds = new Rectangle(10, 10, 230, 220);
            host.Controls.Add(control);
            host.Show();
            PumpMessages();

            foreach (var size in new[] { new Size(345, 330), new Size(230, 220), new Size(300, 285) })
            {
                control.Size = size;
                AssertHorizontalChromeAtBottom(control);
            }

            control.Size = new Size(230, 220);
            control.Scale(new SizeF(1.5f, 1.5f));
            AssertHorizontalChromeAtBottom(control);

            // The Gallery DPI transaction resumes layout after the child controls have scaled.
            // A manually positioned overlay must not be restored to its pre-scale Top anchor.
            control.PerformLayout();
            AssertHorizontalChromeAtBottom(control);
        });

        static void AssertHorizontalChromeAtBottom(Control control)
        {
            var horizontal = Assert.Single(control.Controls.Cast<Control>(), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            Assert.Equal(control.ClientSize.Height, horizontal.Bottom);
            Assert.Equal(Math.Max(0, control.ClientSize.Height - horizontal.Height), horizontal.Top);
        }

        static Control CreateWideControl(string kind)
        {
            if (kind == "ListBox")
            {
                var list = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 520 };
                list.Items.AddRange(Enumerable.Range(1, 16)
                    .Select(index => (object)$"最近任务 {index:000} · Inspection Camera / Recipe-A").ToArray());
                return list;
            }
            if (kind == "ListView")
            {
                var list = new ModernListView();
                list.Columns.Add("Wide", 520);
                list.Items.AddRange(Enumerable.Range(1, 16)
                    .Select(index => new ListViewItem($"最近任务 {index:000}")).ToArray());
                return list;
            }
            if (kind == "TreeView")
            {
                var tree = new ModernTreeView();
                tree.Nodes.Add(new string('W', 120));
                return tree;
            }
            var grid = new ModernDataGridView { AllowUserToAddRows = false };
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wide", Width = 520 });
            grid.Rows.Add("Wide row");
            return grid;
        }
    }

    [Fact]
    public void ListBoxVerticalThumbDragKeepsHorizontalChromeAtTheBottom()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 360) };
            using var list = new ModernListBox
            {
                Bounds = new Rectangle(10, 10, 345, 330),
                HorizontalScrollbar = true,
                HorizontalExtent = 650
            };
            list.Items.AddRange(Enumerable.Range(1, 16)
                .Select(index => (object)$"最近任务 {index:000} · Inspection Camera / Recipe-A").ToArray());
            host.Controls.Add(list);
            host.Show();
            PumpMessages();

            var vertical = Assert.Single(list.Controls.Cast<Control>(), child =>
                child.Cursor == Cursors.SizeNS && child.Visible);
            var horizontal = Assert.Single(list.Controls.Cast<Control>(), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            var thumb = FindScrollThumbCenter(vertical, list.BackColor);
            var destination = new Point(thumb.X, Math.Min(vertical.Height - 8, thumb.Y + 100));

            SendMouseMessage(vertical, 0x0201, vertical.PointToScreen(thumb), 1);
            SendMouseMessage(vertical, 0x0200, vertical.PointToScreen(destination), 1);

            Assert.True(list.TopIndex > 0);
            Assert.Equal(list.ClientSize.Height, horizontal.Bottom);

            SendMouseMessage(vertical, 0x0202, vertical.PointToScreen(destination), 0);

            list.TopIndex = 2;
            Assert.Equal(2, list.TopIndex);
            Assert.Equal(list.ClientSize.Height, horizontal.Bottom);

            list.TopIndex = 0;
            list.SelectedIndex = 0;
            list.Focus();
            for (var page = 0; page < 2; page++)
            {
                SendMessage(list.Handle, 0x0100, (IntPtr)Keys.PageDown, IntPtr.Zero);
                SendMessage(list.Handle, 0x0101, (IntPtr)Keys.PageDown, IntPtr.Zero);
            }
            Assert.True(list.TopIndex > 0);
            Assert.Equal(list.ClientSize.Height, horizontal.Bottom);
        });
    }

    [Fact]
    public void ListBoxManagedHorizontalChromeNeverExposesTheNativeBar()
    {
        RunInSta(() =>
        {
            const int horizontalScrollStyle = 0x00100000;
            using var host = new Form { ClientSize = new Size(320, 280) };
            using var list = new ModernListBox
            {
                Bounds = new Rectangle(20, 20, 230, 220),
                HorizontalScrollbar = true,
                HorizontalExtent = 650
            };
            list.Items.AddRange(Enumerable.Range(1, 16)
                .Select(index => (object)$"任务 {index:000} · Inspection Camera / Recipe-A").ToArray());
            host.Controls.Add(list);
            host.Show();
            PumpMessages();

            AssertNativeBarHidden("initial");
            list.HorizontalExtent = 720;
            AssertNativeBarHidden("HorizontalExtent immediate");
            PumpMessages();
            AssertNativeBarHidden("HorizontalExtent final");
            list.Items.Add("任务 017 · Inspection Camera / Recipe-A");
            AssertNativeBarHidden("item add immediate");
            list.Size = new Size(260, 230);
            AssertNativeBarHidden("resize immediate");
            list.TopIndex = 3;
            AssertNativeBarHidden("TopIndex");

            var vertical = Assert.Single(list.Controls.Cast<Control>(), child =>
                child.Cursor == Cursors.SizeNS && child.Visible);
            var thumb = FindScrollThumbCenter(vertical, list.BackColor);
            var destination = new Point(thumb.X, Math.Min(vertical.Height - 8, thumb.Y + 60));
            SendMouseMessage(vertical, 0x0201, vertical.PointToScreen(thumb), 1);
            SendMouseMessage(vertical, 0x0200, vertical.PointToScreen(destination), 1);
            AssertNativeBarHidden("vertical drag");
            SendMouseMessage(vertical, 0x0202, vertical.PointToScreen(destination), 0);

            SetWindowLong(list.Handle, -16, GetWindowLong(list.Handle, -16) | horizontalScrollStyle);
            AssertNativeBarHidden("external style change");

            void AssertNativeBarHidden(string stage) => Assert.True(
                (GetWindowLong(list.Handle, -16) & horizontalScrollStyle) == 0,
                $"Native horizontal ListBox bar became visible during {stage}.");
        });
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("TreeView")]
    public void NativeDataControlHorizontalDragDoesNotToggleWholeWindowRedrawPerFrame(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 220) };
            using Control control = controlKind == "ListBox"
                ? CreateScrollableListBox()
                : CreateScrollableTreeView();
            control.Bounds = new Rectangle(10, 10, 260, 170);
            host.Controls.Add(control);
            host.Show();
            PumpMessages();

            var horizontal = Assert.Single(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            using var monitor = new WindowMessageMonitor(control.Handle, 0x000B, 0x0014);
            var thumb = FindScrollThumbCenter(horizontal, control.BackColor);
            SendMouseMessage(horizontal, 0x0201, horizontal.PointToScreen(thumb), 1);
            for (var step = 1; step <= 8; step++)
            {
                var destination = new Point(Math.Min(horizontal.Width - 8, thumb.X + step * 12), thumb.Y);
                SendMouseMessage(horizontal, 0x0200, horizontal.PointToScreen(destination), 1);
            }
            var end = new Point(Math.Min(horizontal.Width - 8, thumb.X + 96), thumb.Y);
            SendMouseMessage(horizontal, 0x0202, horizontal.PointToScreen(end), 0);
            PumpMessages();

            Assert.Equal(0, monitor.Count(0x000B));
            Assert.Equal(monitor.Count(0x0014), monitor.NonZeroResultCount(0x0014));
        });

        static ModernListBox CreateScrollableListBox()
        {
            var control = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 720 };
            control.Items.AddRange(Enumerable.Range(1, 12).Select(index => (object)$"Wide list item {index}").ToArray());
            return control;
        }

        static ModernTreeView CreateScrollableTreeView()
        {
            var control = new ModernTreeView();
            control.Nodes.AddRange(Enumerable.Range(1, 12)
                .Select(index => new TreeNode(new string('W', 140) + index)).ToArray());
            return control;
        }
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataControlScrollChromeMirrorsItsVerticalGutterInRightToLeft(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 210) };
            using Control control = CreateControl(controlKind);
            control.Bounds = new Rectangle(10, 10, 200, 160);
            control.RightToLeft = RightToLeft.Yes;
            host.Controls.Add(control);
            host.Show();
            PumpMessages();

            var vertical = Assert.Single(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeNS && child.Visible);
            var horizontal = Assert.Single(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            var verticalScreen = vertical.RectangleToScreen(vertical.ClientRectangle);
            var horizontalScreen = horizontal.RectangleToScreen(horizontal.ClientRectangle);

            Assert.Equal(control.RectangleToScreen(control.ClientRectangle).Left, verticalScreen.Left);
            Assert.True(horizontalScreen.Left >= verticalScreen.Right,
                $"{controlKind} horizontal chrome overlaps the mirrored vertical gutter.");
        });

        static Control CreateControl(string kind)
        {
            switch (kind)
            {
                case "ListBox":
                    var listBox = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 620 };
                    listBox.Items.AddRange(Enumerable.Range(1, 40).Select(index => (object)$"Wide item {index}").ToArray());
                    return listBox;
                case "ListView":
                    var listView = new ModernListView();
                    listView.Columns.Add("Wide", 620);
                    listView.Items.AddRange(Enumerable.Range(1, 40).Select(index => new ListViewItem($"Wide item {index}")).ToArray());
                    return listView;
                case "TreeView":
                    var tree = new ModernTreeView();
                    tree.Nodes.AddRange(Enumerable.Range(1, 40).Select(index => new TreeNode(new string('W', 100) + index)).ToArray());
                    return tree;
                default:
                    var grid = new ModernDataGridView { AllowUserToAddRows = false };
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wide", Width = 620 });
                    for (var index = 1; index <= 40; index++) grid.Rows.Add($"Wide item {index}");
                    return grid;
            }
        }
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataControlHorizontalThumbStartsAtTheMirroredEdgeInRightToLeft(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 210) };
            using Control control = controlKind switch
            {
                "ListBox" => CreateWideListBox(),
                "ListView" => CreateWideListView(),
                "TreeView" => CreateWideTreeView(),
                _ => CreateWideGrid()
            };
            control.Bounds = new Rectangle(10, 10, 200, 160);
            control.RightToLeft = RightToLeft.Yes;
            host.Controls.Add(control);
            host.Show();
            PumpMessages();

            var horizontal = Assert.Single(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);
            using var frame = new Bitmap(horizontal.Width, horizontal.Height);
            horizontal.DrawToBitmap(frame, horizontal.ClientRectangle);
            var thumbBounds = FindScrollThumbBounds(frame, frame.GetPixel(0, 0));
            Assert.True(thumbBounds.Left + thumbBounds.Width / 2 > horizontal.ClientSize.Width / 2,
                $"{controlKind} RTL zero-position thumb must start from the right edge: {thumbBounds}.");
        });

        static ModernListBox CreateWideListBox()
        {
            var control = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 620 };
            control.Items.Add("Wide item");
            return control;
        }
        static ModernListView CreateWideListView()
        {
            var control = new ModernListView();
            control.Columns.Add("Wide", 620);
            control.Items.Add("Wide item");
            return control;
        }
        static ModernTreeView CreateWideTreeView()
        {
            var control = new ModernTreeView();
            control.Nodes.Add(new string('W', 120));
            return control;
        }
        static ModernDataGridView CreateWideGrid()
        {
            var control = new ModernDataGridView { AllowUserToAddRows = false };
            control.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Wide", Width = 620 });
            control.Rows.Add("Wide item");
            return control;
        }
    }

    [Theory]
    [InlineData("ListBox")]
    [InlineData("ListView")]
    [InlineData("TreeView")]
    [InlineData("DataGridView")]
    public void NativeDataControlHorizontalChromeTracksRuntimeRangeChangesAndDisposesPendingLayout(string controlKind)
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 210) };
            var control = CreateNarrowControl(controlKind);
            control.Bounds = new Rectangle(10, 10, 200, 160);
            host.Controls.Add(control);
            host.Show();
            PumpMessages();
            Assert.DoesNotContain(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);

            MakeWide(control);
            PumpMessages();
            Assert.Contains(DescendantsAndSelf(control).Skip(1), child =>
                child.Cursor == Cursors.SizeWE && child.Visible);

            MakeNarrow(control);
            control.Dispose();
            Application.DoEvents();
            Assert.True(control.IsDisposed);
        });

        static Control CreateNarrowControl(string kind)
        {
            switch (kind)
            {
                case "ListBox":
                    var listBox = new ModernListBox { HorizontalScrollbar = true, HorizontalExtent = 100 };
                    listBox.Items.Add("Item");
                    return listBox;
                case "ListView":
                    var listView = new ModernListView();
                    listView.Columns.Add("Column", 100);
                    listView.Items.Add("Item");
                    return listView;
                case "TreeView":
                    var tree = new ModernTreeView();
                    tree.Nodes.Add("Item");
                    return tree;
                default:
                    var grid = new ModernDataGridView { AllowUserToAddRows = false };
                    grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Column", Width = 100 });
                    grid.Rows.Add("Item");
                    return grid;
            }
        }

        static void MakeWide(Control control)
        {
            switch (control)
            {
                case ModernListBox listBox: listBox.HorizontalExtent = 620; break;
                case ModernListView listView: listView.Columns[0].Width = 620; break;
                case ModernTreeView tree: tree.Nodes[0].Text = new string('W', 120); break;
                case ModernDataGridView grid: grid.Columns[0].Width = 620; break;
            }
        }

        static void MakeNarrow(Control control)
        {
            switch (control)
            {
                case ModernListBox listBox: listBox.HorizontalExtent = 100; break;
                case ModernListView listView: listView.Columns[0].Width = 100; break;
                case ModernTreeView tree: tree.Nodes[0].Text = "Item"; break;
                case ModernDataGridView grid: grid.Columns[0].Width = 100; break;
            }
        }
    }

    [Fact]
    public void CheckedListBoxUsesConfiguredRowDistributionAfterHandleCreation()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 240) };
            using var list = new ModernCheckedListBox
            {
                Bounds = new Rectangle(12, 12, 260, 180),
                RowHeight = 30
            };
            list.Items.AddRange(["相机在线", "PLC 握手", "安全门关闭", "机器人回零", "配方已下发"]);
            host.Controls.Add(list);
            host.Show();
            PumpMessages();

            Assert.Equal(30, list.RowHeight);
            var rows = Enumerable.Range(0, list.Items.Count).Select(list.GetItemRectangle).ToArray();
            Assert.All(rows, row => Assert.Equal(30, row.Height));
            Assert.All(rows.Zip(rows.Skip(1)), pair => Assert.Equal(pair.First.Bottom, pair.Second.Top));
        });
    }

    [Fact]
    public void CheckedListBoxDoesNotRepeatTheLastItemBelowItsMeasuredRows()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(340, 260) };
            using var list = new ModernCheckedListBox
            {
                Bounds = new Rectangle(12, 12, 280, 210),
                RowHeight = 30
            };
            list.Items.AddRange(["相机在线", "PLC 握手", "安全门关闭", "机器人回零", "配方已下发", "旧的第六行"]);
            host.Controls.Add(list);
            host.Show();
            PumpMessages();
            list.Items.RemoveAt(list.Items.Count - 1);
            list.Refresh();
            PumpMessages();

            var lastRow = list.GetItemRectangle(list.Items.Count - 1);
            using var frame = CaptureControl(list);
            var trailing = Rectangle.FromLTRB(0, lastRow.Bottom, frame.Width, frame.Height);
            Assert.Equal(0, CountNonBackgroundPixels(frame, trailing, list.BackColor, 20));
        });
    }

    [Fact]
    public void CheckedListBoxClickDoesNotEraseTheWholeBackgroundOrQueueASecondFullRepaint()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 220) };
            using var list = new ModernCheckedListBox { Bounds = new Rectangle(10, 10, 260, 170) };
            list.Items.AddRange(["相机在线", "PLC 握手", "安全门关闭", "机器人回零", "配方已下发"]);
            host.Controls.Add(list);
            host.Show();
            PumpMessages();

            var row = list.GetItemRectangle(2);
            var clickPoint = new Point(row.Left + 12, row.Top + row.Height / 2);
            Cursor.Position = list.PointToScreen(clickPoint);
            PumpMessages();
            using var monitor = new WindowMessageMonitor(list.Handle, 0x000F, 0x0014);
            ClickControl(list, clickPoint);
            PumpMessages();

            Assert.True(list.GetItemChecked(2));
            Assert.Equal(monitor.Count(0x0014), monitor.NonZeroResultCount(0x0014));
            Assert.InRange(monitor.Count(0x000F), 0, 1);
        });
    }

    [Fact]
    public void CheckedListBoxKeepsFocusAndChecksIndependentAndSeparatesUserCommit()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 180) };
            using var list = new ModernCheckedListBox { Bounds = new Rectangle(10, 10, 220, 130) };
            list.Items.AddRange(["A", "B", "C"]);
            list.SelectedIndex = 1;
            list.SetItemChecked(0, true);
            var commits = 0;
            list.CheckedItemsCommitted += (_, _) => commits++;
            host.Controls.Add(list);
            host.Show();
            PumpMessages();

            Assert.Equal(1, list.SelectedIndex);
            Assert.True(list.GetItemChecked(0));
            Assert.False(list.GetItemChecked(1));

            list.ReadOnly = true;
            var secondRow = list.GetItemRectangle(1);
            ClickControl(list, new Point(12, secondRow.Top + secondRow.Height / 2));
            PumpMessages();
            Assert.False(list.GetItemChecked(1));
            Assert.Equal(0, commits);

            list.SetItemChecked(1, true);
            Assert.True(list.GetItemChecked(1));
            Assert.Equal(0, commits);

            list.ReadOnly = false;
            var thirdRow = list.GetItemRectangle(2);
            ClickControl(list, new Point(12, thirdRow.Top + thirdRow.Height / 2));
            PumpMessages();
            Assert.True(list.GetItemChecked(2));
            Assert.Equal(1, commits);
        });
    }

    [Fact]
    public void ModernGroupBoxPaintsOpaqueChromeInsideTransparentContainer()
    {
        RunInSta(() =>
        {
            var background = Color.FromArgb(241, 243, 245);
            using var host = new Form { BackColor = background, ClientSize = new Size(440, 160) };
            using var transparentContainer = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            using var group = new ModernGroupBox { Bounds = new Rectangle(20, 20, 400, 120), Text = "Options" };
            transparentContainer.Controls.Add(group);
            host.Controls.Add(transparentContainer);
            host.Show();
            Application.DoEvents();

            using var bitmap = new Bitmap(group.Width, group.Height);
            group.DrawToBitmap(bitmap, group.ClientRectangle);

            Assert.Equal(background.ToArgb(), bitmap.GetPixel(group.Width / 2, 0).ToArgb());
            host.Close();
        });
    }

    [Fact]
    public void NewFoundationControlsPreserveNativeValueAndCollectionSemantics()
    {
        RunInSta(() =>
        {
            using var masked = new ModernMaskedInput { Mask = "000-000", Text = "123456" };
            using var rich = new ModernRichTextBox { Text = "line one" };
            using var checkedList = new ModernCheckedListBox();
            using var link = new ModernLinkLabel { Text = "Documentation" };
            using var group = new ModernGroupBox { Text = "Options" };
            using var menu = new ModernMenuStrip();
            using var toolStrip = new ModernToolStrip();
            checkedList.Items.AddRange(["A", "B"]);
            checkedList.SetItemChecked(1, true);
            menu.Items.Add("File");
            toolStrip.Items.Add("Run");

            Assert.True(masked.MaskCompleted);
            Assert.Equal("line one", rich.Text);
            Assert.Equal("B", checkedList.CheckedItems[0]);
            Assert.Equal(AccessibleRole.Link, link.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.Grouping, group.AccessibilityObject.Role);
            Assert.Single(menu.Items);
            Assert.Single(toolStrip.Items);
        });
    }

    [Fact]
    public void CompositeInputs_ScaledChildrenRemainInsideClientBounds()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput { Size = new Size(172, 34) };
            using var number = new ModernInputNumber { Size = new Size(188, 34) };

            input.Scale(new SizeF(1.5f, 1.5f));
            number.Scale(new SizeF(1.5f, 1.5f));

            Assert.True(input.ClientRectangle.Contains(input.InnerTextBox.Bounds));
            Assert.All(number.Controls.Cast<Control>(), child =>
                Assert.True(number.ClientRectangle.Contains(child.Bounds),
                    $"{child.GetType().Name} bounds {child.Bounds} exceed {number.ClientRectangle}."));
        });
    }

    [Fact]
    public void ArrowKeys_StepValueAndClampAtBoundaries()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(180, 48) };
            using var input = new ModernInputNumber
            {
                Minimum = 0,
                Maximum = 2,
                Increment = 1,
                Value = 1
            };
            host.Controls.Add(input);
            host.Show();
            host.Activate();
            var inner = InnerTextBoxOf(input);
            inner.Focus();
            Application.DoEvents();

            SendKey(inner, Keys.Up);
            Assert.Equal(2, input.Value);
            SendKey(inner, Keys.Up);
            Assert.Equal(2, input.Value);
            SendKey(inner, Keys.Down);
            Assert.Equal(1, input.Value);
            SendKey(inner, Keys.Down);
            SendKey(inner, Keys.Down);
            Assert.Equal(0, input.Value);

            host.Close();
        });
    }

    [Fact]
    public void StepButtons_SaturateWithoutDecimalOverflow()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber
            {
                Minimum = decimal.MinValue,
                Maximum = decimal.MaxValue,
                Increment = 2,
                Value = decimal.MaxValue - 1
            };
            var increase = input.Controls.OfType<ModernButton>().Single(button => button.AccessibleName == "增加");
            var decrease = input.Controls.OfType<ModernButton>().Single(button => button.AccessibleName == "减少");

            increase.PerformClick();
            Assert.Equal(decimal.MaxValue, input.Value);

            input.Value = decimal.MinValue + 1;
            decrease.PerformClick();
            Assert.Equal(decimal.MinValue, input.Value);
        });
    }    [Fact]
    public void SameValueCommit_NormalizesTextAndClearsValidation()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(180, 48) };
            using var input = new ModernInputNumber { DecimalPlaces = 2, Value = 5 };
            host.Controls.Add(input);
            host.Show();
            host.Activate();
            var inner = InnerTextBoxOf(input);
            inner.Text = "not-a-number";
            inner.Focus();
            Application.DoEvents();
            SendKey(inner, Keys.Enter);
            Assert.True(input.Controls.OfType<ModernInput>().Single().HasError);

            input.Value = 5;

            Assert.Equal("5.00", inner.Text);
            Assert.False(input.Controls.OfType<ModernInput>().Single().HasError);
            host.Close();
        });
    }

    [Fact]
    public void SetSelectedItems_MatchesItemIdentityInsteadOfDisplayText()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelectMultiple();
            var first = new SameLabelValue("first");
            var second = new SameLabelValue("second");
            select.Items.Add(first);
            select.Items.Add(second);

            select.SetSelectedItems([first]);

            var selected = Assert.Single(select.SelectedItems);
            Assert.Same(first, selected);
        });
    }
    [Fact]
    public void ModernInput_AccessibilityTreeExposesSingleNativeTextEditor()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(240, 60) };
            using var input = new ModernInput
            {
                AccessibleName = "设备名称",
                Text = "Camera 01",
                Width = 200
            };
            host.Controls.Add(input);
            host.Show();
            Application.DoEvents();

            var textNodes = EnumerateAccessibilityTree(input.AccessibilityObject)
                .Where(node => node.Role == AccessibleRole.Text)
                .ToArray();

            var editor = Assert.Single(textNodes);
            Assert.True(editor.GetChildCount() <= 0);
            Assert.Equal("设备名称", editor.Name);
            Assert.Equal("Camera 01", editor.Value);

            input.InnerTextBox.Focus();
            Application.DoEvents();
            Assert.True(input.InnerTextBox.Focused);
            Assert.Same(input.InnerTextBox.AccessibilityObject, editor);
            host.Close();
        });
    }

    [Fact]
    public void ModernInput_AccessibilityUsesNativeEditorProvider()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput
            {
                Text = "secret",
                ReadOnly = true,
                UseSystemPasswordChar = true
            };

            Assert.Same(input.InnerTextBox.AccessibilityObject, input.AccessibilityObject);
            Assert.Equal(AccessibleRole.Text, input.AccessibilityObject.Role);
        });
    }

    [Fact]
    public void ModernInput_CommonTextInterface_DelegatesToNativeEditor()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput
            {
                MaxLength = 32,
                CharacterCasing = CharacterCasing.Upper,
                TextAlign = HorizontalAlignment.Right,
                ShortcutsEnabled = false,
                Text = "Camera 01"
            };

            input.Select(0, 6);

            Assert.Equal(32, input.InnerTextBox.MaxLength);
            Assert.Equal(CharacterCasing.Upper, input.InnerTextBox.CharacterCasing);
            Assert.Equal(HorizontalAlignment.Right, input.InnerTextBox.TextAlign);
            Assert.False(input.InnerTextBox.ShortcutsEnabled);
            Assert.Equal("Camera", input.SelectedText);

            input.SelectedText = "Line";
            Assert.Equal("LINE 01", input.Text);
            input.SelectAll();
            Assert.Equal(input.Text.Length, input.SelectionLength);
            input.Clear();
            Assert.Empty(input.Text);
        });
    }

    [Fact]
    public void ModernTextArea_MultilineInterface_PreservesLineBreaksAndConfiguration()
    {
        RunInSta(() =>
        {
            using var textArea = new ModernTextArea
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Text = "first\r\nsecond"
            };

            Assert.Equal(new[] { "first", "second" }, textArea.Lines);
            Assert.True(textArea.AcceptsReturn);
            Assert.True(textArea.AcceptsTab);
            Assert.False(textArea.WordWrap);
            Assert.Equal(ScrollBars.Both, textArea.ScrollBars);
        });
    }

    [Fact]
    public void ModernTextArea_Defaults_PreserveFormNavigationAndAvoidPermanentScrollBars()
    {
        RunInSta(() =>
        {
            using var textArea = new ModernTextArea();

            Assert.True(textArea.AcceptsReturn);
            Assert.False(textArea.AcceptsTab);
            Assert.True(textArea.WordWrap);
            Assert.Equal(ScrollBars.None, textArea.ScrollBars);
        });
    }

    [Fact]
    public void ModernTextArea_TextChanged_RaisesOncePerChange()
    {
        RunInSta(() =>
        {
            using var textArea = new ModernTextArea();
            var changeCount = 0;
            textArea.TextChanged += (_, _) => changeCount++;

            textArea.Text = "updated";

            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void ModernTextArea_AccessibilityAndLayout_UseNativeEditorAcrossAvailableHeight()
    {
        RunInSta(() =>
        {
            using var textArea = new ModernTextArea
            {
                AccessibleName = "JSON 配置",
                Size = new Size(220, 120),
                Padding = new Padding(10)
            };

            textArea.PerformLayout();

            Assert.Same(textArea.InnerTextBox.AccessibilityObject, textArea.AccessibilityObject);
            Assert.Equal(AccessibleRole.Text, textArea.AccessibilityObject.Role);
            Assert.Equal("JSON 配置", textArea.AccessibilityObject.Name);
            Assert.Equal(new Rectangle(10, 10, 200, 100), textArea.InnerTextBox.Bounds);
        });
    }

    [Fact]
    public void ModernComboBox_EditableText_DoesNotRequireMatchingItem()
    {
        RunInSta(() =>
        {
            using var comboBox = new ModernComboBox();
            comboBox.Items.AddRange(["System", "System.Linq"]);
            var changeCount = 0;
            comboBox.TextChanged += (_, _) => changeCount++;

            comboBox.Text = "Company.Product";

            Assert.Equal("Company.Product", comboBox.Text);
            Assert.Equal(-1, comboBox.SelectedIndex);
            Assert.Equal(AutoCompleteMode.SuggestAppend, comboBox.AutoCompleteMode);
            Assert.Equal(AutoCompleteSource.ListItems, comboBox.AutoCompleteSource);
            Assert.Equal(1, changeCount);

            comboBox.Select(0, 7);
            Assert.Equal("Company", comboBox.SelectedText);
            comboBox.SelectedText = "Product";
            Assert.Equal("Product.Product", comboBox.Text);
        });
    }

    [Fact]
    public void ModernComboBox_DataBinding_ExposesDisplayTextAndSelectedValue()
    {
        RunInSta(() =>
        {
            using var comboBox = new ModernComboBox
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = new[]
                {
                    new BoundChoice(1, "System"),
                    new BoundChoice(2, "System.Linq")
                }
            };

            comboBox.SelectedValue = 2;

            Assert.Equal(1, comboBox.SelectedIndex);
            Assert.Equal("System.Linq", comboBox.Text);
            Assert.Equal(2, comboBox.SelectedValue);

            comboBox.SelectedValue = null;
            Assert.Equal(-1, comboBox.SelectedIndex);
        });
    }

    [Fact]
    public void ModernComboBox_Accessibility_UsesNativeEditableComboProvider()
    {
        RunInSta(() =>
        {
            using var comboBox = new ModernComboBox
            {
                AccessibleName = "命名空间",
                Text = "System.Linq"
            };

            Assert.Same(comboBox.InnerComboBox.AccessibilityObject, comboBox.AccessibilityObject);
            Assert.Equal(AccessibleRole.ComboBox, comboBox.AccessibilityObject.Role);
            Assert.Equal("命名空间", comboBox.AccessibilityObject.Name);
        });
    }

    [Fact]
    public void ModernComboBox_BindingListChanges_SynchronizeAndDetachOnNull()
    {
        RunInSta(() =>
        {
            var source = new BindingList<MutableBoundChoice>
            {
                new(1, "System"),
                new(2, "System.Linq")
            };
            using var comboBox = new ModernComboBox
            {
                DisplayMember = nameof(MutableBoundChoice.Name),
                ValueMember = nameof(MutableBoundChoice.Id),
                DataSource = source,
                SelectedValue = 2
            };

            source.RemoveAt(1);

            Assert.Single(comboBox.Items);
            Assert.Equal(-1, comboBox.SelectedIndex);

            comboBox.DataSource = null;
            source.Add(new MutableBoundChoice(3, "System.Text"));
            Assert.Empty(comboBox.Items);
        });
    }

    [Fact]
    public void ModernRadioButton_Checked_ExcludesPeersInSameGroup()
    {
        RunInSta(() =>
        {
            using var host = new Panel();
            using var first = new ModernRadioButton { Text = "Select", Checked = true };
            using var second = new ModernRadioButton { Text = "Rectangle" };
            host.Controls.Add(first);
            host.Controls.Add(second);
            var firstChanges = 0;
            var secondChanges = 0;
            first.CheckedChanged += (_, _) => firstChanges++;
            second.CheckedChanged += (_, _) => secondChanges++;

            second.Checked = true;

            Assert.False(first.Checked);
            Assert.True(second.Checked);
            Assert.Equal(1, firstChanges);
            Assert.Equal(1, secondChanges);
            Assert.False(first.TabStop);
            Assert.True(second.TabStop);
        });
    }

    [Fact]
    public void ModernRadioButton_GroupName_KeepsIndependentGroupsSelected()
    {
        RunInSta(() =>
        {
            using var host = new Panel();
            using var tool = new ModernRadioButton { GroupName = "tool", Checked = true };
            using var source = new ModernRadioButton { GroupName = "source", Checked = true };
            host.Controls.Add(tool);
            host.Controls.Add(source);

            Assert.True(tool.Checked);
            Assert.True(source.Checked);
            Assert.True(tool.TabStop);
            Assert.True(source.TabStop);
        });
    }

    [Fact]
    public void ModernRadioButton_ArrowKey_SelectsNextEnabledPeer()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 80) };
            using var first = new ModernRadioButton { Text = "Select", Checked = true, TabIndex = 0 };
            using var disabled = new ModernRadioButton { Text = "Rectangle", Enabled = false, TabIndex = 1, Left = 110 };
            using var third = new ModernRadioButton { Text = "Ellipse", TabIndex = 2, Left = 220 };
            host.Controls.AddRange([first, disabled, third]);
            host.Show();
            first.Focus();

            PostKey(first, Keys.Right);

            Assert.False(first.Checked);
            Assert.False(disabled.Checked);
            Assert.True(third.Checked);
            Assert.True(third.Focused);
            host.Close();
        });
    }

    [Fact]
    public void ModernRadioButton_Accessibility_ReportsRoleStateAndDefaultAction()
    {
        RunInSta(() =>
        {
            using var radio = new ModernRadioButton { Text = "Rectangle" };

            Assert.Equal(AccessibleRole.RadioButton, radio.AccessibilityObject.Role);
            Assert.False(radio.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
            Assert.Equal("选择", radio.AccessibilityObject.DefaultAction);

            radio.AccessibilityObject.DoDefaultAction();

            Assert.True(radio.Checked);
            Assert.True(radio.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
            Assert.Equal("取消选择", radio.AccessibilityObject.DefaultAction);

            radio.AccessibilityObject.DoDefaultAction();

            Assert.False(radio.Checked);
            Assert.False(radio.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
            Assert.Equal("选择", radio.AccessibilityObject.DefaultAction);
        });
    }

    [Fact]
    public void ModernRadioButton_Space_TogglesCurrentSelectionOffAndOn()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(220, 60) };
            using var radio = new ModernRadioButton { Text = "Rectangle", Checked = true };
            host.Controls.Add(radio);
            host.Show();
            radio.Focus();
            Application.DoEvents();
            var changes = 0;
            radio.CheckedChanged += (_, _) => changes++;

            PostKey(radio, Keys.Space);
            Assert.False(radio.Checked);
            Assert.True(radio.TabStop);

            PostKey(radio, Keys.Space);
            Assert.True(radio.Checked);
            Assert.Equal(2, changes);
            host.Close();
        });
    }

    [Fact]
    public void ModernSegmentedControl_SelectedValue_SelectsMatchingEnabledItem()
    {
        RunInSta(() =>
        {
            using var segmented = new ModernSegmentedControl();
            segmented.Items.Add(new ModernSegmentedItem("Select", "select"));
            segmented.Items.Add(new ModernSegmentedItem("Rectangle", "rectangle"));
            segmented.Items.Add(new ModernSegmentedItem("Ellipse", "ellipse") { Enabled = false });
            segmented.SelectedIndex = 0;
            var changeCount = 0;
            segmented.SelectedIndexChanged += (_, _) => changeCount++;

            segmented.SelectedValue = "rectangle";

            Assert.Equal(1, segmented.SelectedIndex);
            Assert.Equal("Rectangle", segmented.SelectedItem?.Text);
            Assert.Equal("rectangle", segmented.SelectedValue);
            Assert.Equal(1, changeCount);

            segmented.SelectedValue = "ellipse";
            Assert.Equal(1, segmented.SelectedIndex);
        });
    }

    [Fact]
    public void ModernSegmentedControl_KeyboardNavigation_SkipsDisabledItems()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 80) };
            using var segmented = new ModernSegmentedControl { Width = 330 };
            segmented.Items.Add(new ModernSegmentedItem("Select", "select"));
            segmented.Items.Add(new ModernSegmentedItem("Rectangle", "rectangle") { Enabled = false });
            segmented.Items.Add(new ModernSegmentedItem("Ellipse", "ellipse"));
            segmented.SelectedIndex = 0;
            host.Controls.Add(segmented);
            host.Show();
            segmented.Focus();

            PostKey(segmented, Keys.Right);
            Assert.Equal(2, segmented.SelectedIndex);

            PostKey(segmented, Keys.Home);
            Assert.Equal(0, segmented.SelectedIndex);

            PostKey(segmented, Keys.End);
            Assert.Equal(2, segmented.SelectedIndex);
            host.Close();
        });
    }

    [Fact]
    public void ModernSegmentedControl_Accessibility_ExposesRadioChildrenAndSelection()
    {
        RunInSta(() =>
        {
            using var segmented = new ModernSegmentedControl();
            segmented.Items.Add(new ModernSegmentedItem("Select", "select"));
            segmented.Items.Add(new ModernSegmentedItem("Rectangle", "rectangle"));
            segmented.Items.Add(new ModernSegmentedItem("Ellipse", "ellipse") { Enabled = false });
            segmented.SelectedIndex = 0;

            var root = segmented.AccessibilityObject;
            Assert.Equal(AccessibleRole.Grouping, root.Role);
            Assert.Equal(3, root.GetChildCount());
            Assert.Equal(AccessibleRole.RadioButton, root.GetChild(0)?.Role);
            Assert.True(root.GetChild(0)!.State.HasFlag(AccessibleStates.Checked));
            Assert.True(root.GetChild(2)!.State.HasFlag(AccessibleStates.Unavailable));

            root.GetChild(1)!.DoDefaultAction();

            Assert.Equal(1, segmented.SelectedIndex);
            Assert.True(root.GetChild(1)!.State.HasFlag(AccessibleStates.Checked));
        });
    }

    [Fact]
    public void ModernTabControl_NativePages_PreserveSelectionAndReceiveTheme()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 240) };
            using var tabs = new ModernTabControl { Dock = DockStyle.Fill };
            using var first = new TabPage("Diagnostics");
            using var second = new TabPage("Runtime");
            using var content = new Label { Text = "Running" };
            second.Controls.Add(content);
            tabs.TabPages.Add(first);
            tabs.TabPages.Add(second);
            host.Controls.Add(tabs);
            host.Show();
            var changeCount = 0;
            tabs.SelectedIndexChanged += (_, _) => changeCount++;

            tabs.SelectedIndex = 1;
            ModernUiSettings.ApplyTheme(tabs, ModernTheme.Dark);

            Assert.Same(second, tabs.SelectedTab);
            Assert.Equal(1, changeCount);
            Assert.Same(ModernTheme.Dark, tabs.Theme);
            Assert.Equal(ModernTheme.Dark.Background, second.BackColor);
            Assert.Equal(ModernTheme.Dark.Text, content.ForeColor);
            host.Close();
        });
    }

    [Fact]
    public void ModernTabControl_RendersNativeTabPageImageInTheModernHeader()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 180) };
            using var tabs = new ModernTabControl { Dock = DockStyle.Fill };
            using var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            using var icon = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Magenta);
            images.Images.Add("diagnostics", icon);
            tabs.ImageList = images;
            tabs.TabPages.Add(new TabPage("Diagnostics") { ImageKey = "diagnostics" });
            host.Controls.Add(tabs);
            host.Show();
            Application.DoEvents();

            using var bitmap = new Bitmap(tabs.Width, tabs.Height);
            tabs.DrawToBitmap(bitmap, tabs.ClientRectangle);
            var header = new Rectangle(0, 0, tabs.Width, Math.Min(48, tabs.Height));
            Assert.Same(images, tabs.ImageList);
            Assert.Contains(Pixels(bitmap, header), color => color.R > 220 && color.B > 220 && color.G < 40);
            host.Close();
        });
    }

    [Fact]
    public void ModernTabControl_Accessibility_PreservesNativePageTabProvider()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 240) };
            using var tabs = new ModernTabControl { Dock = DockStyle.Fill, AccessibleName = "运行详情" };
            tabs.TabPages.Add(new TabPage("Tokens"));
            tabs.TabPages.Add(new TabPage("Trace"));
            host.Controls.Add(tabs);
            host.Show();

            Assert.Equal(AccessibleRole.PageTabList, tabs.AccessibilityObject.Role);
            Assert.Equal("运行详情", tabs.AccessibilityObject.Name);
            Assert.True(tabs.AccessibilityObject.GetChildCount() >= 2);
            var tabItems = EnumerateAccessibilityTree(tabs.AccessibilityObject)
                .Where(item => item.Role == AccessibleRole.PageTab).ToArray();
            Assert.True(tabItems.Length >= 2);
            Assert.True(tabItems[0].State.HasFlag(AccessibleStates.Selected));
            tabs.SelectedIndex = 1;
            Application.DoEvents();
            tabItems = EnumerateAccessibilityTree(tabs.AccessibilityObject)
                .Where(item => item.Role == AccessibleRole.PageTab).ToArray();
            Assert.True(tabItems[1].State.HasFlag(AccessibleStates.Selected));
            Assert.False(tabItems[0].State.HasFlag(AccessibleStates.Selected));
            host.Close();
        });
    }

    [Fact]
    public void ModernDataGridView_NativeBindingPreservesKeyboardCurrentCellAndAccessibility()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(460, 220) };
            using var grid = new ModernDataGridView
            {
                Dock = DockStyle.Fill,
                AccessibleName = "变量映射表",
                DataSource = new BindingList<BoundChoice>
                {
                    new(1, "System"),
                    new(2, "System.Linq")
                }
            };
            host.Controls.Add(grid);
            host.Show();
            grid.CurrentCell = grid.Rows[0].Cells[nameof(BoundChoice.Name)];
            grid.Focus();
            Application.DoEvents();

            var changes = 0;
            grid.SelectionChanged += (_, _) => changes++;
            SendKey(grid, Keys.Down);

            Assert.Equal(1, grid.CurrentCell.RowIndex);
            Assert.Equal("System.Linq", grid.CurrentCell.Value);
            Assert.True(changes >= 1);
            Assert.Equal(AccessibleRole.Table, grid.AccessibilityObject.Role);
            Assert.Equal("变量映射表", grid.AccessibilityObject.Name);
            Assert.True(grid.AccessibilityObject.GetChildCount() >= 1);
            host.Close();
        });
    }

    [Fact]
    public void ModernDataGridView_ThemeAndMetricsUsePublicAppearanceSeam()
    {
        RunInSta(() =>
        {
            using var grid = new ModernDataGridView
            {
                Theme = ModernTheme.Dark,
                RowHeight = 34,
                HeaderHeight = 38
            };

            Assert.Equal(ModernTheme.Dark.Control, grid.BackgroundColor);
            Assert.Equal(ModernTheme.Dark.Text, grid.DefaultCellStyle.ForeColor);
            Assert.Equal(ModernTheme.Dark.PrimaryBackground, grid.DefaultCellStyle.SelectionBackColor);
            Assert.Equal(ModernTheme.Dark.Container, grid.ColumnHeadersDefaultCellStyle.BackColor);
            Assert.Equal(34, grid.RowTemplate.Height);
            Assert.Equal(38, grid.ColumnHeadersHeight);
            Assert.False(grid.EnableHeadersVisualStyles);
            Assert.Equal(BorderStyle.None, grid.BorderStyle);
            Assert.Equal(DataGridViewCellBorderStyle.SingleHorizontal, grid.CellBorderStyle);
            Assert.Equal(new Padding(8, 0, 8, 0), grid.ColumnHeadersDefaultCellStyle.Padding);
            Assert.Equal(grid.ColumnHeadersDefaultCellStyle.Padding, grid.DefaultCellStyle.Padding);
            Assert.Equal(grid.ColumnHeadersDefaultCellStyle.Padding, grid.RowsDefaultCellStyle.Padding);
            Assert.Equal(grid.ColumnHeadersDefaultCellStyle.Padding, grid.AlternatingRowsDefaultCellStyle.Padding);
        });
    }

    [Fact]
    public void ModernDataGridView_ModernComboBoxColumnReusesRoundedSelectEditorAndCommits()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 140) };
            using var grid = new ModernDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            grid.Columns.Add(new ModernDataGridViewComboBoxColumn
            {
                Name = "Source",
                DataSource = new[] { "Literal", "Binding" }
            });
            grid.Rows.Add("Literal");
            host.Controls.Add(grid);
            host.Show();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Assert.True(grid.BeginEdit(true));

            var editor = Assert.IsAssignableFrom<ModernSelect>(grid.EditingControl);
            var initialItem = editor.SelectedItem;
            editor.DroppedDown = true;
            var opened = editor.DroppedDown;
            editor.DroppedDown = false;
            editor.SelectedIndex = 1;
            var dirty = grid.IsCurrentCellDirty;
            var committed = grid.EndEdit();
            var value = grid.Rows[0].Cells[0].Value;
            host.Close();

            Assert.Equal("Literal", initialItem);
            Assert.True(opened);
            Assert.True(dirty);
            Assert.True(committed);
            Assert.Equal("Binding", value);
        });
    }

    [Fact]
    public void ModernDataGridView_ModernComboBoxColumnKeepsCellSurfaceWhenEditing()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 140) };
            using var grid = new ModernDataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                Theme = ModernTheme.Dark
            };
            grid.Columns.Add(new ModernDataGridViewComboBoxColumn
            {
                Name = "Source",
                DataSource = new[] { "Literal", "Binding" }
            });
            grid.Rows.Add("Literal");
            host.Controls.Add(grid);
            host.Show();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.Focus();
            Application.DoEvents();

            var cellBounds = grid.GetCellDisplayRectangle(0, 0, false);
            Assert.True(grid.BeginEdit(true));
            Application.DoEvents();
            var editor = Assert.IsType<ModernDataGridViewSelectEditingControl>(grid.EditingControl);
            var editorBounds = grid.RectangleToClient(editor.RectangleToScreen(editor.ClientRectangle));
            grid.EndEdit();
            host.Close();

            Assert.True(cellBounds.Contains(editorBounds), $"Editor {editorBounds} must stay inside cell {cellBounds}.");
            Assert.InRange(editorBounds.Left - cellBounds.Left, 0, grid.DefaultCellStyle.Padding.Left + 1);
            Assert.InRange(cellBounds.Right - editorBounds.Right, 0, grid.DefaultCellStyle.Padding.Right + 1);
            Assert.False(editor.ShowFocusBorder);
        });
    }

    [Fact]
    public void ModernDataGridView_DisposeWhileModernComboBoxEditorIsOpenClosesTheEditLifecycle()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 140) };
            var grid = new ModernDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            grid.Columns.Add(new ModernDataGridViewComboBoxColumn
            {
                Name = "Source",
                DataSource = new[] { "Literal", "Binding" }
            });
            grid.Rows.Add("Literal");
            host.Controls.Add(grid);
            host.Show();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Assert.True(grid.BeginEdit(true));
            var editor = Assert.IsType<ModernDataGridViewSelectEditingControl>(grid.EditingControl);
            editor.DroppedDown = true;
            PumpMessages();
            Assert.True(editor.DroppedDown);

            var failure = Record.Exception(grid.Dispose);
            PumpMessages();

            Assert.Null(failure);
            Assert.True(grid.IsDisposed);
            Assert.DoesNotContain(host.OwnedForms, form => form.Visible);
        });
    }

    [Fact]
    public void ModernDataGridView_ComboBoxColumnPreservesNativeEditorAndCommit()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 140) };
            using var grid = new ModernDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Source",
                DataSource = new[] { "Literal", "Binding" }
            });
            grid.Rows.Add("Literal");
            host.Controls.Add(grid);
            host.Show();
            grid.CurrentCell = grid.Rows[0].Cells[0];
            Assert.True(grid.BeginEdit(true));

            var editor = Assert.IsType<DataGridViewComboBoxEditingControl>(grid.EditingControl);
            editor.SelectedItem = "Binding";
            Assert.True(grid.EndEdit());

            Assert.Equal("Binding", grid.Rows[0].Cells[0].Value);
            host.Close();
        });
    }

    [Fact]
    public void ModernGridAndListView_RoundedRegionTracksFlexibleClientSize()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(760, 220) };
            using var grid = new ModernDataGridView { Bounds = new Rectangle(10, 10, 340, 180), CornerRadius = 10 };
            using var list = new ModernListView { Bounds = new Rectangle(370, 10, 340, 180), CornerRadius = 10 };
            list.Columns.Add("Name", 200);
            list.Items.Add("Camera");
            host.Controls.AddRange([grid, list]);
            host.Show();
            Application.DoEvents();

            Assert.NotNull(grid.Region);
            Assert.False(grid.Region.IsVisible(0, 0));
            Assert.True(grid.Region.IsVisible(grid.Width / 2, grid.Height / 2));
            Assert.NotNull(list.Region);
            Assert.False(list.Region.IsVisible(0, 0));
            Assert.True(list.Region.IsVisible(list.Width / 2, list.Height / 2));

            grid.Size = new Size(420, 190);
            list.Size = new Size(300, 190);
            Application.DoEvents();
            Assert.False(grid.Region.IsVisible(0, 0));
            Assert.True(grid.Region.IsVisible(grid.Width - 6, grid.Height / 2));
            Assert.False(list.Region.IsVisible(0, 0));
            Assert.True(list.Region.IsVisible(list.Width - 6, list.Height / 2));
            host.Close();
        });
    }

    [Fact]
    public void ModernSplitter_AppliesInitialPanelSizeFromFinalHostedBounds()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(640, 360), ShowInTaskbar = false };
            using var split = new ModernSplitter
            {
                Dock = DockStyle.Fill,
                Size = new Size(1000, 560),
                Orientation = Orientation.Vertical,
                InitialPanel2Size = 230,
                FixedPanel = FixedPanel.Panel2,
                Panel1MinSize = 200,
                Panel2MinSize = 160
            };
            host.Controls.Add(split);
            host.Show();
            PumpMessages();

            Assert.Equal(host.ClientSize.Width, split.ClientSize.Width);
            Assert.InRange(split.Panel2.Width, 229, 231);
            Assert.Equal(split.ClientSize.Width - split.SplitterWidth - split.Panel2.Width,
                split.SplitterDistance);
        });
    }

    [Fact]
    public void ModernDataGridView_IncrementalSplitterGrowthDoesNotRetainPreviousRoundedEdges()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(640, 500), ShowInTaskbar = false };
            using var split = new ModernSplitter
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                InitialPanel2Size = 0,
                SplitterDistance = 190,
                Panel1MinSize = 100,
                Panel2MinSize = 100
            };
            using var grid = new ModernDataGridView { Dock = DockStyle.Fill, CornerRadius = 10 };
            grid.Columns.Add("Name", "Name");
            split.Panel1.Controls.Add(grid);
            host.Controls.Add(split);
            host.Show();
            PumpMessages();

            split.SplitterDistance = 150;
            PumpMessages();
            for (var distance = 180; distance <= 360; distance += 30)
            {
                split.SplitterDistance = distance;
                PumpMessages();
            }

            using var candidate = CaptureVisibleClient(grid);
            Assert.True(RedrawWindow(grid.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100));
            using var clean = CaptureVisibleClient(grid);

            // Managed corner masking in a Splitter-hosted grid can differ by its antialiased edge
            // pixels after a forced full redraw; stale former bottom edges changed thousands.
            Assert.InRange(CountDifferentPixels(candidate, clean), 0, 256);
            host.Close();
        });
    }

    [Fact]
    public void ModernDataGridView_HorizontalThumbDragDoesNotRetainCopiedBorderEdges()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 420), ShowInTaskbar = false };
            using var grid = new ModernDataGridView
            {
                Dock = DockStyle.Fill,
                CornerRadius = 10,
                AllowUserToAddRows = false,
                RowHeadersVisible = false
            };
            for (var index = 0; index < 10; index++)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = $"Column {index}", Width = 115 });
            for (var row = 0; row < 8; row++)
                grid.Rows.Add(Enumerable.Range(0, 10).Select(column => $"{row}:{column}").ToArray());
            host.Controls.Add(grid);
            host.Show();
            PumpMessages();

            for (var offset = 12; offset <= 420; offset += 12)
            {
                grid.HorizontalScrollingOffset = offset;
                PumpMessages();
            }

            using var candidate = CaptureVisibleClient(grid);
            Assert.True(RedrawWindow(grid.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100));
            using var clean = CaptureVisibleClient(grid);
            Assert.InRange(CountDifferentPixels(candidate, clean), 0, 256);
            host.Close();
        });
    }

    [Fact]
    public void ModernListView_LastColumnRemainderClearsAfterSelectionChanges()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(440, 180) };
            using var list = new ModernListView { Bounds = new Rectangle(10, 10, 420, 150) };
            list.Columns.Add("Level", 80);
            list.Columns.Add("Code", 90);
            list.Columns.Add("Message", 230);
            var item = list.Items.Add(new ListViewItem(["Error", "WF001", "Broken"]));
            host.Controls.Add(list);
            host.Show();
            item.Selected = true;
            Application.DoEvents();
            item.Selected = false;
            list.Invalidate();
            Application.DoEvents();

            using var bitmap = new Bitmap(list.Width, list.Height);
            list.DrawToBitmap(bitmap, list.ClientRectangle);
            var rowY = list.Font.Height + list.RowHeight / 2 + 8;
            var remainder = bitmap.GetPixel(list.Width - 8, Math.Min(list.Height - 2, rowY));
            Assert.Equal(list.BackColor.ToArgb(), remainder.ToArgb());
            host.Close();
        });
    }

    [Fact]
    public void ModernListView_NativeDetailsPreserveKeyboardSelectionAndAccessibility()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 180) };
            using var list = new ModernListView { Dock = DockStyle.Fill, AccessibleName = "诊断列表" };
            list.Columns.Add("级别", 90);
            list.Columns.Add("代码", 100);
            list.Items.Add(new ListViewItem(["Error", "WF001"]));
            var warning = list.Items.Add(new ListViewItem(["Warning", "WF002"]));
            list.Items[0].Selected = true;
            host.Controls.Add(list);
            host.Show();
            list.FocusedItem = list.Items[0];
            list.Focus();
            Application.DoEvents();

            var changes = 0;
            list.SelectedIndexChanged += (_, _) => changes++;
            SendKey(list, Keys.Down);

            Assert.Single(list.SelectedItems.Cast<ListViewItem>());
            Assert.Same(warning, list.SelectedItems[0]);
            Assert.True(changes >= 1);
            Assert.Equal(AccessibleRole.List, list.AccessibilityObject.Role);
            Assert.Equal("诊断列表", list.AccessibilityObject.Name);
            Assert.Contains(EnumerateAccessibilityTree(list.AccessibilityObject),
                node => node.Role == AccessibleRole.ListItem && node.Name?.Contains("Warning") == true);
            host.Close();
        });
    }

    [Fact]
    public void ModernListView_ThemeAndMetricsPreserveUserImageList()
    {
        RunInSta(() =>
        {
            using var list = new ModernListView { Theme = ModernTheme.Dark, RowHeight = 34 };

            Assert.Equal(ModernTheme.Dark.Control, list.BackColor);
            Assert.Equal(ModernTheme.Dark.Text, list.ForeColor);
            Assert.True(list.OwnerDraw);
            Assert.Equal(View.Details, list.View);
            Assert.Equal(BorderStyle.None, list.BorderStyle);
            Assert.Equal(34, list.SmallImageList!.ImageSize.Height);

            using var images = new ImageList { ImageSize = new Size(16, 16) };
            list.SmallImageList = images;
            list.RowHeight = 40;
            Assert.Same(images, list.SmallImageList);
            Assert.Equal(new Size(16, 16), list.SmallImageList.ImageSize);
        });
    }

    [Fact]
    public void ModernComboBox_DropDownUsesRoundedManagedPopup()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 180) };
            using var combo = new ModernComboBox { Bounds = new Rectangle(20, 20, 260, 34) };
            combo.Items.AddRange(["Automatic", "Manual", "Maintenance"]);
            combo.SelectedIndex = 0;
            host.Controls.Add(combo);
            host.Show();
            Application.DoEvents();

            combo.DroppedDown = true;
            Application.DoEvents();

            Assert.True(combo.DroppedDown);
            Assert.False(combo.InnerComboBox.DroppedDown);
            var popup = EnumerateTopLevelWindowsForCurrentThread()
                .Single(window => window != host.Handle && IsWindowVisible(window));
            Assert.NotEqual("ComboLBox", GetWindowClassName(popup));
            Assert.True(HasRoundedWindowRegion(popup));

            combo.DroppedDown = false;
            host.Close();
        });
    }

    [Fact]
    public void ModernMenuStripDropDownUsesSharedRoundedWindowShape()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 180) };
            using var menu = new ModernMenuStrip { Dock = DockStyle.Top };
            var root = new ToolStripMenuItem("File");
            root.DropDownItems.Add("Open");
            menu.Items.Add(root);
            host.Controls.Add(menu);
            host.Show();
            Application.DoEvents();

            root.ShowDropDown();
            Application.DoEvents();

            Assert.True(root.DropDown.Visible);
            Assert.True(HasRoundedWindowRegion(root.DropDown.Handle));

            root.HideDropDown();
            host.Close();
        });
    }

    [Fact]
    public void ModernComboBox_NativeEditorEdgesAreClippedBehindOuterChrome()
    {
        RunInSta(() =>
        {
            using var combo = new ModernComboBox { Size = new Size(218, 34) };
            combo.PerformLayout();

            var textLineHeight = TextRenderer.MeasureText("Ag", combo.Font, Size.Empty,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
            var targetTextTop = (combo.ClientRectangle.Height - textLineHeight) / 2;
            var nativeTextTopInset = Math.Max(0,
                (combo.InnerComboBox.PreferredHeight - combo.InnerComboBox.ItemHeight) / 2);
            var expectedEditorTop = Math.Max(combo.Padding.Top, targetTextTop - nativeTextTopInset);
            Assert.Equal(expectedEditorTop, combo.InnerComboBox.Top);

            var editorRegion = Assert.IsType<Region>(combo.InnerComboBox.Region);
            Assert.False(editorRegion.IsVisible(0, 0));
            Assert.False(editorRegion.IsVisible(combo.InnerComboBox.Width / 2, combo.InnerComboBox.Height - 1));
            Assert.True(editorRegion.IsVisible(combo.InnerComboBox.Width / 2, combo.InnerComboBox.Height / 2));
        });
    }

    [Fact]
    public void ModernComboBox_SelectedTextInkIsCenteredInOuterChrome()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 80), BackColor = ModernTheme.Light.Background };
            var combo = new ModernComboBox
            {
                Bounds = new Rectangle(20, 20, 218, 34),
                Text = "Company.Product",
                Theme = ModernTheme.Light
            };
            host.Controls.Add(combo);
            host.Show();
            combo.InnerComboBox.Focus();
            combo.SelectAll();
            PumpMessages();

            using var bitmap = new Bitmap(combo.Width, combo.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var deviceContext = graphics.GetHdc();
                try { Assert.True(PrintWindow(combo.Handle, deviceContext, 2)); }
                finally { graphics.ReleaseHdc(deviceContext); }
            }

            var highlight = SystemColors.Highlight;
            var selectedRows = Enumerable.Range(2, combo.Height - 4)
                .Where(y => Enumerable.Range(4, combo.Width - 32)
                    .Count(x =>
                    {
                        var color = bitmap.GetPixel(x, y);
                        return Math.Abs(color.R - highlight.R) < 48 &&
                               Math.Abs(color.G - highlight.G) < 48 &&
                               Math.Abs(color.B - highlight.B) < 48;
                    }) >= 4)
                .ToArray();
            Assert.NotEmpty(selectedRows);
            var selectionCenter = (selectedRows.First() + selectedRows.Last()) / 2f;
            var chromeCenter = (combo.ClientRectangle.Top + combo.ClientRectangle.Bottom - 1) / 2f;
            Assert.True(Math.Abs(selectionCenter - chromeCenter) <= 1f,
                $"Selected text center {selectionCenter} does not match chrome center {chromeCenter}; rows {selectedRows.First()}-{selectedRows.Last()}.");
            host.Close();
        });
    }

    [Fact]
    public void ModernComboBox_CompositedWindowKeepsVisibleBottomBorder()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 80), BackColor = ModernTheme.Light.Background };
            var combo = new ModernComboBox
            {
                Bounds = new Rectangle(20, 20, 218, 34),
                Text = "Company.Product",
                Theme = ModernTheme.Light
            };
            host.Controls.Add(combo);
            host.Show();
            combo.InnerComboBox.Focus();
            PumpMessages();
            Assert.True(combo.ContainsFocus);

            using var bitmap = new Bitmap(host.Width, host.Height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var deviceContext = graphics.GetHdc();
                try { Assert.True(PrintWindow(host.Handle, deviceContext, 2)); }
                finally { graphics.ReleaseHdc(deviceContext); }
            }
            var comboOrigin = new Point(combo.PointToScreen(Point.Empty).X - host.Left,
                combo.PointToScreen(Point.Empty).Y - host.Top);
            var rowCounts = Enumerable.Range(comboOrigin.Y + combo.Height - 5, 4)
                .Select(y => Enumerable.Range(comboOrigin.X + 20, combo.Width - 40)
                    .Select(x => bitmap.GetPixel(x, y))
                    .Count(color => color.B - color.R > 50 && color.B - color.G > 20))
                .ToArray();

            Assert.True(rowCounts.Max() >= combo.Width - 50,
                $"Expected a continuous active bottom border; active-blue pixels by lower row: {string.Join(", ", rowCounts)}.");
            host.Close();
            Application.DoEvents();
        });
    }

    [Fact]
    public void ModernListBox_NativeBindingPreservesKeyboardSelectionAndAccessibility()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 180) };
            using var list = new ModernListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = "引用列表",
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = new[] { new BoundChoice(1, "System"), new BoundChoice(2, "System.Linq") }
            };
            host.Controls.Add(list);
            host.Show();
            list.SelectedIndex = 0;
            list.Focus();
            Application.DoEvents();

            var changes = 0;
            list.SelectedIndexChanged += (_, _) => changes++;
            SendKey(list, Keys.Down);

            Assert.Equal(1, list.SelectedIndex);
            Assert.Equal(2, list.SelectedValue);
            Assert.Equal(1, changes);
            Assert.Equal(AccessibleRole.List, list.AccessibilityObject.Role);
            Assert.Equal("引用列表", list.AccessibilityObject.Name);
            Assert.Contains(EnumerateAccessibilityTree(list.AccessibilityObject),
                node => node.Role == AccessibleRole.ListItem && node.Name == "System.Linq");
            host.Close();
        });
    }

    [Fact]
    public void ModernListBox_ThemeAndRowMetricsUsePublicAppearanceSeam()
    {
        RunInSta(() =>
        {
            using var list = new ModernListBox { Theme = ModernTheme.Dark, RowHeight = 34 };

            Assert.Equal(ModernTheme.Dark.Control, list.BackColor);
            Assert.Equal(ModernTheme.Dark.Text, list.ForeColor);
            Assert.Equal(DrawMode.OwnerDrawFixed, list.DrawMode);
            Assert.Equal(BorderStyle.None, list.BorderStyle);
            Assert.False(list.IntegralHeight);
            Assert.Equal(34, list.ItemHeight);
        });
    }

    [Fact]
    public void ModernTreeView_NativeNodesPreserveKeyboardSelectionAndAccessibility()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 220) };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill, AccessibleName = "工作流工具箱" };
            var category = tree.Nodes.Add("Vision");
            var first = category.Nodes.Add("Acquire Image");
            category.Nodes.Add("Find Circle");
            tree.Nodes.Add("Logic");
            category.Expand();
            tree.SelectedNode = category;
            host.Controls.Add(tree);
            host.Show();
            tree.Focus();
            Application.DoEvents();

            var changes = 0;
            tree.AfterSelect += (_, _) => changes++;
            SendKey(tree, Keys.Down);

            Assert.Same(first, tree.SelectedNode);
            Assert.Equal(1, changes);
            Assert.Equal(AccessibleRole.Outline, tree.AccessibilityObject.Role);
            Assert.Equal("工作流工具箱", tree.AccessibilityObject.Name);
            Assert.True(tree.AccessibilityObject.GetChildCount() >= 1);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_ParentAndDescendantCheckStatesStaySynchronized()
    {
        RunInSta(() =>
        {
            using var tree = new ModernTreeView { CheckBoxes = true };
            var parent = tree.Nodes.Add("Vision");
            var first = parent.Nodes.Add("Acquire Image");
            var group = parent.Nodes.Add("Locate");
            var second = group.Nodes.Add("Find Circle");
            var third = group.Nodes.Add("Match Template");

            parent.Checked = true;
            Assert.All(new[] { first, group, second, third }, node => Assert.True(node.Checked));
            Assert.Equal(CheckState.Checked, tree.GetNodeCheckState(parent));

            second.Checked = false;
            Assert.Equal(CheckState.Indeterminate, tree.GetNodeCheckState(group));
            Assert.Equal(CheckState.Indeterminate, tree.GetNodeCheckState(parent));
            Assert.False(group.Checked);
            Assert.False(parent.Checked);

            third.Checked = false;
            Assert.Equal(CheckState.Unchecked, tree.GetNodeCheckState(group));
            Assert.Equal(CheckState.Indeterminate, tree.GetNodeCheckState(parent));

            first.Checked = false;
            Assert.Equal(CheckState.Unchecked, tree.GetNodeCheckState(parent));
        });
    }

    [Fact]
    public void ModernTreeView_ClickingNodeTextTogglesItsCheckState()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(280, 160) };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill, CheckBoxes = true };
            var node = tree.Nodes.Add("Robot inspection station");
            host.Controls.Add(tree);
            host.Show();
            Application.DoEvents();

            var textPoint = new Point(Math.Min(tree.ClientSize.Width - 12, node.Bounds.Right - 4),
                node.Bounds.Top + Math.Max(1, node.Bounds.Height / 2));
            PostClickControl(tree, textPoint);

            Assert.True(node.Checked, $"Text click at {textPoint} did not check node bounds {node.Bounds}.");
            Assert.Same(node, tree.SelectedNode);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_ExpanderAndNodeSelectionRemainFunctionalWithCompleteOwnerDraw()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(280, 180) };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill, CheckBoxes = true };
            var parent = tree.Nodes.Add("Production line");
            var child = parent.Nodes.Add("Camera");
            parent.Expand();
            host.Controls.Add(tree);
            host.Show();
            Application.DoEvents();

            tree.SelectedNode = parent;
            ClickControl(tree, new Point(10, parent.Bounds.Top + parent.Bounds.Height / 2));
            Assert.False(parent.IsExpanded);
            ClickControl(tree, new Point(10, parent.Bounds.Top + parent.Bounds.Height / 2));
            Assert.True(parent.IsExpanded);
            SendKey(tree, Keys.Down);
            Assert.Same(child, tree.SelectedNode);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_CheckBoxesUseModernThemeRenderer()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(240, 140) };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill, CheckBoxes = true, Theme = ModernTheme.Dark };
            tree.Nodes.Add("Acquire").Checked = true;
            host.Controls.Add(tree);
            host.Show();
            Application.DoEvents();

            using var bitmap = new Bitmap(tree.Width, tree.Height);
            tree.DrawToBitmap(bitmap, tree.ClientRectangle);
            Assert.Contains(Pixels(bitmap, tree.ClientRectangle),
                color => color.ToArgb() == ModernTheme.Dark.Primary.ToArgb());
            Assert.True(tree.Nodes[0].Checked);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_ThemeAndRowMetricsUsePublicAppearanceSeam()
    {
        RunInSta(() =>
        {
            using var tree = new ModernTreeView { Theme = ModernTheme.Dark, NodeHeight = 32 };

            Assert.Equal(ModernTheme.Dark.Background, tree.BackColor);
            Assert.Equal(ModernTheme.Dark.Text, tree.ForeColor);
            Assert.Equal(TreeViewDrawMode.OwnerDrawAll, tree.DrawMode);
            Assert.Equal(BorderStyle.None, tree.BorderStyle);
            Assert.Equal(32, tree.ItemHeight);
        });
    }

    [Fact]
    public void ModernTabControl_SelectedIndicatorSlidesBetweenHeaders()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            ModernUiSettings.AnimationsEnabled = true;
            try
            {
                using var host = new Form { ClientSize = new Size(440, 140) };
                using var tabs = new ModernTabControl
                {
                    Dock = DockStyle.Fill,
                    SelectionAnimationDuration = 240,
                    Theme = ModernTheme.Light
                };
                using var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
                using var icon = new Bitmap(16, 16);
                using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Magenta);
                images.Images.Add(icon);
                tabs.ImageList = images;
                tabs.TabPages.AddRange([
                    new TabPage("One") { ImageIndex = 0 },
                    new TabPage("Two") { ImageIndex = 0 },
                    new TabPage("Three") { ImageIndex = 0 },
                    new TabPage("Four") { ImageIndex = 0 }
                ]);
                tabs.SelectedIndex = 0;
                host.Controls.Add(tabs);
                host.Show();
                Application.DoEvents();

                var headerY = tabs.Font.Height + 2 * tabs.HeaderVerticalPadding - 2;
                var surfaceY = headerY / 2;
                var startX = PrimaryPixelAverageX(tabs, headerY, ModernTheme.Light.Primary);
                var startSurfaceX = ColorPixelAverageX(tabs, surfaceY, ModernTheme.Light.Container);
                tabs.SelectedIndex = 3;
                Thread.Sleep(100);
                Application.DoEvents();
                var middleX = PrimaryPixelAverageX(tabs, headerY, ModernTheme.Light.Primary);
                var middleSurfaceX = ColorPixelAverageX(tabs, surfaceY, ModernTheme.Light.Container);
                Thread.Sleep(260);
                Application.DoEvents();
                var endX = PrimaryPixelAverageX(tabs, headerY, ModernTheme.Light.Primary);
                var endSurfaceX = ColorPixelAverageX(tabs, surfaceY, ModernTheme.Light.Container);

                Assert.True(startX < middleX, $"Expected indicator to leave {startX}, actual {middleX}.");
                Assert.True(middleX < endX, $"Expected in-flight indicator before {endX}, actual {middleX}.");
                var middleProgress = (middleX - startX) / (double)(endX - startX);
                Assert.InRange(middleProgress, .2, .9);
                Assert.True(startSurfaceX < middleSurfaceX,
                    $"Expected selected surface to leave {startSurfaceX}, actual {middleSurfaceX}.");
                Assert.True(middleSurfaceX < endSurfaceX,
                    $"Expected selected surface to remain in flight before {endSurfaceX}, actual {middleSurfaceX}.");
                host.Close();
            }
            finally
            {
                ModernUiSettings.AnimationsEnabled = previousAnimations;
            }
        });
    }

    [Fact]
    public void ModernTabControl_MouseSwitchAnimatesInsideCompositedScrollView()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            ModernUiSettings.AnimationsEnabled = true;
            try
            {
                using var host = new Form { ClientSize = new Size(620, 180) };
                using var viewport = new ModernScrollView { Dock = DockStyle.Fill, UseCompositedScrolling = true };
                using var content = new Panel { Size = new Size(600, 160) };
                using var tabs = new ModernTabControl
                {
                    Bounds = new Rectangle(20, 30, 540, 100),
                    SelectionAnimationDuration = 240,
                    Theme = ModernTheme.Light
                };
                using var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
                using var icon = new Bitmap(16, 16);
                using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Magenta);
                images.Images.Add(icon);
                tabs.ImageList = images;
                tabs.TabPages.AddRange([
                    new TabPage("One") { ImageIndex = 0 },
                    new TabPage("Two") { ImageIndex = 0 },
                    new TabPage("Three") { ImageIndex = 0 },
                    new TabPage("Four") { ImageIndex = 0 }
                ]);
                tabs.SelectedIndex = 0;
                content.Controls.Add(tabs);
                viewport.Content = content;
                host.Controls.Add(viewport);
                host.Show();
                host.Activate();
                host.BringToFront();
                Application.DoEvents();

                var header = tabs.GetChildAtPoint(new Point(4, 4));
                Assert.NotNull(header);
                var scale = tabs.DeviceDpi / 96f;
                var itemWidths = tabs.TabPages.Cast<TabPage>().Select(page =>
                {
                    var textWidth = TextRenderer.MeasureText(page.Text, tabs.Font, Size.Empty,
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                    return Math.Max((int)Math.Round(56 * scale),
                        textWidth + (int)Math.Round(22 * scale) + (int)Math.Round(tabs.HeaderHorizontalPadding * 2 * scale));
                }).ToArray();
                var targetX = itemWidths.Take(3).Sum() + itemWidths[3] / 2;
                var startX = ColorPixelAverageX(header!, header.Height / 2, ModernTheme.Light.Container);

                ClickControl(header, new Point(targetX, header.Height / 2));
                Assert.Equal(3, tabs.SelectedIndex);
                Thread.Sleep(100);
                Application.DoEvents();
                var middleX = ColorPixelAverageX(header, header.Height / 2, ModernTheme.Light.Container);
                Thread.Sleep(260);
                Application.DoEvents();
                var endX = ColorPixelAverageX(header, header.Height / 2, ModernTheme.Light.Container);

                Assert.True(startX < middleX, $"Expected composed selected surface to leave {startX}, actual {middleX}.");
                Assert.True(middleX < endX, $"Expected composed selected surface before {endX}, actual {middleX}.");
                host.Close();
            }
            finally
            {
                ModernUiSettings.AnimationsEnabled = previousAnimations;
            }
        });
    }

    [Fact]
    public void ModernTabControl_ControlsAddTabPage_RedirectsToNativePageCollection()
    {
        RunInSta(() =>
        {
            using var tabs = new ModernTabControl();
            using var page = new TabPage("Input Mapping");

            tabs.Controls.Add(page);

            Assert.Single(tabs.TabPages.Cast<TabPage>());
            Assert.Same(page, tabs.TabPages[0]);
            Assert.False(tabs.Controls.Contains(page));
        });
    }

    [Fact]
    public void DiagnosticsPilotSwitchesBusinessTextWithoutRecreatingControl()
    {
        RunInSta(() =>
        {
            var manager = WorkflowUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
            using var managerLease = (IDisposable)manager;
            using var diagnostics = new WorkflowDiagnosticsControl { LocalizationContext = manager.Context };
            Assert.Equal("未发现诊断问题", diagnostics.SummaryText);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();

            Assert.Equal("No diagnostics found", diagnostics.SummaryText);
            Assert.Equal("DP.WorkFlow diagnostics", diagnostics.AccessibleName);
        });
    }

    [Fact]
    public void LocalizationManagerKeepsPreviousSnapshotWhenLoadingFails()
    {
        RunInSta(() =>
        {
            var initialCulture = CultureInfo.GetCultureInfo("zh-CN");
            var catalog = new TestTextCatalog(new TextKey("test", "Greeting"), "你好");
            var manager = new LocalizationManager(new LocalizationSnapshot(initialCulture, catalog, TextDirection.LeftToRight),
                (_, _) => Task.FromException<LocalizationSnapshot>(new InvalidOperationException("resource failure")));
            using var managerLease = manager;
            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }

            Assert.Throws<InvalidOperationException>(() => change.GetAwaiter().GetResult());
            Assert.Equal("zh-CN", manager.Context.Current.Culture.Name);
            Assert.Equal("你好", manager.Context.Text(new TextKey("test", "Greeting")));
        });
    }

    [Fact]
    public void LocalizationSwitchUpdatesFrameworkDefaultsWithoutOverwritingExplicitText()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
            using var managerLease = (IDisposable)manager;
            using var automatic = new ModernInput { LocalizationContext = manager.Context };
            using var explicitInput = new ModernInput { LocalizationContext = manager.Context, PlaceholderText = "Device ID", Text = "USER-001" };
            Assert.Equal("请输入", automatic.EffectivePlaceholderText);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();

            Assert.Equal("Enter a value", automatic.EffectivePlaceholderText);
            Assert.Equal("Device ID", explicitInput.EffectivePlaceholderText);
            Assert.Equal("USER-001", explicitInput.Text);
        });
    }

    [Fact]
    public void LocalizationProviderUpdatesPageTextAndDetachesOnDispose()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("zh-CN"));
            using var managerLease = (IDisposable)manager;
            using var provider = new ModernLocalizationProvider { LocalizationContext = manager.Context };
            using var label = new Label();
            provider.SetTextKey(label, "modernUi.Confirm");
            Assert.Equal("确定", label.Text);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("en-US"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();
            Assert.Equal("OK", label.Text);

            provider.Dispose();
            var secondChange = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("zh-CN"));
            while (!secondChange.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            secondChange.GetAwaiter().GetResult();
            Assert.Equal("OK", label.Text);
        });
    }

    [Fact]
    public void DesignerMetadataSerializesCollectionsAndAvoidsRuntimeHooks()
    {
        RunInSta(() =>
        {
            var commandsProperty = TypeDescriptor.GetProperties(typeof(ModernCommandBar))[nameof(ModernCommandBar.Commands)];
            var executeProperty = TypeDescriptor.GetProperties(typeof(ModernCommand))[nameof(ModernCommand.ExecuteAction)];
            var providerProperty = TypeDescriptor.GetProperties(typeof(ModernValidationSummary))[nameof(ModernValidationSummary.Provider)];
            Assert.NotNull(commandsProperty);
            Assert.Equal(DesignerSerializationVisibility.Content,
                commandsProperty!.Attributes[typeof(DesignerSerializationVisibilityAttribute)] is DesignerSerializationVisibilityAttribute visibility
                    ? visibility.Visibility : DesignerSerializationVisibility.Visible);
            Assert.False(executeProperty!.IsBrowsable);
            Assert.True(providerProperty!.IsBrowsable);
            Assert.True(TypeDescriptor.GetConverter(typeof(TimeSpan)).CanConvertTo(typeof(System.ComponentModel.Design.Serialization.InstanceDescriptor)));

            using var host = new Form();
            using var manager = new ModernCommandManager { Site = new DesignModeSite() };
            manager.Owner = host;
            host.CreateControl();
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void TransientUiReleasesWindowsTimersAndMessageFiltersWithOwner()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 220) };
            using var manager = new ModernCommandManager { Owner = host };
            using var date = new ModernDatePicker();
            host.Controls.Add(date);
            host.Show();
            Assert.True(manager.IsActive);

            date.DroppedDown = true;
            Assert.NotEmpty(host.OwnedForms);
            date.Dispose();
            PumpMessages();
            Assert.DoesNotContain(host.OwnedForms, form => form.Visible);

            using var message = ModernMessage.Info(host, "Temporary", 40);
            PumpMessages();
            PumpMessages();
            PumpMessages();
            Assert.DoesNotContain(host.OwnedForms, form => form.Visible);

            host.Close();
            PumpMessages();
            Assert.False(manager.IsActive);
        });
    }

    [Fact]
    public void DatePickerCalendarExposesSelectableDateCellsThroughAccessibility()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(100, 100, 420, 220) };
            using var date = new ModernDatePicker
            {
                Bounds = new Rectangle(20, 30, 220, 34),
                Value = new DateTime(2026, 3, 2)
            };
            date.DateEnabledPredicate = candidate => candidate.Day != 8;
            host.Controls.Add(date);
            host.Show();
            date.DroppedDown = true;
            PumpMessages();

            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            var accessibility = calendar.AccessibilityObject;
            Assert.Equal(AccessibleRole.Table, accessibility.Role);
            Assert.Equal(38, accessibility.GetChildCount());
            var selected = Assert.IsAssignableFrom<AccessibleObject>(accessibility.GetChild(8));
            Assert.Equal(AccessibleRole.Cell, selected.Role);
            Assert.True((selected.State & AccessibleStates.Selected) != 0);
            Assert.False(selected.Bounds.IsEmpty);
            var disabled = Assert.IsAssignableFrom<AccessibleObject>(accessibility.GetChild(14));
            Assert.True((disabled.State & AccessibleStates.Unavailable) != 0);
            disabled.DoDefaultAction();
            Assert.Equal(new DateTime(2026, 3, 2), date.Value);
        });
    }

    [Fact]
    public void SelectionControlsKeepInstanceAwareCustomTypeDescriptorDisplayValues()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelect { DisplayMember = "Label" };
            var first = new DynamicDisplayItem("First");
            var second = new DynamicDisplayItem("Second");
            select.Items.AddRange([first, second]);

            Assert.Equal("First", select.GetItemText(first));
            Assert.Equal("Second", select.GetItemText(second));
            first.Label = "Renamed";
            Assert.Equal("Renamed", select.GetItemText(first));
        });
    }

    [Fact]
    public void SelectionControlNestedMemberResolutionFitsLargeListPaintBudget()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelect { DisplayMember = "Metadata.Name" };
            var items = Enumerable.Range(0, 10_000)
                .Select(index => new NestedBoundChoice(new ChoiceMetadata(index, $"Camera {index}")))
                .ToArray();
            var watch = Stopwatch.StartNew();
            for (var index = 0; index < items.Length; index++)
                Assert.Equal($"Camera {index}", select.GetItemText(items[index]));
            watch.Stop();

            Assert.True(watch.ElapsedMilliseconds < 2500,
                $"10,000 nested display resolutions took {watch.ElapsedMilliseconds}ms.");
        });
    }

    [Fact]
    public void DateRangeAccessibilityNamesEndpointsAndPreservesValuesAcrossLocaleSwitch()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("en-US"));
            using var managerLease = (IDisposable)manager;
            var start = new DateTime(2026, 3, 2);
            var end = new DateTime(2026, 3, 9);
            using var range = new ModernDateRangePicker
            {
                StartDate = start,
                EndDate = end,
                LocalizationContext = manager.Context
            };
            var accessibility = range.AccessibilityObject;
            Assert.Equal(AccessibleRole.Grouping, accessibility.Role);
            Assert.Equal(2, accessibility.GetChildCount());
            Assert.Equal("Start date", accessibility.GetChild(0)!.Name);
            Assert.Equal("End date", accessibility.GetChild(1)!.Name);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("zh-CN"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();

            Assert.Equal(start, range.StartDate);
            Assert.Equal(end, range.EndDate);
            Assert.Equal("开始日期", accessibility.GetChild(0)!.Name);
            Assert.Equal("结束日期", accessibility.GetChild(1)!.Name);
        });
    }

    [Fact]
    public void SharedAnimationCommitsFinalFrameImmediatelyWhenAnimationsAreDisabled()
    {
        RunInSta(() =>
        {
            var previous = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = false;
                using var owner = new Panel();
                owner.CreateControl();
                var values = new List<float>();
                var completed = false;

                using var lease = ModernAnimation.Start(owner, 0, 1, 500,
                    value => values.Add(value), () => completed = true);

                Assert.Equal(new[] { 1f }, values);
                Assert.True(completed);
            }
            finally { ModernUiSettings.AnimationsEnabled = previous; }
        });
    }

    [Fact]
    public void ValidatedNativeInputsExposeUpdatedValidationMessageAsAccessibleHelpText()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput { AccessibleDescription = "Device identifier" };
            using var combo = new ModernComboBox { AccessibleDescription = "Camera source" };
            using var date = new ModernDatePicker { AccessibleDescription = "Inspection date" };
            using var time = new ModernTimePicker { AccessibleDescription = "Inspection time" };
            _ = input.AccessibilityObject;
            _ = combo.AccessibilityObject;
            _ = date.AccessibilityObject;
            _ = time.AccessibilityObject;

            input.ValidationMessage = "Device is required";
            combo.ValidationMessage = "Camera is offline";
            date.ValidationMessage = "Date is unavailable";
            time.ValidationMessage = "Time is outside the shift";

            Assert.Equal("Device identifier Device is required", input.AccessibilityObject.Description);
            Assert.Equal("Camera source Camera is offline", combo.AccessibilityObject.Description);
            Assert.Equal("Inspection date Date is unavailable", date.AccessibilityObject.Description);
            Assert.Equal("Inspection time Time is outside the shift", time.AccessibilityObject.Description);

            input.ValidationMessage = string.Empty;
            combo.ValidationMessage = string.Empty;
            date.ValidationMessage = string.Empty;
            time.ValidationMessage = string.Empty;
            Assert.Equal("Device identifier", input.AccessibilityObject.Description);
            Assert.Equal("Camera source", combo.AccessibilityObject.Description);
            Assert.Equal("Inspection date", date.AccessibilityObject.Description);
            Assert.Equal("Inspection time", time.AccessibilityObject.Description);

            input.AccessibleDescription = "Renamed device identifier";
            input.ValidationMessage = "Device is required";
            Assert.Equal("Renamed device identifier Device is required", input.AccessibilityObject.Description);
        });
    }

    [Fact]
    public void DatePickerCalendarAccessibilityExposesLocalizedHeadersParentsAndCellNavigation()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("en-US"));
            using var managerLease = (IDisposable)manager;
            using var host = new Form { Bounds = new Rectangle(100, 100, 420, 220) };
            using var date = new ModernDatePicker
            {
                Bounds = new Rectangle(20, 30, 220, 34),
                Value = new DateTime(2026, 3, 2),
                LocalizationContext = manager.Context
            };
            date.DateEnabledPredicate = candidate => candidate.Day != 8;
            host.Controls.Add(date);
            host.Show();
            date.DroppedDown = true;
            PumpMessages();

            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            var root = calendar.AccessibilityObject;
            Assert.Equal(38, root.GetChildCount());
            var firstHeader = Assert.IsAssignableFrom<AccessibleObject>(root.GetChild(0));
            Assert.Equal(AccessibleRole.ColumnHeader, firstHeader.Role);
            Assert.Same(root, firstHeader.Parent);
            var selected = Assert.IsAssignableFrom<AccessibleObject>(root.GetChild(8));
            Assert.Equal("Select date", selected.DefaultAction);
            Assert.Same(root, selected.Parent);
            var next = Assert.IsAssignableFrom<AccessibleObject>(selected.Navigate(AccessibleNavigation.Right));
            Assert.Contains("Tuesday", next.Name, StringComparison.Ordinal);
            Assert.Same(root, next.Parent);
            var disabled = Assert.IsAssignableFrom<AccessibleObject>(root.GetChild(14));
            Assert.Null(disabled.DefaultAction);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("zh-CN"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();
            Assert.Equal("选择日期", root.GetChild(8)!.DefaultAction);
        });
    }

    [Fact]
    public void CollapsiblePanelAccessibilityReportsExpandedStateAndLocalizedDefaultAction()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("en-US"));
            using var managerLease = (IDisposable)manager;
            using var panel = new ModernCollapsiblePanel
            {
                Text = "Diagnostics",
                LocalizationContext = manager.Context
            };
            var accessibility = panel.AccessibilityObject;
            Assert.Equal(AccessibleRole.Grouping, accessibility.Role);
            Assert.True(accessibility.State.HasFlag(AccessibleStates.Expanded));
            Assert.Equal("Collapse", accessibility.DefaultAction);

            accessibility.DoDefaultAction();
            Assert.False(panel.Expanded);
            Assert.True(accessibility.State.HasFlag(AccessibleStates.Collapsed));
            Assert.Equal("Expand", accessibility.DefaultAction);

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("zh-CN"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();
            Assert.Equal("展开", accessibility.DefaultAction);
        });
    }

    [Fact]
    public void PaginationRtlMirrorsButtonOrderAndIconsWithoutChangingPage()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("ar-SA"));
            using var managerLease = (IDisposable)manager;
            using var host = new Form { ClientSize = new Size(460, 100) };
            using var pagination = new ModernPagination
            {
                Bounds = new Rectangle(20, 20, 400, 34),
                PageSize = 20,
                TotalCount = 100,
                PageIndex = 3,
                LocalizationContext = manager.Context
            };
            host.Controls.Add(pagination);
            host.Show();
            pagination.PerformLayout();

            var buttons = pagination.Controls.OfType<ModernButton>().OrderBy(button => button.Left).ToArray();
            var first = Assert.Single(buttons, button => button.AccessibleName == "第一页");
            var previous = Assert.Single(buttons, button => button.AccessibleName == "上一页");
            var next = Assert.Single(buttons, button => button.AccessibleName == "下一页");
            var last = Assert.Single(buttons, button => button.AccessibleName == "最后一页");
            Assert.True(first.Left > previous.Left);
            Assert.True(previous.Left > next.Left);
            Assert.True(next.Left > last.Left);
            Assert.Equal(ModernIconKind.DoubleChevronRight, first.Icon);
            Assert.Equal(ModernIconKind.ChevronRight, previous.Icon);
            Assert.Equal(ModernIconKind.ChevronLeft, next.Icon);
            Assert.Equal(ModernIconKind.DoubleChevronLeft, last.Icon);
            Assert.Equal(3, pagination.PageIndex);
        });
    }

    [Fact]
    public void DatePickerRtlMirrorsMonthNavigationWithoutChangingCommittedValue()
    {
        RunInSta(() =>
        {
            var arabic = CultureInfo.GetCultureInfo("ar-SA");
            var manager = ModernUiLocalization.CreateManager(arabic);
            using var managerLease = (IDisposable)manager;
            using var host = new Form { Bounds = new Rectangle(100, 100, 420, 220) };
            var committed = new DateTime(2026, 3, 2);
            using var date = new ModernDatePicker
            {
                Bounds = new Rectangle(20, 30, 220, 34),
                Value = committed,
                LocalizationContext = manager.Context
            };
            host.Controls.Add(date);
            host.Show();
            date.DroppedDown = true;
            PumpMessages();
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            var marchFirst = calendar.AccessibilityObject.GetChild(7)!.Name;

            ClickControl(calendar, new Point(20, 20));
            PumpMessages();
            var aprilFirst = calendar.AccessibilityObject.GetChild(7)!.Name;

            Assert.NotEqual(marchFirst, aprilFirst);
            Assert.Equal(new DateTime(2026, 4, 1).ToString("D", arabic), aprilFirst);
            Assert.Equal(committed, date.Value);
        });
    }

    [Fact]
    public void PaginationLocalizationUpdatesAccessibleNamesWithoutChangingPage()
    {
        RunInSta(() =>
        {
            var manager = ModernUiLocalization.CreateManager(CultureInfo.GetCultureInfo("en-US"));
            using var managerLease = (IDisposable)manager;
            using var pagination = new ModernPagination
            {
                PageSize = 20,
                TotalCount = 95,
                PageIndex = 2,
                LocalizationContext = manager.Context
            };
            Assert.Equal("Pagination", pagination.AccessibilityObject.Name);
            Assert.Contains("Page 2 of 5", pagination.AccessibilityObject.Description, StringComparison.Ordinal);
            var buttons = pagination.Controls.OfType<ModernButton>()
                .Select(button => button.AccessibilityObject).ToArray();
            Assert.Contains(buttons, item => item.Name == "First page");
            Assert.Contains(buttons, item => item.Name == "Last page");

            var change = manager.ChangeLocaleAsync(CultureInfo.GetCultureInfo("zh-CN"));
            while (!change.IsCompleted) { Application.DoEvents(); Thread.Sleep(2); }
            change.GetAwaiter().GetResult();

            Assert.Equal(2, pagination.PageIndex);
            Assert.Equal("分页", pagination.AccessibilityObject.Name);
            Assert.Contains("第 2 页，共 5 页", pagination.AccessibilityObject.Description, StringComparison.Ordinal);
            buttons = pagination.Controls.OfType<ModernButton>()
                .Select(button => button.AccessibilityObject).ToArray();
            Assert.Contains(buttons, item => item.Name == "第一页");
            Assert.Contains(buttons, item => item.Name == "最后一页");
        });
    }

    [Fact]
    public void ComboBoxAutoCompleteSurvivesHandleRecreationAndDispose()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(420, 180) };
            using var combo = new ModernComboBox { Bounds = new Rectangle(30, 30, 240, 34) };
            combo.Items.AddRange(["Camera A", "Camera B"]);
            host.Controls.Add(combo);
            host.Show();
            _ = combo.Handle;
            combo.RightToLeft = RightToLeft.Yes;
            PumpMessages();

            Assert.True(combo.IsHandleCreated);
            Assert.Equal(AutoCompleteMode.SuggestAppend, combo.InnerComboBox.AutoCompleteMode);
            Assert.Equal(AutoCompleteSource.ListItems, combo.InnerComboBox.AutoCompleteSource);
            combo.Dispose();
            PumpMessages();
            Assert.True(combo.IsDisposed);
        });
    }

    [Fact]
    public void DatePickerManagedPopupStaysOnScreenAndClosesWhenOwnerMoves()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(80, 80), ClientSize = new Size(360, 140) };
            using var date = new ModernDatePicker { Left = 20, Top = 30, Width = 180, Value = new DateTime(2026, 3, 2), Theme = ModernTheme.Dark };
            date.DateEnabledPredicate = candidate => candidate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            host.Controls.Add(date);
            host.Show();
            Assert.Equal(new DateTime(2026, 3, 2), date.Value);

            ClickControl(date.InnerPicker, new Point(date.InnerPicker.Width - 10, date.InnerPicker.Height / 2));
            PumpMessages();
            PumpMessages();
            var popup = Assert.Single(host.OwnedForms);
            Assert.True(popup.Visible);
            var calendarSurface = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            Assert.Equal(
                new DateTime(2026, 3, 2),
                calendarSurface.GetType().GetProperty("SelectedDate")!.GetValue(calendarSurface));
            Assert.Equal(date.Font.FontFamily.Name, calendarSurface.Font.FontFamily.Name);
            Assert.Equal(date.Font.SizeInPoints, calendarSurface.Font.SizeInPoints);
            Assert.DoesNotContain(EnumerateTopLevelWindowsForCurrentThread(), window =>
                string.Equals(GetWindowClassName(window), "SysMonthCal32", StringComparison.Ordinal));
            Assert.True(Screen.FromControl(date).WorkingArea.Contains(popup.Bounds), $"Popup {popup.Bounds} outside {Screen.FromControl(date).WorkingArea}.");
            using var popupBitmap = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(popupBitmap, popup.ClientRectangle);
            Assert.Contains(Pixels(popupBitmap, popup.ClientRectangle), color => color.ToArgb() == ModernTheme.Dark.Elevated.ToArgb());
            Assert.Contains(Pixels(popupBitmap, popup.ClientRectangle), color => color.ToArgb() == ModernTheme.Dark.TextDisabled.ToArgb());
            // 2026-03-02 is selected; target enabled, unselected 2026-03-03 in the first date row.
            // zh-CN starts weeks on Sunday, so Tuesday is column 2.
            var calendarHeader = (int)Math.Round(44d * calendarSurface.DeviceDpi / 96d);
            var calendarCellHeight = (calendarSurface.ClientSize.Height - calendarHeader) / 7d;
            var hoverPoint = new Point(
                (int)Math.Round(calendarSurface.ClientSize.Width * 2.5d / 7d),
                (int)Math.Round(calendarHeader + calendarCellHeight * 1.5d));
            var hoverDate = new DateTime(2026, 3, 3);
            var firstOfMonth = new DateTime(2026, 3, 1);
            var firstDay = CultureInfo.GetCultureInfo("zh-CN").DateTimeFormat.FirstDayOfWeek;
            var firstOffset = ((int)firstOfMonth.DayOfWeek - (int)firstDay + 7) % 7;
            var slot = firstOffset + hoverDate.Day - 1;
            hoverPoint = new Point(
                (int)Math.Round(calendarSurface.ClientSize.Width * (slot % 7 + .5d) / 7d),
                (int)Math.Round(calendarHeader + calendarCellHeight * (slot / 7 + 1.5d)));
            var previousCalendarAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = false;
                Cursor.Position = calendarSurface.PointToScreen(hoverPoint);
                SendMessage(calendarSurface.Handle, 0x0200, IntPtr.Zero,
                    (IntPtr)((hoverPoint.Y << 16) | (hoverPoint.X & 0xffff)));
                PumpMessages();
            }
            finally { ModernUiSettings.AnimationsEnabled = previousCalendarAnimations; }
            using var afterHover = new Bitmap(calendarSurface.Width, calendarSurface.Height);
            calendarSurface.DrawToBitmap(afterHover, calendarSurface.ClientRectangle);
            Assert.Equal(calendarSurface.ClientSize, afterHover.Size);
            SendKey(date.InnerPicker, Keys.Right);
            SendKey(date.InnerPicker, Keys.Enter);
            PumpMessages();
            Assert.Equal(new DateTime(2026, 3, 3), date.Value);
            Assert.False(date.DroppedDown);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                ClickControl(date.InnerPicker, new Point(date.InnerPicker.Width - 10, date.InnerPicker.Height / 2));
                PumpMessages();
                Assert.True(date.DroppedDown);
                Assert.DoesNotContain(EnumerateTopLevelWindowsForCurrentThread(), window =>
                    string.Equals(GetWindowClassName(window), "SysMonthCal32", StringComparison.Ordinal));
                date.DroppedDown = false;
                PumpMessages();
            }

            date.DroppedDown = true;
            PumpMessages();
            host.Left += 20;
            PumpMessages();
            Assert.False(date.DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void DatePicker_TodayMarkerDoesNotLookLikeASecondSelectionAfterReselection()
    {
        RunInSta(() =>
        {
            var today = DateTime.Today;
            var selected = today.Day < DateTime.DaysInMonth(today.Year, today.Month)
                ? today.AddDays(1)
                : today.AddDays(-1);
            using var host = new Form { ClientSize = new Size(420, 420) };
            using var date = new ModernDatePicker { Bounds = new Rectangle(20, 20, 220, 34), Value = selected };
            host.Controls.Add(date);
            host.Show();
            date.DroppedDown = true;
            PumpMessages();

            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            using var bitmap = new Bitmap(calendar.Width, calendar.Height);
            calendar.DrawToBitmap(bitmap, calendar.ClientRectangle);
            var todayCenter = CalendarDateCenter(calendar, today, CultureInfo.GetCultureInfo("zh-CN"));
            var selectedCenter = CalendarDateCenter(calendar, selected, CultureInfo.GetCultureInfo("zh-CN"));
            var logicalInsetY = 6d * calendar.DeviceDpi / 96d;
            var markerRadius = (int)Math.Round(Math.Min(calendar.ClientSize.Width / 7d - 8d * calendar.DeviceDpi / 96d,
                (calendar.ClientSize.Height - 44d * calendar.DeviceDpi / 96d) / 7d - logicalInsetY) / 2d);
            var ringInk = CountPrimaryRingPixels(bitmap, todayCenter, ModernTheme.Light.Primary, markerRadius);
            var selectedInk = CountMatchingPixels(bitmap,
                new Rectangle(selectedCenter.X - 8, selectedCenter.Y - 8, 17, 17), ModernTheme.Light.Primary, 18);

            Assert.InRange(ringInk, 0, 4);
            Assert.True(selectedInk > 80, $"Selected date has only {selectedInk} primary pixels.");
            host.Close();
        });
    }

    [Fact]
    public void DateRangePicker_RealCalendarReselectionUpdatesAndClosesTheCorrectEndpoint()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 460) };
            using var range = new ModernDateRangePicker
            {
                Bounds = new Rectangle(20, 30, 480, 34),
                StartDate = new DateTime(2026, 8, 5),
                EndDate = new DateTime(2026, 8, 12)
            };
            host.Controls.Add(range);
            host.Show();
            Application.DoEvents();

            var pickers = range.Controls.OfType<ModernDatePicker>().OrderBy(picker => picker.Left).ToArray();
            ClickControl(pickers[0].InnerPicker,
                new Point(pickers[0].InnerPicker.Width - 10, pickers[0].InnerPicker.Height / 2));
            PumpMessages();
            var startPopup = Assert.Single(host.OwnedForms, form => form.Visible);
            var startCalendar = Assert.Single(startPopup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            ClickCalendarDate(startCalendar, new DateTime(2026, 8, 14), CultureInfo.GetCultureInfo("zh-CN"));
            PumpMessages();

            Assert.False(pickers[0].DroppedDown);
            Assert.Equal(new DateTime(2026, 8, 14), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 14), range.EndDate);

            ClickControl(pickers[1].InnerPicker,
                new Point(pickers[1].InnerPicker.Width - 10, pickers[1].InnerPicker.Height / 2));
            PumpMessages();
            var endPopup = Assert.Single(host.OwnedForms, form => form.Visible);
            var endCalendar = Assert.Single(endPopup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            ClickCalendarDate(endCalendar, new DateTime(2026, 8, 16), CultureInfo.GetCultureInfo("zh-CN"));
            PumpMessages();

            Assert.False(pickers[1].DroppedDown);
            Assert.Equal(new DateTime(2026, 8, 14), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 16), range.EndDate);
            host.Close();
        });
    }

    [Fact]
    public void DateRangePicker_SwitchingDirectlyFromStartPopupToEndPopupKeepsEndActive()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 220) };
            using var range = new ModernDateRangePicker
            {
                Bounds = new Rectangle(20, 30, 480, 34),
                StartDate = new DateTime(2026, 8, 5),
                EndDate = new DateTime(2026, 8, 12)
            };
            host.Controls.Add(range);
            host.Show();
            var pickers = range.Controls.OfType<ModernDatePicker>().OrderBy(picker => picker.Left).ToArray();

            pickers[0].DroppedDown = true;
            PumpMessages();
            Assert.True(pickers[0].DroppedDown);
            pickers[1].InnerPicker.Focus();
            pickers[1].DroppedDown = true;
            PumpMessages();

            Assert.False(pickers[0].DroppedDown);
            Assert.True(pickers[1].DroppedDown);
            Assert.True(pickers[1].ContainsFocus);
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            ClickCalendarDate(calendar, new DateTime(2026, 8, 16), CultureInfo.GetCultureInfo("zh-CN"));
            PumpMessages();

            Assert.Equal(new DateTime(2026, 8, 5), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 16), range.EndDate);
            Assert.False(pickers[1].DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void DateRangePicker_ReopeningEachDropDownUsesTheNormalizedRangeEndpoint()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 180) };
            using var range = new ModernDateRangePicker
            {
                Bounds = new Rectangle(20, 30, 480, 34),
                StartDate = new DateTime(2026, 8, 5),
                EndDate = new DateTime(2026, 8, 12)
            };
            host.Controls.Add(range);
            host.Show();
            Application.DoEvents();

            range.StartDate = new DateTime(2026, 8, 14);
            Assert.Equal(new DateTime(2026, 8, 14), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 14), range.EndDate);
            range.EndDate = new DateTime(2026, 8, 16);
            var pickers = range.Controls.OfType<ModernDatePicker>().OrderBy(picker => picker.Left).ToArray();

            for (var attempt = 0; attempt < 3; attempt++)
            {
                pickers[0].DroppedDown = true;
                PumpMessages();
                var startPopup = Assert.Single(host.OwnedForms, form => form.Visible);
                var startCalendar = Assert.Single(startPopup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                    control => control.GetType().Name == "ModernCalendarSurface");
                Assert.Contains("2026", startCalendar.AccessibleName);
                Assert.Contains("8", startCalendar.AccessibleName);
                Assert.Contains("14", startCalendar.AccessibleName);
                pickers[0].DroppedDown = false;
                PumpMessages();

                pickers[1].DroppedDown = true;
                PumpMessages();
                var endPopup = Assert.Single(host.OwnedForms, form => form.Visible);
                var endCalendar = Assert.Single(endPopup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                    control => control.GetType().Name == "ModernCalendarSurface");
                Assert.Contains("2026", endCalendar.AccessibleName);
                Assert.Contains("8", endCalendar.AccessibleName);
                Assert.Contains("16", endCalendar.AccessibleName);
                pickers[1].DroppedDown = false;
                PumpMessages();
            }

            Assert.Equal(new DateTime(2026, 8, 14), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 16), range.EndDate);
            host.Close();
        });
    }

    [Fact]
    public void DateRangePicker_ReopeningAfterSelectionPaintsTheCommittedRange()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 180) };
            using var range = new ModernDateRangePicker
            {
                Bounds = new Rectangle(20, 30, 480, 34),
                StartDate = new DateTime(2026, 8, 5),
                EndDate = new DateTime(2026, 8, 12)
            };
            host.Controls.Add(range);
            host.Show();
            PumpMessages();
            var culture = CultureInfo.GetCultureInfo("zh-CN");
            var pickers = range.Controls.OfType<ModernDatePicker>().OrderBy(picker => picker.Left).ToArray();

            pickers[0].DroppedDown = true;
            PumpMessages();
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            ClickCalendarDate(calendar, new DateTime(2026, 8, 6), culture);
            PumpMessages();

            pickers[1].DroppedDown = true;
            PumpMessages();
            popup = Assert.Single(host.OwnedForms, form => form.Visible);
            calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                control => control.GetType().Name == "ModernCalendarSurface");
            ClickCalendarDate(calendar, new DateTime(2026, 8, 15), culture);
            PumpMessages();

            Assert.Equal(new DateTime(2026, 8, 6), range.StartDate);
            Assert.Equal(new DateTime(2026, 8, 15), range.EndDate);
            var expected = range.Theme.PrimaryBackground;
            foreach (var (picker, selectedDay) in new[] { (pickers[0], 6), (pickers[1], 15) })
            {
                picker.DroppedDown = true;
                PumpMessages();
                popup = Assert.Single(host.OwnedForms, form => form.Visible);
                calendar = Assert.Single(popup.Controls.Cast<Control>().SelectMany(DescendantsAndSelf),
                    control => control.GetType().Name == "ModernCalendarSurface");
                Assert.Contains(selectedDay.ToString(CultureInfo.InvariantCulture), calendar.AccessibleName);
                var middle = CalendarDateCenter(calendar, new DateTime(2026, 8, 10), culture);
                var rangePixel = CaptureWindowPixel(calendar, Point.Add(middle, new Size(-10, 0)));
                var difference = Math.Abs(rangePixel.R - expected.R) + Math.Abs(rangePixel.G - expected.G) +
                                 Math.Abs(rangePixel.B - expected.B);
                Assert.True(difference <= 15,
                    $"Expected committed range background {expected}, actual {rangePixel}.");
                picker.DroppedDown = false;
                PumpMessages();
            }
            host.Close();
        });
    }

    [Fact]
    public void CommandBarKeepsHiddenCommandsInOverflow()
    {
        RunInSta(() =>
        {
            using var bar = new ModernCommandBar { Width = 150 };
            bar.Commands.Add(new ModernCommand { Text = "Run", Icon = ModernIconKind.Play });
            bar.Commands.Add(new ModernCommand { Text = "Settings", Icon = ModernIconKind.Settings });
            bar.Commands.Add(new ModernCommand { Text = "Diagnostics", Icon = ModernIconKind.Info });
            bar.CreateControl();

            Assert.True(bar.OverflowVisible);
            Assert.NotEmpty(bar.OverflowCommands);
            Assert.Contains(bar.OverflowCommands, command => command.Text == "Diagnostics");
        });
    }

    [Fact]
    public void CommandManagerRejectsDuplicateShortcutsAndProtectsTextInput()
    {
        RunInSta(() =>
        {
            using var host = new Form();
            using var input = new ModernInput { Dock = DockStyle.Top };
            using var manager = new ModernCommandManager { Owner = host };
            var executions = 0;
            manager.Commands.Add(new ModernCommand(() => executions++) { ShortcutKeys = Keys.Control | Keys.R });
            manager.Commands.Add(new ModernCommand(() => executions++) { ShortcutKeys = Keys.Control | Keys.R });
            host.Controls.Add(input);
            host.Show();

            Assert.Throws<InvalidOperationException>(() => manager.ProcessShortcut(Keys.Control | Keys.R));
            manager.Commands.RemoveAt(1);
            input.Focus();
            Assert.False(manager.ProcessShortcut(Keys.Control | Keys.R));
            manager.Commands[0].AllowInTextInput = true;
            Assert.True(manager.ProcessShortcut(Keys.Control | Keys.R));
            Assert.Equal(1, executions);
            host.Close();
        });
    }

    [Fact]
    public void CommandManagerRoutesVisibleExecutableShortcut()
    {
        RunInSta(() =>
        {
            var executions = 0;
            using var manager = new ModernCommandManager();
            manager.Commands.Add(new ModernCommand(() => executions++) { ShortcutKeys = Keys.Control | Keys.R });

            Assert.True(manager.ProcessShortcut(Keys.Control | Keys.R));
            Assert.Equal(1, executions);
            Assert.False(manager.ProcessShortcut(Keys.Control | Keys.T));
        });
    }

    [Fact]
    public void ValidationProviderRunsRegisteredAsyncRulesThroughPublicSeam()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput();
            using var provider = new ModernValidationProvider();
            provider.SetAsyncValidator(input, _ => ValueTask.FromResult(
                new ModernValidationOutcome(ModernValidationState.Error, "Address is invalid")));

            var valid = provider.ValidateAsync().GetAwaiter().GetResult();

            Assert.False(valid);
            Assert.Equal(ModernValidationState.Error, input.ValidationState);
            Assert.Equal("Address is invalid", input.ValidationMessage);
        });
    }

    [Fact]
    public void TagSelectionExposesRemovableAccessibleChildren()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelectMultiple { DisplayMode = MultipleSelectionDisplayMode.Tags };
            select.Items.AddRange(["Camera", "PLC"]);
            select.SetItemChecked(0, true);
            select.SetItemChecked(1, true);

            Assert.Equal(2, select.AccessibilityObject.GetChildCount());
            var removeCamera = Assert.IsAssignableFrom<AccessibleObject>(select.AccessibilityObject.GetChild(0));
            Assert.Contains("Camera", removeCamera.Name);
            removeCamera.DoDefaultAction();
            Assert.Single(select.SelectedItems);
        });
    }

    [Fact]
    public void MessageShowsAsOneCompleteNonBlockingFrame()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(500, 300) };
            host.Show();
            var started = Stopwatch.StartNew();
            using var message = ModernMessage.Success(host, "Saved", 0);
            started.Stop();
            var popup = Assert.Single(host.OwnedForms);

            Assert.True(started.ElapsedMilliseconds < 100);
            Assert.Equal(1d, popup.Opacity);
            Assert.True(popup.Visible);
            host.Close();
        });
    }

    [Fact]
    public void ButtonLoadingAnimatesItsVectorIcon()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = true;
                using var host = new Form { ClientSize = new Size(220, 90) };
                using var button = new ModernButton
                {
                    Bounds = new Rectangle(20, 20, 150, 42), Text = "提交", Loading = true,
                    ButtonType = ModernButtonType.Primary
                };
                host.Controls.Add(button);
                host.Show();
                PumpMessages();
                using var first = CaptureControl(button);
                PumpFor(TimeSpan.FromMilliseconds(140));
                using var second = CaptureControl(button);
                Assert.True(CountDifferentPixels(first, second) > 8,
                    "Loading button rendered the same icon in consecutive animation frames.");
                host.Close();
            }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
    }

    [Fact]
    public void PasswordRevealButtonPreservesTextSelectionAndFocus()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 90) };
            using var input = new ModernInput
            {
                Bounds = new Rectangle(20, 20, 300, 38), Text = "camera-admin",
                UseSystemPasswordChar = true, ShowPasswordRevealButton = true
            };
            host.Controls.Add(input);
            host.Show();
            input.InnerTextBox.Focus();
            input.Select(2, 5);
            PumpMessages();
            var reveal = Assert.Single(input.Controls.OfType<ModernButton>());

            reveal.PerformClick();
            PumpMessages();

            Assert.False(input.UseSystemPasswordChar);
            Assert.Equal("camera-admin", input.Text);
            Assert.Equal(2, input.SelectionStart);
            Assert.Equal(5, input.SelectionLength);
            Assert.True(input.InnerTextBox.Focused);
            reveal.PerformClick();
            Assert.True(input.UseSystemPasswordChar);
            host.Close();
        });
    }

    [Fact]
    public void TextAreaHidesBothPillsWhenShortTextDoesNotOverflow()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(520, 240) };
            using var area = new ModernTextArea
            {
                Bounds = new Rectangle(20, 20, 460, 160), ScrollBars = ScrollBars.Both,
                WordWrap = false, Lines = ["Camera 01", "PLC", "Robot"]
            };
            host.Controls.Add(area);
            host.Show();
            PumpMessages();

            var pills = area.Controls.Cast<Control>()
                .Where(control => control.GetType().Name == "ModernNativeScrollBarOverlay").ToArray();
            Assert.Equal(2, pills.Length);
            Assert.All(pills, pill => Assert.False(pill.Visible));
            Assert.Equal(area.InnerTextBox.ClientSize, area.TextViewportBounds.Size);
            host.Close();
        });
    }

    [Fact]
    public void TextAreaWordWrapRemovesHorizontalScrollAndKeepsLastLineVisible()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 260) };
            using var area = new ModernTextArea
            {
                Bounds = new Rectangle(20, 20, 560, 180), ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Lines = Enumerable.Range(1, 18).Select(index => $"{index:00} long acquisition line").ToArray()
            };
            host.Controls.Add(area);
            host.Show();
            area.WordWrap = true;
            area.SelectionStart = area.Text.Length;
            Application.DoEvents();

            Assert.Equal(ScrollBars.Both, area.ScrollBars);
            Assert.Equal(0, area.ScrollPosition.X);
            var pills = area.Controls.Cast<Control>()
                .Where(control => control.GetType().Name == "ModernNativeScrollBarOverlay").ToArray();
            Assert.Equal(2, pills.Length);
            Assert.Single(pills, pill => pill.Visible);
            Assert.Equal(area.InnerTextBox.ClientSize.Height, area.TextViewportBounds.Height);
            area.ScrollToCaret();
            Application.DoEvents();
            var caret = area.InnerTextBox.GetPositionFromCharIndex(area.Text.Length - 1);
            Assert.True(caret.Y + area.Font.Height <= area.TextViewportBounds.Bottom,
                $"Last line is clipped: caret={caret}, viewport={area.TextViewportBounds}.");
            host.Close();
        });
    }

    [Fact]
    public void TextAreaWrappedBottomPinsThumbToNativeLastVisibleLine()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(620, 270) };
            var lines = Enumerable.Range(1, 18)
                .Select(index => $"{index:00} 采集日志：Line Camera 01 / Exposure=1500 / Result=OK").ToArray();
            lines[^1] += new string('1', 45);
            using var area = new ModernTextArea
            {
                Bounds = new Rectangle(20, 20, 560, 190), ScrollBars = ScrollBars.Both,
                WordWrap = true, Lines = lines
            };
            host.Controls.Add(area);
            host.Show();
            area.SelectionStart = area.Text.Length;
            area.ScrollToCaret();
            PumpMessages();

            var vertical = area.Controls.Cast<Control>().Single(control =>
                control.GetType().Name == "ModernNativeScrollBarOverlay" && control.Visible);
            using var frame = CaptureControl(vertical);
            var thumb = FindScrollThumbBounds(frame, area.BackColor);
            Assert.True(thumb.Bottom >= vertical.Height - ScaleFor(area, 4),
                $"Wrapped bottom is visible but thumb is not at the end: thumb={thumb}, track={vertical.ClientSize}.");
            var before = SendMessage(area.InnerTextBox.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
            PostMouseWheel(area.InnerTextBox, -120);
            var after = SendMessage(area.InnerTextBox.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal(before, after);
            host.Close();
        });
    }

    [Fact]
    public void ListBoxScrollThumbStaysCapsuleDuringLiveDrag()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 260) };
            using var list = new ModernListBox { Bounds = new Rectangle(20, 20, 260, 190) };
            list.Items.AddRange(Enumerable.Range(1, 80).Select(index => $"Device {index:00}").Cast<object>().ToArray());
            host.Controls.Add(list);
            host.Show();
            PumpMessages();
            const int windowVerticalScrollStyle = 0x00200000;
            Assert.Equal(0, GetWindowLong(list.Handle, -16) & windowVerticalScrollStyle);
            var overlay = Assert.Single(list.Controls.Cast<Control>(), control =>
                control.Cursor == Cursors.SizeNS && control.Visible);
            var thumbPoint = FindScrollThumbCenter(overlay, list.BackColor);
            var from = overlay.PointToScreen(thumbPoint);
            var toClient = new Point(thumbPoint.X, Math.Min(overlay.Height - 10, thumbPoint.Y + 80));
            var to = overlay.PointToScreen(toClient);
            SendMouseMessage(overlay, 0x0201, from, 1);
            SendMouseMessage(overlay, 0x0200, to, 1);
            Application.DoEvents();

            Assert.Equal(0, GetWindowLong(list.Handle, -16) & windowVerticalScrollStyle);
            SetWindowLong(list.Handle, -16, GetWindowLong(list.Handle, -16) | windowVerticalScrollStyle);
            Assert.Equal(0, GetWindowLong(list.Handle, -16) & windowVerticalScrollStyle);
            using var overlayFrame = new Bitmap(overlay.Width, overlay.Height);
            overlay.DrawToBitmap(overlayFrame, overlay.ClientRectangle);
            var thumbBounds = FindScrollThumbBounds(overlayFrame, list.BackColor);
            var expectedWidth = ScaleFor(list, 6);
            Assert.InRange(thumbBounds.Width, expectedWidth, expectedWidth + 2);
            Assert.True(thumbBounds.Height >= thumbBounds.Width * 2,
                $"Dragging thumb must remain a vertical capsule: {thumbBounds}.");

            SendMouseMessage(overlay, 0x0202, to, 0);
            host.Close();
        });
    }

    [Fact]
    public void ListViewModernCheckboxHitTogglesImmediately()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 180) };
            using var list = new ModernListView { Dock = DockStyle.Fill, CheckBoxes = true, RowHeight = 32 };
            list.Columns.Add("Device", 260);
            var item = list.Items.Add("Camera 01");
            host.Controls.Add(list);
            host.Show();
            PumpMessages();
            var changes = 0;
            list.ItemChecked += (_, _) => changes++;
            var row = item.Bounds;
            ClickControl(list, new Point(12, row.Top + row.Height / 2));

            Assert.True(item.Checked);
            Assert.Equal(1, changes);
            host.Close();
        });
    }

    [Fact]
    public void TimePickerShowsWhichTimeSegmentHasKeyboardFocus()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 120) };
            using var time = new ModernTimePicker
            {
                Bounds = new Rectangle(20, 24, 280, 40),
                Value = new TimeSpan(17, 33, 13)
            };
            host.Controls.Add(time);
            host.Show();
            time.InnerPicker.Focus();
            PumpMessages();

            using var hour = CaptureControl(time.InnerPicker);
            SendMessage(time.InnerPicker.Handle, 0x0100, (IntPtr)(int)Keys.Right, IntPtr.Zero);
            SendMessage(time.InnerPicker.Handle, 0x0101, (IntPtr)(int)Keys.Right, IntPtr.Zero);
            time.InnerPicker.Refresh();
            using var minute = CaptureControl(time.InnerPicker);

            Assert.True(CountDifferentPixels(hour, minute) > 20,
                "Moving between hour and minute must move a visible segment-focus cue.");
            Assert.True(CountMatchingPixels(hour, new Rectangle(Point.Empty, hour.Size), time.Theme.PrimaryBackground, 30) > 20,
                "The focused time segment must use the theme selection surface.");
        });
    }

    [Fact]
    public void TimePickerUsesFullHeightModernSpinnerAndPreservesNativeStepSemantics()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 120) };
            using var time = new ModernTimePicker
            {
                Bounds = new Rectangle(20, 24, 280, 34),
                Value = new TimeSpan(13, 49, 3)
            };
            host.Controls.Add(time);
            host.Show();
            PumpMessages();

            var spinner = Assert.Single(time.Controls.Cast<Control>(),
                control => control.GetType().Name == "ModernTimeSpinnerSurface");
            Assert.True(spinner.Height > time.InnerPicker.Height,
                $"Modern spinner should use the control height: spinner={spinner.Bounds}, native={time.InnerPicker.Bounds}.");
            Assert.True(spinner.Left <= time.InnerPicker.Right && spinner.Right >= time.InnerPicker.Right,
                $"Modern spinner must cover the native up/down HWND: spinner={spinner.Bounds}, native={time.InnerPicker.Bounds}.");
            var background = CaptureWindowPixel(spinner, new Point(spinner.Width - 3, spinner.Height / 4));
            Assert.Equal(time.Theme.Control.ToArgb(), background.ToArgb());

            var before = time.Value;
            ClickControl(spinner, new Point(spinner.Width / 2, spinner.Height / 4));
            PumpMessages();
            Assert.NotEqual(before, time.Value);
            host.Close();
        });
    }

    [Fact]
    public void TemporalEditorsPreserveDateTimeAndDurationSemantics()
    {
        RunInSta(() =>
        {
            using var date = new ModernDatePicker { Value = new DateTime(2026, 3, 14) };
            using var time = new ModernTimePicker { Value = new TimeSpan(16, 25, 30) };
            using var duration = new ModernDurationInput { Value = TimeSpan.FromMinutes(90), Unit = ModernDurationUnit.Hours };

            Assert.Equal(new DateTime(2026, 3, 14), date.Value);
            Assert.Equal(new TimeSpan(16, 25, 30), time.Value);
            Assert.Equal(TimeSpan.FromMinutes(90), duration.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => time.Value = TimeSpan.FromDays(1));
        });
    }

    [Fact]
    public void ValidationToolTipRendersCompleteShortSuccessMessage()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 500, 220) };
            using var input = new ModernInput { Bounds = new Rectangle(40, 90, 320, 36), Text = "192.168.1.12" };
            using var provider = new ModernValidationProvider();
            host.Controls.Add(input);
            host.Show();
            provider.SetValidation(input, ModernValidationState.Success, "地址可用");
            Assert.True(provider.FocusInvalid(input));
            Application.DoEvents();
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            using var bitmap = CaptureControl(popup);
            Assert.True(CountTextInkComponents(bitmap, popup.BackColor) >=
                        CountTextInkComponents("地址可用", popup.Font),
                $"Validation ToolTip text appears clipped: popup={popup.ClientSize}.");
            host.Close();
        });
    }

    [Fact]
    public void ToolTipsFromDifferentProvidersReplaceThePreviousVisibleBubble()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 520, 240) };
            using var firstAnchor = new ModernInput { Bounds = new Rectangle(40, 50, 220, 34) };
            using var secondAnchor = new ModernInput { Bounds = new Rectangle(40, 120, 220, 34) };
            using var first = new ModernToolTip();
            using var second = new ModernToolTip();
            host.Controls.AddRange([firstAnchor, secondAnchor]);
            host.Show();
            first.Show(firstAnchor, "First bubble", duration: 0);
            second.Show(secondAnchor, "Second bubble", duration: 0);
            Application.DoEvents();

            Assert.Single(host.OwnedForms, form => form.Visible);
        });
    }

    [Fact]
    public void ValidationToolTipCreatesNoUnownedShadowWindowThatCanReplaceTheOwnerInZOrder()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 500, 220) };
            using var input = new ModernInput { Bounds = new Rectangle(40, 90, 320, 36), Text = "192.168.1.12" };
            using var provider = new ModernValidationProvider();
            host.Controls.Add(input);
            host.Show();
            var before = EnumerateTopLevelWindowsForCurrentThread().ToHashSet();
            provider.SetValidation(input, ModernValidationState.Success, "地址可用");
            provider.FocusInvalid(input);
            Application.DoEvents();

            var addedVisible = EnumerateTopLevelWindowsForCurrentThread()
                .Where(window => !before.Contains(window) && IsWindowVisible(window)).ToArray();
            Assert.NotEmpty(addedVisible);
            Assert.All(addedVisible, window => Assert.Equal(host.Handle, GetWindow(window, 4)));
            host.Close();
        });
    }

    [Fact]
    public void ValidationToolTipRepeatedShowForSameAnchorReusesCompleteFrame()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 500, 220) };
            using var input = new ModernInput { Bounds = new Rectangle(40, 90, 320, 36), Text = "192.168.1.12" };
            using var provider = new ModernValidationProvider();
            host.Controls.Add(input);
            host.Show();
            provider.SetValidation(input, ModernValidationState.Success, "地址可用");
            provider.FocusInvalid(input);
            Application.DoEvents();
            var first = Assert.Single(host.OwnedForms, form => form.Visible);
            var firstHandle = first.Handle;

            provider.FocusInvalid(input);
            Application.DoEvents();

            var second = Assert.Single(host.OwnedForms, form => form.Visible);
            Assert.Same(first, second);
            Assert.Equal(firstHandle, second.Handle);
            Assert.Equal(1d, second.Opacity);
            host.Close();
        });
    }

    [Fact]
    public void ValidationProviderCoordinatesResultsAndFocusesFirstError()
    {
        RunInSta(() =>
        {
            using var host = new Form();
            using var input = new ModernInput();
            using var select = new ModernSelect();
            using var provider = new ModernValidationProvider();
            host.Controls.AddRange([input, select]);
            host.Show();

            provider.SetValidation(input, ModernValidationState.Warning, "Review");
            provider.SetValidation(select, ModernValidationState.Error, "Required");

            Assert.Equal(2, provider.Results.Count);
            Assert.True(provider.FocusFirstInvalid());
            Assert.True(select.Focused);
            provider.Clear();
            Assert.Empty(provider.Results);
            Assert.Equal(ModernValidationState.None, select.ValidationState);
            host.Close();
        });
    }

    [Fact]
    public void SelectMultipleTagModeRetainsSelectionAndRendersCollapsedTags()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelectMultiple { Width = 260, DisplayMode = MultipleSelectionDisplayMode.Tags, MaxVisibleTags = 2 };
            select.Items.AddRange(["Alpha", "Beta", "Gamma"]);
            select.SetItemChecked(0, true);
            select.SetItemChecked(1, true);
            select.SetItemChecked(2, true);

            using var bitmap = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(bitmap, select.ClientRectangle);

            Assert.Equal(3, select.SelectedItems.Count);
            Assert.Equal(2, select.MaxVisibleTags);
            Assert.Contains(Pixels(bitmap, select.ClientRectangle), color => color.ToArgb() == select.Theme.PrimaryBackground.ToArgb());
        });
    }

    [Fact]
    public void SharedCommandExecutesAndUpdatesButtonAndMenuState()
    {
        RunInSta(() =>
        {
            var executions = 0;
            var allowed = true;
            var command = new ModernCommand(() => executions++)
            {
                Text = "Run",
                Icon = ModernIconKind.Play,
                CanExecutePredicate = () => allowed,
                CheckOnExecute = true
            };
            using var button = new ModernButton { Command = command };
            using var menu = new ModernContextMenu();
            menu.SetCommands([command]);

            button.PerformClick();
            Assert.Equal(1, executions);
            Assert.True(command.IsChecked);
            Assert.Equal("Run", button.Text);
            Assert.True(((ToolStripMenuItem)menu.Items[0]).Checked);

            allowed = false;
            command.RaiseCanExecuteChanged();
            Assert.False(button.Enabled);
            Assert.False(menu.Items[0].Enabled);
        });
    }

    [Fact]
    public void InputControlsShareValidationStateAndLegacyErrorCompatibility()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput();
            using var combo = new ModernComboBox();
            using var select = new ModernSelect();
            using var multiple = new ModernSelectMultiple();
            using var slider = new ModernSlider();
            foreach (var validation in new IModernValidationControl[] { input, combo, select, multiple, slider })
            {
                validation.ValidationMessage = "Required";
                validation.ValidationState = ModernValidationState.Error;
                Assert.Equal("Required", validation.ValidationMessage);
            }
            Assert.True(input.HasError);
            Assert.True(combo.HasError);

            using var bitmap = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(bitmap, select.ClientRectangle);
            Assert.Contains(Pixels(bitmap, select.ClientRectangle), color =>
                Math.Abs(color.R - select.Theme.Error.R) +
                Math.Abs(color.G - select.Theme.Error.G) +
                Math.Abs(color.B - select.Theme.Error.B) <= 80);
        });
    }

    [Fact]
    public void OpeningOneManagedPopupClosesThePreviouslyActivePopup()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 160) };
            using var first = new ModernSelect { Left = 20, Top = 30, Width = 180 };
            using var second = new ModernSelectMultiple { Left = 240, Top = 30, Width = 180 };
            first.Items.AddRange(["One", "Two"]);
            second.Items.AddRange(["Alpha", "Beta"]);
            host.Controls.AddRange([first, second]);
            host.Show();

            first.DroppedDown = true;
            PumpMessages();
            second.DroppedDown = true;
            PumpMessages();

            Assert.False(first.DroppedDown);
            Assert.True(second.DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void SelectionControls_RenderImagesResolvedFromNestedMemberPaths()
    {
        RunInSta(() =>
        {
            using var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            using var icon = new Bitmap(16, 16);
            using (var graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Magenta);
            images.Images.Add("camera", icon);
            var item = new IconChoice("Camera", new IconMetadata("camera"));

            using var select = new ModernSelect { Size = new Size(220, 34), ImageList = images, ImageKeyMember = "Icon.Key" };
            select.Items.Add(item);
            select.DisplayMember = nameof(IconChoice.Name);
            select.SelectedIndex = 0;

            using var multiple = new ModernSelectMultiple { Size = new Size(220, 34), ImageList = images, ImageKeyMember = "Icon.Key" };
            multiple.Items.Add(item);
            multiple.DisplayMember = nameof(IconChoice.Name);
            multiple.SetItemChecked(0, true);

            using var combo = new ModernComboBox { Size = new Size(220, 34), ImageList = images, ImageKeyMember = "Icon.Key" };
            combo.Items.Add(item);
            combo.DisplayMember = nameof(IconChoice.Name);
            combo.SelectedIndex = 0;

            foreach (var control in new Control[] { select, multiple, combo })
            {
                using var bitmap = new Bitmap(control.Width, control.Height);
                control.DrawToBitmap(bitmap, control.ClientRectangle);
                Assert.Contains(Pixels(bitmap, control.ClientRectangle),
                    color => color.R > 220 && color.B > 220 && color.G < 40);
            }
        });
    }

    [Fact]
    public void ModernSelect_DataMembersExposeDisplayAndSelectedValue()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = new[]
                {
                    new BoundChoice(1, "Camera 1"),
                    new BoundChoice(2, "Camera 2")
                }
            };

            select.SelectedValue = 2;

            Assert.Equal(1, select.SelectedIndex);
            Assert.Equal(2, select.SelectedValue);
            Assert.Equal("Camera 2", select.AccessibilityObject.Value);
        });
    }

    [Fact]
    public void SelectDataMembers_SupportNestedDisplayAndValuePaths()
    {
        RunInSta(() =>
        {
            var choices = new[]
            {
                new NestedBoundChoice(new ChoiceMetadata(1, "Camera 1")),
                new NestedBoundChoice(new ChoiceMetadata(2, "Camera 2"))
            };
            const string displayPath = nameof(NestedBoundChoice.Metadata) + "." + nameof(ChoiceMetadata.Name);
            const string valuePath = nameof(NestedBoundChoice.Metadata) + "." + nameof(ChoiceMetadata.Id);
            using var select = new ModernSelect
            {
                DisplayMember = displayPath,
                ValueMember = valuePath,
                DataSource = choices
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = displayPath,
                ValueMember = valuePath,
                DataSource = choices
            };

            select.SelectedValue = 2;
            multiple.SetSelectedValues([2]);

            Assert.Same(choices[1], select.SelectedItem);
            Assert.Equal("Camera 2", select.AccessibilityObject.Value);
            Assert.Equal([choices[1]], multiple.SelectedItems);
            Assert.Equal([2], multiple.SelectedValues);
            Assert.Equal("Camera 2", multiple.AccessibilityObject.Value);
        });
    }

    [Fact]
    public void SelectDataMembers_NullIntermediateStillValidatesRemainingPath()
    {
        RunInSta(() =>
        {
            var choice = new NestedBoundChoice(null);
            using var valid = new ModernSelectMultiple
            {
                ValueMember = nameof(NestedBoundChoice.Metadata) + "." + nameof(ChoiceMetadata.Id),
                DataSource = new[] { choice }
            };
            using var invalid = new ModernSelectMultiple
            {
                DisplayMember = nameof(NestedBoundChoice.Metadata) + ".Missing",
                DataSource = new[] { choice }
            };

            valid.SetSelectedValues([null]);
            invalid.SetItemChecked(0, true);

            Assert.Equal([choice], valid.SelectedItems);
            Assert.Equal([null], valid.SelectedValues);
            Assert.Throws<InvalidOperationException>(() => _ = invalid.AccessibilityObject.Value);
        });
    }

    [Fact]
    public void SelectDataMembers_InvalidPathsFailConsistentlyWhenResolved()
    {
        RunInSta(() =>
        {
            var choices = new[] { new BoundChoice(1, "Camera 1") };
            using var select = new ModernSelect
            {
                ValueMember = "Metadata.Missing",
                DataSource = choices
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = "Metadata.Missing",
                DataSource = choices
            };
            multiple.SetItemChecked(0, true);

            var valueError = Assert.Throws<InvalidOperationException>(() => select.SelectedValue = 1);
            var displayError = Assert.Throws<InvalidOperationException>(() => _ = multiple.AccessibilityObject.Value);

            Assert.Contains("Metadata.Missing", valueError.Message);
            Assert.Contains(nameof(BoundChoice), valueError.Message);
            Assert.Contains("Metadata.Missing", displayError.Message);
            Assert.Contains(nameof(BoundChoice), displayError.Message);
        });
    }

    [Fact]
    public void SelectDataMembers_KeepEqualDisplayTextValuesIndependentAndAllowNullSelection()
    {
        RunInSta(() =>
        {
            var choices = new[]
            {
                new BoundChoice(1, "Camera"),
                new BoundChoice(2, "Camera")
            };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = choices
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = choices
            };

            select.SelectedValue = 2;
            multiple.SetSelectedValues([2]);

            Assert.Same(choices[1], select.SelectedItem);
            Assert.Equal([choices[1]], multiple.SelectedItems);
            Assert.Equal("Camera", select.AccessibilityObject.Value);
            Assert.Equal("Camera", multiple.AccessibilityObject.Value);

            select.SelectedValue = null;
            Assert.Equal(-1, select.SelectedIndex);
            Assert.Null(select.SelectedValue);
        });
    }

    [Fact]
    public void ModernSelectMultiple_SelectedValuesUseValueMemberAndItemOrder()
    {
        RunInSta(() =>
        {
            var choices = new[]
            {
                new BoundChoice(1, "Camera 1"),
                new BoundChoice(2, "Camera 2"),
                new BoundChoice(3, "Camera 3")
            };
            using var select = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = choices
            };

            select.SetSelectedValues([3, 1]);

            Assert.Equal([choices[0], choices[2]], select.SelectedItems);
            Assert.Equal([1, 3], select.SelectedValues);
            Assert.Equal("Camera 1, Camera 3", select.AccessibilityObject.Value);
        });
    }

    [Fact]
    public void SelectDataSource_RejectedReplacementKeepsPreviousBindingAlive()
    {
        RunInSta(() =>
        {
            var source = new BindingList<BoundChoice> { new(1, "Camera 1") };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };

            Assert.Throws<ArgumentException>(() => select.DataSource = new object());
            Assert.Throws<ArgumentException>(() => multiple.DataSource = new object());
            source.Add(new BoundChoice(2, "Camera 2"));
            select.SelectedValue = 2;
            multiple.SetSelectedValues([2]);

            Assert.Same(source, select.DataSource);
            Assert.Same(source, multiple.DataSource);
            Assert.Equal("Camera 2", select.AccessibilityObject.Value);
            Assert.Equal([2], multiple.SelectedValues);
        });
    }

    [Fact]
    public void SelectDataSource_NullDetachesBindingAndRestoresManualItemsMode()
    {
        RunInSta(() =>
        {
            var source = new BindingList<BoundChoice> { new(1, "Bound Camera") };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            multiple.SetSelectedValues([1]);

            select.DataSource = null;
            multiple.DataSource = null;
            source.Add(new BoundChoice(2, "Stale Camera"));
            var manual = new BoundChoice(3, "Manual Camera");
            select.Items.Add(manual);
            multiple.Items.Add(manual);
            select.SelectedValue = 3;
            multiple.SetSelectedValues([3]);

            Assert.Single(select.Items.Cast<object>());
            Assert.Single(multiple.Items.Cast<object>());
            Assert.Same(manual, select.SelectedItem);
            Assert.Equal("Manual Camera", select.AccessibilityObject.Value);
            Assert.Equal([manual], multiple.SelectedItems);
            Assert.Equal([3], multiple.SelectedValues);
        });
    }

    [Fact]
    public void SelectDataSource_BindingListChangesRemainSelectable()
    {
        RunInSta(() =>
        {
            var source = new BindingList<BoundChoice>
            {
                new(1, "Camera 1")
            };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };

            source.Add(new BoundChoice(2, "Camera 2"));
            select.SelectedValue = 2;
            multiple.SetSelectedValues([2]);

            Assert.Equal("Camera 2", select.AccessibilityObject.Value);
            Assert.Equal([2], multiple.SelectedValues);
        });
    }

    [Fact]
    public void SelectDataSource_BindingSourceResetRefreshesDisplayWithoutLosingIdentity()
    {
        RunInSta(() =>
        {
            var choices = new BindingList<MutableBoundChoice>
            {
                new(1, "Camera 1"),
                new(2, "Camera 2")
            };
            using var source = new BindingSource { DataSource = choices };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(MutableBoundChoice.Name),
                ValueMember = nameof(MutableBoundChoice.Id),
                DataSource = source,
                SelectedValue = 2
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(MutableBoundChoice.Name),
                ValueMember = nameof(MutableBoundChoice.Id),
                DataSource = source
            };
            multiple.SetSelectedValues([2]);

            choices[1].Name = "Inspection Camera";
            source.ResetItem(1);

            Assert.Equal("Inspection Camera", select.AccessibilityObject.Value);
            Assert.Equal("Inspection Camera", multiple.AccessibilityObject.Value);
            Assert.Same(choices[1], Assert.Single(multiple.SelectedItems));
        });
    }

    [Fact]
    public void SelectDataSource_OpenDropDownTracksBindingListChanges()
    {
        RunInSta(() =>
        {
            var source = new BindingList<BoundChoice> { new(1, "Camera 1") };
            using var host = new Form { ClientSize = new Size(480, 100) };
            using var select = new ModernSelect
            {
                Width = 200,
                DropDownAnimationDuration = 0,
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            using var multiple = new ModernSelectMultiple
            {
                Left = 220,
                Width = 240,
                DropDownAnimationDuration = 0,
                CheckBoxAnimationDuration = 0,
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            host.Controls.AddRange([select, multiple]);
            host.Show();
            host.Activate();
            Application.DoEvents();

            select.DroppedDown = true;
            var initialPopupContent = Assert.Single(Assert.Single(host.OwnedForms).Controls.Cast<Control>());
            Assert.Equal(select.Font.FontFamily.Name, initialPopupContent.Font.FontFamily.Name);
            Assert.Equal(select.Font.SizeInPoints, initialPopupContent.Font.SizeInPoints);
            source.Add(new BoundChoice(2, "Camera 2"));
            PumpMessages();
            PostKey(select, Keys.End);
            PostKey(select, Keys.Enter);
            Assert.Equal(2, select.SelectedValue);

            multiple.DroppedDown = true;
            source.Add(new BoundChoice(3, "Camera 3"));
            PumpMessages();
            PostKey(multiple, Keys.End);
            PostKey(multiple, Keys.Space);
            Assert.Equal([3], multiple.SelectedValues);
            multiple.DroppedDown = false;
            host.Close();
        });
    }

    [Fact]
    public void SelectDataSource_ReplacingItemKeepsSingleValueAndDropsOldMultiIdentity()
    {
        RunInSta(() =>
        {
            var original = new BoundChoice(2, "Camera 2");
            var replacement = new BoundChoice(2, "Replacement Camera");
            var source = new BindingList<BoundChoice>
            {
                new(1, "Camera 1"),
                original
            };
            using var select = new ModernSelect
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source,
                SelectedValue = 2
            };
            using var multiple = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            multiple.SetSelectedItems([original]);
            var multiChangeCount = 0;
            multiple.SelectionChanged += (_, _) => multiChangeCount++;

            source[1] = replacement;

            Assert.Same(replacement, select.SelectedItem);
            Assert.Equal(2, select.SelectedValue);
            Assert.Equal("Replacement Camera", select.AccessibilityObject.Value);
            Assert.Empty(multiple.SelectedItems);
            Assert.Empty(multiple.SelectedValues);
            Assert.Equal(1, multiChangeCount);
        });
    }

    [Fact]
    public void ModernSelectMultiple_RemovingBoundItemUpdatesSelection()
    {
        RunInSta(() =>
        {
            var source = new BindingList<BoundChoice>
            {
                new(1, "Camera 1"),
                new(2, "Camera 2")
            };
            using var select = new ModernSelectMultiple
            {
                ValueMember = nameof(BoundChoice.Id),
                DataSource = source
            };
            select.SetSelectedValues([2]);
            var changeCount = 0;
            select.SelectionChanged += (_, _) => changeCount++;

            source.RemoveAt(1);

            Assert.Empty(select.SelectedItems);
            Assert.Empty(select.SelectedValues);
            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void SelectDataSource_ClearingOpenDropDownRemovesSelectionAndStaleNavigation()
    {
        RunInSta(() =>
        {
            var singleSource = new BindingList<BoundChoice>
            {
                new(1, "Camera 1"),
                new(2, "Camera 2")
            };
            var multiSource = new BindingList<BoundChoice>(singleSource.ToList());
            using var host = new Form { ClientSize = new Size(460, 80) };
            using var select = new ModernSelect
            {
                Width = 200,
                DropDownAnimationDuration = 0,
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = singleSource,
                SelectedValue = 2
            };
            using var multiple = new ModernSelectMultiple
            {
                Left = 220,
                Width = 220,
                DropDownAnimationDuration = 0,
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = multiSource
            };
            multiple.SetSelectedValues([2]);
            host.Controls.AddRange([select, multiple]);
            host.Show();
            Application.DoEvents();

            select.DroppedDown = true;
            singleSource.Clear();
            PostKey(select, Keys.End);
            PostKey(select, Keys.Enter);

            Assert.Equal(-1, select.SelectedIndex);
            Assert.Null(select.SelectedValue);

            multiple.DroppedDown = true;
            multiSource.Clear();
            PostKey(multiple, Keys.End);
            PostKey(multiple, Keys.Space);

            Assert.Empty(multiple.SelectedItems);
            Assert.Empty(multiple.SelectedValues);
            multiple.DroppedDown = false;
            select.DroppedDown = false;
            host.Close();
        });
    }

    [Fact]
    public void ModernSelectMultiple_ChangingDataSourceClearsPreviousSelection()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelectMultiple
            {
                DisplayMember = nameof(BoundChoice.Name),
                ValueMember = nameof(BoundChoice.Id),
                DataSource = new[] { new BoundChoice(1, "Camera 1") }
            };
            select.SetSelectedValues([1]);
            var changeCount = 0;
            select.SelectionChanged += (_, _) => changeCount++;

            select.DataSource = new[] { new BoundChoice(2, "Camera 2") };

            Assert.Empty(select.SelectedItems);
            Assert.Empty(select.SelectedValues);
            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void ModernSelectMultiple_NullValuesMatchOneItemPerRequestedValue()
    {
        RunInSta(() =>
        {
            var first = new NullableBoundChoice(null, "Unassigned 1");
            var second = new NullableBoundChoice(null, "Unassigned 2");
            var assigned = new NullableBoundChoice(3, "Camera 3");
            using var select = new ModernSelectMultiple
            {
                DisplayMember = nameof(NullableBoundChoice.Name),
                ValueMember = nameof(NullableBoundChoice.Id),
                DataSource = new[] { first, assigned, second }
            };

            select.SetSelectedValues([null, null]);

            Assert.Equal([first, second], select.SelectedItems);
            Assert.Equal([null, null], select.SelectedValues);
            Assert.Equal("Unassigned 1, Unassigned 2", select.AccessibilityObject.Value);
        });
    }

    [Fact]
    public void ModernSelectMultiple_EqualItemsCanBeCheckedIndependently()
    {
        RunInSta(() =>
        {
            using var select = new ModernSelectMultiple();
            var first = new BoundChoice(1, "Camera");
            var second = new BoundChoice(1, "Camera");
            select.Items.Add(first);
            select.Items.Add(second);

            select.SetItemChecked(0, true);

            var selected = Assert.Single(select.SelectedItems);
            Assert.Same(first, selected);
        });
    }

    [Fact]
    public void Text_SetOnce_RaisesTextChangedOnce()
    {
        RunInSta(() =>
        {
            using var input = new ModernInput();
            var changeCount = 0;
            input.TextChanged += (_, _) => changeCount++;

            input.Text = "Camera 1";

            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void Minimum_RaisedAboveValue_ClampsValueAndRaisesChange()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber { Value = 5 };
            var changeCount = 0;
            input.ValueChanged += (_, _) => changeCount++;

            input.Minimum = 10;

            Assert.Equal(10, input.Value);
            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void Maximum_LoweredBelowValue_ClampsValueAndRaisesChange()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber { Value = 20 };
            var changeCount = 0;
            input.ValueChanged += (_, _) => changeCount++;

            input.Maximum = 10;

            Assert.Equal(10, input.Value);
            Assert.Equal(1, changeCount);
        });
    }

    [Fact]
    public void Range_Inverted_ThrowsArgumentOutOfRangeException()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber { Minimum = 0, Maximum = 10 };

            Assert.Throws<ArgumentOutOfRangeException>(() => input.Minimum = 11);
            Assert.Throws<ArgumentOutOfRangeException>(() => input.Maximum = -1);
        });
    }

    [Fact]
    public void Increment_NotPositive_ThrowsArgumentOutOfRangeException()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber();

            Assert.Throws<ArgumentOutOfRangeException>(() => input.Increment = 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => input.Increment = -1);
        });
    }

    [Fact]
    public void DecimalPlaces_OutsideDecimalPrecision_ThrowsArgumentOutOfRangeException()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber();

            Assert.Throws<ArgumentOutOfRangeException>(() => input.DecimalPlaces = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => input.DecimalPlaces = 29);
        });
    }

    [Fact]
    public void ModernSelectRightToLeftPopupAlignsItsRightEdgeWithTheAnchor()
    {
        RunInSta(() =>
        {
            using var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(120, 120, 520, 240),
                RightToLeft = RightToLeft.Yes
            };
            using var select = new ModernSelect
            {
                Bounds = new Rectangle(220, 50, 180, 34),
                RightToLeft = RightToLeft.Yes
            };
            select.Items.AddRange(["Camera A", "Camera B"]);
            host.Controls.Add(select);
            host.Show();
            select.DroppedDown = true;
            PumpMessages();

            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            var anchor = select.RectangleToScreen(select.ClientRectangle);
            Assert.InRange(Math.Abs(popup.Right - anchor.Right), 0, 1);
        });
    }

    [Fact]
    public void ModernSelect_RightToLeftMirrorsCollapsedAndPopupContentWithoutChangingValue()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 520, 240) };
            using var select = new ModernSelect { Bounds = new Rectangle(180, 50, 220, 34) };
            select.Items.AddRange(["Camera A", "Camera B"]);
            select.SelectedIndex = 1;
            host.Controls.Add(select);
            host.Show();

            using var ltrControl = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(ltrControl, select.ClientRectangle);
            select.DroppedDown = true;
            PumpMessages();
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            using var ltrPopup = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(ltrPopup, popup.ClientRectangle);
            select.DroppedDown = false;

            host.RightToLeft = RightToLeft.Yes;
            host.RightToLeftLayout = true;
            select.RightToLeft = RightToLeft.Yes;
            using var rtlControl = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(rtlControl, select.ClientRectangle);
            select.DroppedDown = true;
            PumpMessages();
            popup = Assert.Single(host.OwnedForms, form => form.Visible);
            using var rtlPopup = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(rtlPopup, popup.ClientRectangle);

            Assert.Equal(1, select.SelectedIndex);
            Assert.True(CountDifferentPixels(ltrControl, rtlControl) > 100);
            Assert.True(CountDifferentPixels(ltrPopup, rtlPopup) > 100);
        });
    }

    [Fact]
    public void ModernSelectMultiple_RightToLeftMirrorsTagsAndPopupRowsWithoutChangingValues()
    {
        RunInSta(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(120, 120, 640, 240) };
            using var select = new ModernSelectMultiple
            {
                Bounds = new Rectangle(60, 50, 420, 34),
                DisplayMode = MultipleSelectionDisplayMode.Tags
            };
            select.Items.AddRange(["Alpha", "Beta", "Gamma"]);
            select.SetItemChecked(0, true);
            select.SetItemChecked(2, true);
            host.Controls.Add(select);
            host.Show();

            using var ltrControl = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(ltrControl, select.ClientRectangle);
            select.DroppedDown = true;
            PumpMessages();
            var popup = Assert.Single(host.OwnedForms, form => form.Visible);
            using var ltrPopup = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(ltrPopup, popup.ClientRectangle);
            select.DroppedDown = false;

            host.RightToLeft = RightToLeft.Yes;
            host.RightToLeftLayout = true;
            select.RightToLeft = RightToLeft.Yes;
            using var rtlControl = new Bitmap(select.Width, select.Height);
            select.DrawToBitmap(rtlControl, select.ClientRectangle);
            select.DroppedDown = true;
            PumpMessages();
            popup = Assert.Single(host.OwnedForms, form => form.Visible);
            using var rtlPopup = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(rtlPopup, popup.ClientRectangle);

            Assert.Equal(new object[] { "Alpha", "Gamma" }, select.SelectedValues);
            Assert.True(CountDifferentPixels(ltrControl, rtlControl) > 100);
            Assert.True(CountDifferentPixels(ltrPopup, rtlPopup) > 100);
        });
    }

    [Fact]
    public void ModernSelect_MouseClickTogglesDropDown()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 100) };
            using var select = new ClickableModernSelect { Width = 200 };
            select.Items.AddRange(["Camera 1", "Camera 2"]);
            host.Controls.Add(select);
            host.Show();
            Application.DoEvents();

            select.InvokeClick();
            Assert.True(select.DroppedDown);
            select.InvokeClick();
            Assert.False(select.DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void ModernSelectMultiple_ToggleDropDownOpensAndCloses()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 100) };
            using var select = new ModernSelectMultiple { Width = 200 };
            select.Items.AddRange(["Camera 1", "Camera 2"]);
            host.Controls.Add(select);
            host.Show();
            Application.DoEvents();
            select.ToggleDropDown();
            Assert.True(select.DroppedDown);
            select.ToggleDropDown();
            Assert.False(select.DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void ModernDataGridView_ModernComboBoxColumnFirstClickBeginsEditAndOpensDropDown()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 140) };
            using var grid = new ModernDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            grid.Columns.Add(new ModernDataGridViewComboBoxColumn
            {
                DataSource = new[] { "Literal", "Binding" }
            });
            grid.Rows.Add("Literal");
            host.Controls.Add(grid);
            host.Show();
            host.Activate();
            grid.Focus();
            Application.DoEvents();

            var cell = grid.GetCellDisplayRectangle(0, 0, false);
            ClickControl(grid, new Point(cell.Left + cell.Width / 2, cell.Top + cell.Height / 2));
            Application.DoEvents();

            var editing = grid.IsCurrentCellInEditMode;
            var editor = grid.EditingControl as ModernSelect;
            var opened = editor?.DroppedDown == true;
            if (editor is not null) editor.DroppedDown = false;
            if (editing) grid.EndEdit();
            host.Close();

            Assert.True(editing);
            Assert.NotNull(editor);
            Assert.True(opened);
        });
    }

    [Fact]
    public void Select_KeyboardNavigation_CommitsChoiceAndRestoresFocus()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 100) };
            using var select = new ModernSelect { Width = 200 };
            select.Items.AddRange(["Camera 1", "Camera 2"]);
            host.Controls.Add(select);
            host.Show();
            host.Activate();
            select.Focus();
            Application.DoEvents();

            PostKey(select, Keys.Down);
            Assert.True(select.DroppedDown);
            PostKey(select, Keys.Down);
            PostKey(select, Keys.Enter);

            Assert.Equal(0, select.SelectedIndex);
            Assert.Equal("Camera 1", select.SelectedItem);
            Assert.False(select.DroppedDown);
            Assert.True(select.Focused);
            host.Close();
        });
    }

    [Fact]
    public void Select_KeyboardNavigation_HomeEndAndPageKeysUseVisibleRowCount()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 100) };
            using var select = new ModernSelect { Width = 200, MaxDropDownItems = 4 };
            select.Items.AddRange(Enumerable.Range(0, 10).Select(index => $"Item {index}").ToArray());
            select.SelectedIndex = 2;
            host.Controls.Add(select);
            host.Show();
            host.Activate();
            select.Focus();
            Application.DoEvents();
            Cursor.Position = host.PointToScreen(new Point(host.ClientSize.Width + 40, host.ClientSize.Height + 40));

            CommitWithKey(select, Keys.End);
            Assert.Equal(9, select.SelectedIndex);
            CommitWithKey(select, Keys.Home);
            Assert.Equal(0, select.SelectedIndex);
            CommitWithKey(select, Keys.PageDown);
            Assert.Equal(4, select.SelectedIndex);
            CommitWithKey(select, Keys.PageUp);
            Assert.Equal(0, select.SelectedIndex);
            host.Close();
        });
    }

    [Fact]
    public void SelectMultiple_KeyboardNavigation_TogglesChoiceAndEscapeCloses()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 100) };
            using var select = new ModernSelectMultiple { Width = 200 };
            select.Items.AddRange(["Camera 1", "Camera 2"]);
            host.Controls.Add(select);
            host.Show();
            host.Activate();
            select.Focus();
            Application.DoEvents();

            PostKey(select, Keys.Down);
            Assert.True(select.DroppedDown);
            PostKey(select, Keys.Down);
            PostKey(select, Keys.Space);

            Assert.Equal(["Camera 1"], select.SelectedItems);
            Assert.True(select.DroppedDown);

            PostKey(select, Keys.Escape);
            Assert.False(select.DroppedDown);
            Assert.True(select.Focused);
            host.Close();
        });
    }

    [Fact]
    public void SelectAccessibility_ExposesValueExpandedStateAndDefaultAction()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(480, 100) };
            using var select = new ModernSelect { Width = 200, AccessibleName = "Camera" };
            using var multiple = new ModernSelectMultiple { Left = 220, Width = 240, AccessibleName = "Outputs" };
            select.Items.AddRange(["Camera 1", "Camera 2"]);
            select.SelectedIndex = 0;
            multiple.Items.AddRange(["Image", "Result"]);
            multiple.SetItemChecked(0, true);
            host.Controls.AddRange([select, multiple]);
            host.Show();
            Application.DoEvents();

            Assert.Equal("Camera 1", select.AccessibilityObject.Value);
            Assert.True(select.AccessibilityObject.State.HasFlag(AccessibleStates.Collapsed));
            Assert.Equal("Image", multiple.AccessibilityObject.Value);
            Assert.True(multiple.AccessibilityObject.State.HasFlag(AccessibleStates.Collapsed));

            select.AccessibilityObject.DoDefaultAction();
            Assert.True(select.DroppedDown);
            Assert.True(select.AccessibilityObject.State.HasFlag(AccessibleStates.Expanded));

            select.AccessibilityObject.DoDefaultAction();
            Assert.False(select.DroppedDown);
            Assert.True(select.AccessibilityObject.State.HasFlag(AccessibleStates.Collapsed));

            multiple.AccessibilityObject.DoDefaultAction();
            Assert.True(multiple.DroppedDown);
            Assert.True(multiple.AccessibilityObject.State.HasFlag(AccessibleStates.Expanded));
            multiple.AccessibilityObject.DoDefaultAction();
            Assert.False(multiple.DroppedDown);
            host.Close();
        });
    }

    [Fact]
    public void ModernTextArea_BothAxesRouteWheelToAvailableScrollRange()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(300, 180), ShowInTaskbar = false };
            using var area = new ModernTextArea
            {
                Size = new Size(220, 90),
                WordWrap = false,
                ScrollBars = ScrollBars.Both,
                Text = "A very long horizontal line that exceeds the editor width\r\nLine 2\r\nLine 3\r\nLine 4\r\nLine 5\r\nLine 6"
            };
            host.Controls.Add(area);
            host.Show();
            Application.DoEvents();

            Assert.Equal(area.InnerTextBox.ClientSize.Height - SystemInformation.HorizontalScrollBarHeight,
                area.TextViewportBounds.Height);
            Assert.Equal(area.InnerTextBox.ClientSize.Width - SystemInformation.VerticalScrollBarWidth,
                area.TextViewportBounds.Width);

            const uint EmGetFirstVisibleLine = 0x00CE;
            var verticalBefore = SendMessage(area.InnerTextBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
            PostMouseWheel(area.InnerTextBox, -120);
            Assert.True(SendMessage(area.InnerTextBox.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32() > verticalBefore);

            area.Text = "A very long horizontal line that exceeds the editor width";
            Application.DoEvents();
            var horizontalBefore = area.ScrollPosition.X;
            PostMouseWheel(area.InnerTextBox, -120);
            Assert.True(area.ScrollPosition.X > horizontalBefore);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_ModernScrollChromeDoesNotExposeNativeRectangleBars()
    {
        RunInSta(() =>
        {
            const int windowHorizontalScrollStyle = 0x00100000;
            const int windowVerticalScrollStyle = 0x00200000;
            using var host = new Form { ClientSize = new Size(310, 220), ShowInTaskbar = false };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill };
            var root = tree.Nodes.Add("Compiled PLC System with a deliberately wide root node");
            var group = root.Nodes.Add("Compiled ScanGroups with deeply indented content");
            for (var index = 0; index < 30; index++)
                group.Nodes.Add($"Batch {index:00} · HoldingRegister 400{index:000} · diagnostic text");
            root.Expand();
            group.Expand();
            host.Controls.Add(tree);
            host.Show();
            PumpMessages();

            Assert.Equal(96, tree.DeviceDpi);
            Assert.Contains(tree.Controls.Cast<Control>(), control => control.Cursor == Cursors.SizeNS && control.Visible);
            Assert.Contains(tree.Controls.Cast<Control>(), control => control.Cursor == Cursors.SizeWE && control.Visible);
            AssertNativeRectangleBarsHidden("initial layout");

            tree.Width--;
            tree.Nodes.Add("Another wide node forces native extent recalculation");
            PumpMessages();
            AssertNativeRectangleBarsHidden("resize and content update");

            void AssertNativeRectangleBarsHidden(string stage)
            {
                var styles = GetWindowLong(tree.Handle, -16);
                Assert.True((styles & (windowHorizontalScrollStyle | windowVerticalScrollStyle)) == 0,
                    $"Native rectangular TreeView bars were restored during {stage}: style=0x{styles:X8}.");
                Assert.Equal(tree.Size, tree.ClientSize);
            }
        });
    }

    [Fact]
    public void ModernTreeView_CollapsedContentWithoutVisibleOverflowHidesVerticalChrome()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(310, 400), ShowInTaskbar = false };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill };
            var root = tree.Nodes.Add("PLC System · 1 Channel / 14 Tag");
            var channel = root.Nodes.Add("Channel · DemoModbusChannel [modbus.tcp]");
            for (var index = 0; index < 30; index++) channel.Nodes.Add($"Tag {index:00}");
            root.Nodes.Add("ScanClasses · 2");
            root.Expand();
            host.Controls.Add(tree);
            host.Show();
            PumpMessages();

            Assert.True(3 * tree.ItemHeight < tree.ClientSize.Height);
            var verticalChrome = tree.Controls.Cast<Control>().Single(control => control.Cursor == Cursors.SizeNS);
            Assert.False(verticalChrome.Visible);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_HiddenNativeScrollBarStillRespondsToMouseWheel()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 160), ShowInTaskbar = false };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill };
            for (var index = 0; index < 20; index++) tree.Nodes.Add($"Node {index}");
            host.Controls.Add(tree);
            host.Show();
            Application.DoEvents();
            var first = tree.TopNode;

            PostMouseWheel(tree, -120);
            Assert.NotSame(first, tree.TopNode);

            tree.TopNode = tree.Nodes[0];
            var scrollSurface = tree.Controls.Cast<Control>().Single(control => control.Visible);
            DragControl(scrollSurface, new Point(scrollSurface.Width / 2, 14),
                new Point(scrollSurface.Width / 2, Math.Max(30, scrollSurface.Height - 20)));
            Assert.NotSame(tree.Nodes[0], tree.TopNode);
            host.Close();
        });
    }

    [Fact]
    public void ModernTreeView_VerticalScrollPreservesHorizontalPositionForDeepNodes()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(260, 170), ShowInTaskbar = false };
            using var tree = new ModernTreeView { Dock = DockStyle.Fill };
            var root = tree.Nodes.Add("Compiled PLC System with horizontally scrollable diagnostics");
            var group = root.Nodes.Add("Compiled ScanGroups with deeply indented content");
            for (var index = 0; index < 24; index++)
                group.Nodes.Add($"Batch {index:00} · HoldingRegister 400{index:000} · clipped diagnostic text");
            root.Expand();
            group.Expand();
            host.Controls.Add(tree);
            host.Show();
            PumpMessages();
            Assert.Equal(tree.Width, tree.ClientSize.Width);
            Assert.Equal(tree.Height, tree.ClientSize.Height);
            SendMessage(tree.Handle, 0x0114, (IntPtr)6, IntPtr.Zero);
            PumpMessages();
            var first = tree.TopNode;
            var horizontalBefore = GetScrollPos(tree.Handle, 0);
            var visited = new HashSet<TreeNode?> { first };

            for (var index = 0; index < 5; index++)
            {
                SendMessage(tree.Handle, 0x020A, (IntPtr)(-120 << 16), IntPtr.Zero);
                PumpMessages();
                visited.Add(tree.TopNode);
            }

            Assert.True(visited.Count >= 4, $"Vertical wheel stopped after {visited.Count - 1} distinct positions.");
            Assert.Equal(horizontalBefore, GetScrollPos(tree.Handle, 0));

            for (var index = 0; index < 40; index++)
                SendMessage(tree.Handle, 0x020A, (IntPtr)(-120 << 16), IntPtr.Zero);
            PumpMessages();
            Assert.Equal(horizontalBefore, GetScrollPos(tree.Handle, 0));
            Assert.Equal(tree.Width, tree.ClientSize.Width);
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_IdleAtMiddleOffsetDoesNotContinuouslyRepaint()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220) };
            using var viewport = new ModernScrollView { Dock = DockStyle.Fill };
            using var content = new Panel { Size = new Size(340, 1600) };
            viewport.Content = content;
            host.Controls.Add(viewport);
            host.Show();
            viewport.ScrollOffset = 700;
            PumpMessages();

            var paints = 0;
            viewport.Paint += (_, _) => paints++;
            content.Paint += (_, _) => paints++;
            for (var index = 0; index < 5; index++) PumpMessages();

            Assert.InRange(paints, 0, 2);
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_CoalescesBurstContentChangesIntoOnePreferredSizeMeasurement()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var viewport = new ModernScrollView { Dock = DockStyle.Fill };
            using var content = new PreferredSizeProbePanel { Size = new Size(340, 900) };
            viewport.Content = content;
            host.Controls.Add(viewport);
            host.Show();
            PumpMessages();
            content.MeasurementCount = 0;

            for (var index = 0; index < 20; index++)
            {
                content.Height = 900 + index;
                content.PerformLayout();
            }

            Assert.Equal(0, content.MeasurementCount);
            PumpMessages();
            Assert.InRange(content.MeasurementCount, 1, 2);
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_DefaultThumbMetricsUseCapsuleStyle()
    {
        RunInSta(() =>
        {
            using var scroll = new ModernScrollView();

            Assert.Equal(6, scroll.ScrollBarWidth);
            Assert.Equal(8, scroll.ScrollBarHoverWidth);
            Assert.Equal(28, scroll.MinimumThumbLength);
            Assert.True(scroll.LiveScrollDuringThumbDrag);
            Assert.True(scroll.UseCompositedScrolling);
        });
    }

    [Fact]
    public void ModernScrollView_DefaultThumbDragDefersNativeContentMovementUntilMouseUp()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(320, 220), ShowInTaskbar = false };
            using var scroll = new ModernScrollView
            {
                Dock = DockStyle.Fill,
                LiveScrollDuringThumbDrag = false
            };
            var content = new Panel { AutoSize = true };
            content.Controls.Add(new Label { Location = new Point(12, 620), Text = "Bottom" });
            scroll.Content = content;
            host.Controls.Add(scroll);
            host.Show();
            Application.DoEvents();

            var down = new Point(scroll.ClientSize.Width - 10, 10);
            var moved = new Point(down.X, 110);
            static IntPtr Pack(Point point) => (IntPtr)((point.Y << 16) | (point.X & 0xffff));
            SendMessage(scroll.Handle, 0x0201, (IntPtr)1, Pack(down));
            SendMessage(scroll.Handle, 0x0200, (IntPtr)1, Pack(moved));

            Assert.Equal(0, scroll.ScrollOffset);
            Assert.Equal(0, content.Top);

            SendMessage(scroll.Handle, 0x0202, IntPtr.Zero, Pack(moved));
            Application.DoEvents();

            Assert.True(scroll.ScrollOffset > 0);
            Assert.Equal(-scroll.ScrollOffset, content.Top);
            host.Close();
        });
    }

    [Fact]
    public void AutoScrollPage_WheelOverNonScrollableModernListContinuesScrollingThePage()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var list = new ModernListBox { Location = new Point(12, 12), Size = new Size(260, 100) };
            list.Items.AddRange(["One", "Two"]);
            page.Controls.Add(list);
            page.Controls.Add(new Label { Location = new Point(12, 620), Text = "Page bottom" });
            host.Controls.Add(page);
            host.Show();
            Application.DoEvents();

            PostMouseWheel(list, -120);

            Assert.True(-page.AutoScrollPosition.Y > 0,
                "A modern list without a scroll range must forward wheel input to an AutoScroll page.");
            host.Close();
        });
    }

    [Fact]
    public void AutoScrollPage_WheelOverNonScrollableModernListViewContinuesScrollingThePage()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var list = new ModernListView { Location = new Point(12, 12), Size = new Size(260, 100) };
            list.Columns.Add("Name", 220);
            list.Items.AddRange([new ListViewItem("One"), new ListViewItem("Two")]);
            page.Controls.Add(list);
            page.Controls.Add(new Label { Location = new Point(12, 620), Text = "Page bottom" });
            host.Controls.Add(page);
            host.Show();
            Application.DoEvents();

            PostMouseWheel(list, -120);

            Assert.True(-page.AutoScrollPosition.Y > 0,
                "A modern list view without a scroll range must forward wheel input to an AutoScroll page.");
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_WheelOverNonScrollableListContinuesScrollingThePage()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var scroll = new ModernScrollView { Dock = DockStyle.Fill };
            var content = new Panel { AutoSize = true };
            var list = new ModernListBox { Location = new Point(12, 12), Size = new Size(260, 100) };
            list.Items.AddRange(["One", "Two"]);
            content.Controls.Add(list);
            content.Controls.Add(new Label { Location = new Point(12, 620), Text = "Page bottom" });
            scroll.Content = content;
            host.Controls.Add(scroll);
            host.Show();
            Application.DoEvents();

            Assert.Equal(0, scroll.ScrollOffset);
            PostMouseWheel(list, -120);

            Assert.True(scroll.ScrollOffset > 0,
                "A list without a scroll range must not consume the containing page's mouse wheel input.");
            Assert.Equal(-scroll.ScrollOffset, content.Top);
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_ScrollableListConsumesWheelBeforeThePage()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var scroll = new ModernScrollView { Dock = DockStyle.Fill };
            var content = new Panel { AutoSize = true };
            var list = new ModernListBox { Location = new Point(12, 12), Size = new Size(260, 100) };
            for (var index = 0; index < 20; index++) list.Items.Add($"Item {index}");
            content.Controls.Add(list);
            content.Controls.Add(new Label { Location = new Point(12, 620), Text = "Page bottom" });
            scroll.Content = content;
            host.Controls.Add(scroll);
            host.Show();
            Application.DoEvents();

            PostMouseWheel(list, -120);

            Assert.True(list.TopIndex > 0,
                $"Expected the list to scroll first; TopIndex={list.TopIndex}, page offset={scroll.ScrollOffset}.");
            Assert.Equal(0, scroll.ScrollOffset);

            list.TopIndex = list.Items.Count - 1;
            PostMouseWheel(list, -120);
            Assert.True(scroll.ScrollOffset > 0,
                "Once the nested list reaches its boundary, the same wheel direction must continue on the page.");
            host.Close();
        });
    }

    [Fact]
    public void ModernScrollView_WheelOverNonScrollableListViewContinuesScrollingThePage()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(360, 220), ShowInTaskbar = false };
            using var scroll = new ModernScrollView { Dock = DockStyle.Fill };
            var content = new Panel { AutoSize = true };
            var list = new ModernListView { Location = new Point(12, 12), Size = new Size(260, 100) };
            list.Columns.Add("Name", 220);
            list.Items.AddRange([new ListViewItem("One"), new ListViewItem("Two")]);
            content.Controls.Add(list);
            content.Controls.Add(new Label { Location = new Point(12, 620), Text = "Page bottom" });
            scroll.Content = content;
            host.Controls.Add(scroll);
            host.Show();
            Application.DoEvents();

            Assert.Equal(0, scroll.ScrollOffset);
            PostMouseWheel(list, -120);

            Assert.True(scroll.ScrollOffset > 0,
                "A list view without a scroll range must not consume the containing page's mouse wheel input.");
            host.Close();
        });
    }

    [Fact]
    public void InteractiveControls_ExposeSemanticAccessibilityRoles()
    {
        RunInSta(() =>
        {
            using var button = new ModernButton();
            using var checkbox = new ModernCheckbox();
            using var toggle = new ModernSwitch();
            using var input = new ModernInput();
            using var number = new ModernInputNumber();
            using var select = new ModernSelect();
            using var multiple = new ModernSelectMultiple();

            Assert.Equal(AccessibleRole.PushButton, button.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.CheckButton, checkbox.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.CheckButton, toggle.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.Text, input.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.SpinButton, number.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.ComboBox, select.AccessibilityObject.Role);
            Assert.Equal(AccessibleRole.ComboBox, multiple.AccessibilityObject.Role);
        });
    }

    [Fact]
    public void KeyboardFocusableControls_ShowFocusBorderByDefault()
    {
        RunInSta(() =>
        {
            using var button = new ModernButton();
            using var toggle = new ModernSwitch();
            using var select = new ModernSelect();
            using var multiple = new ModernSelectMultiple();

            Assert.True(button.ShowFocusBorder);
            Assert.True(toggle.ShowFocusBorder);
            Assert.True(select.ShowFocusBorder);
            Assert.True(multiple.ShowFocusBorder);
        });
    }

    [Fact]
    public void CheckControls_ExposeCheckedAndMixedAccessibilityStates()
    {
        RunInSta(() =>
        {
            using var checkbox = new ModernCheckbox { ThreeState = true, CheckState = CheckState.Indeterminate };
            using var toggle = new ModernSwitch { Checked = true };

            Assert.True(checkbox.AccessibilityObject.State.HasFlag(AccessibleStates.Mixed));
            Assert.True(toggle.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        });
    }

    [Fact]
    public void Value_AtRangeBoundary_DisablesUnavailableStepButton()
    {
        RunInSta(() =>
        {
            using var input = new ModernInputNumber { Minimum = 0, Maximum = 1, Value = 0 };
            var decrease = input.Controls.OfType<ModernButton>().Single(button => button.AccessibleName == "减少");
            var increase = input.Controls.OfType<ModernButton>().Single(button => button.AccessibleName == "增加");

            Assert.False(decrease.Enabled);
            Assert.True(increase.Enabled);

            input.Value = 1;

            Assert.True(decrease.Enabled);
            Assert.False(increase.Enabled);
        });
    }

    [Fact]
    public void ModernProgressAndSpinner_ClampValuesAndExposeBusyState()
    {
        RunInSta(() =>
        {
            using var progress = new ModernProgressBar { Minimum = 10, Maximum = 20, Value = 30 };
            using var spinner = new ModernSpinner { Spinning = true, Text = "Loading" };
            Assert.Equal(20, progress.Value);
            Assert.Equal(AccessibleRole.ProgressBar, progress.AccessibilityObject.Role);
            Assert.True(spinner.Spinning);
            Assert.Equal("Loading", spinner.AccessibilityObject.Name);
        });
    }

    [Fact]
    public void ProgressAndSlider_ValueTextOccupiesASeparateRowAboveTheTrack()
    {
        RunInSta(() =>
        {
            using var progress = new ModernProgressBar { Size = new Size(220, 40), Value = 68, ShowPercentage = true };
            using var slider = new ModernSlider { Size = new Size(220, 40), Value = 91, ShowValue = true };

            AssertTextAboveTrack(progress, progress.Theme.Text, progress.Theme.Primary);
            AssertTextAboveTrack(slider, slider.Theme.Text, slider.Theme.Primary);
        });
    }

    [Fact]
    public void ContinuousAnimationsPauseOutsideTheVisibleScrollViewport()
    {
        RunInSta(() =>
        {
            using var host = new Form { ClientSize = new Size(340, 180) };
            using var viewport = new ModernScrollView { Dock = DockStyle.Fill };
            using var content = new Panel { Size = new Size(320, 720) };
            using var progress = new ModernProgressBar { Top = 600, Width = 220, Indeterminate = true };
            using var spinner = new ModernSpinner { Top = 640, Width = 160, Spinning = true };
            content.Controls.AddRange([progress, spinner]);
            viewport.Content = content;
            host.Controls.Add(viewport);
            host.Show();
            PumpMessages();

            var invalidations = 0;
            progress.Invalidated += (_, _) => invalidations++;
            spinner.Invalidated += (_, _) => invalidations++;
            for (var index = 0; index < 4; index++) PumpMessages();
            Assert.Equal(0, invalidations);

            viewport.ScrollOffset = 580;
            for (var index = 0; index < 4; index++) PumpMessages();
            Assert.True(invalidations > 0, "Expected animations to resume after entering the viewport.");
            host.Close();
        });
    }

    [Fact]
    public void ModernBadge_FormatsOverflowAndZeroVisibility()
    {
        RunInSta(() =>
        {
            using var badge = new ModernBadge { Count = 120, OverflowCount = 99 };
            Assert.Equal("99+", badge.DisplayText);
            badge.Count = 0;
            Assert.Equal(string.Empty, badge.DisplayText);
            badge.ShowZero = true;
            Assert.Equal("0", badge.DisplayText);
        });
    }

    [Fact]
    public void ModernSlider_KeyboardChangesAndCommitsValue()
    {
        RunInSta(() =>
        {
            using var host = new Form();
            using var slider = new ModernSlider { Minimum = 0, Maximum = 10, Step = 2, Value = 4 };
            host.Controls.Add(slider);
            host.Show();
            slider.Focus();
            var commits = 0;
            slider.ValueCommitted += (_, _) => commits++;
            PostKey(slider, Keys.Right);
            Assert.Equal(6, slider.Value);
            Assert.Equal(1, commits);
            host.Close();
        });
    }

    [Fact]
    public void ModernCollapsiblePanel_HidesContentAndRaisesExpandedChanged()
    {
        RunInSta(() =>
        {
            using var panel = new ModernCollapsiblePanel();
            using var content = new Label { Text = "Advanced" };
            panel.Content = content;
            var changes = 0;
            panel.ExpandedChanged += (_, _) => changes++;
            panel.Expanded = false;
            Assert.False(content.Visible);
            Assert.Equal(1, changes);
        });
    }

    [Fact]
    public void ModernCollapsiblePanel_HeaderIconRendersInTheHeader()
    {
        RunInSta(() =>
        {
            using var panel = new ModernCollapsiblePanel
            {
                Size = new Size(240, 80),
                HeaderIcon = ModernIconKind.Plus
            };
            using var bitmap = new Bitmap(panel.Width, panel.Height);
            panel.DrawToBitmap(bitmap, panel.ClientRectangle);

            var header = new Rectangle(0, 0, panel.Width, Math.Min(panel.Height, panel.HeaderHeight));
            Assert.Contains(Pixels(bitmap, header), color =>
                Math.Abs(color.R - panel.Theme.Primary.R) +
                Math.Abs(color.G - panel.Theme.Primary.G) +
                Math.Abs(color.B - panel.Theme.Primary.B) <= 100);
        });
    }

    [Fact]
    public void ModernPagination_ClampsPageAndRaisesPageChanged()
    {
        RunInSta(() =>
        {
            using var pagination = new ModernPagination { TotalCount = 95, PageSize = 10, PageIndex = 1 };
            var changes = 0;
            pagination.PageChanged += (_, _) => changes++;
            pagination.MoveLast();
            Assert.Equal(10, pagination.PageIndex);
            Assert.Equal(1, changes);
            pagination.MoveNext();
            Assert.Equal(10, pagination.PageIndex);
        });
    }

    [Fact]
    public void ModernContextMenuAndStatusBar_ApplyThemeWithoutReplacingNativeItems()
    {
        RunInSta(() =>
        {
            using var menu = new ModernContextMenu();
            using var status = new ModernStatusBar();
            menu.Items.Add("Open");
            status.Items.Add("Ready");
            menu.Theme = ModernTheme.Dark;
            status.Theme = ModernTheme.Dark;
            Assert.Single(menu.Items);
            Assert.Single(status.Items);
            Assert.Equal(ModernTheme.Dark.Control, menu.BackColor);
            Assert.Equal(ModernTheme.Dark.Container, status.BackColor);
        });
    }

    private static void CommitWithKey(ModernSelect select, Keys key)
    {
        PostKey(select, Keys.Enter);
        Assert.True(select.DroppedDown);
        PumpMessages();
        PostKey(select, key);
        PostKey(select, Keys.Enter);
        Assert.False(select.DroppedDown);
    }

    private static void PostKey(Control control, Keys key)
    {
        WaitForNoKeyboardModifiers();
        const uint WmKeyDown = 0x0100;
        const uint WmKeyUp = 0x0101;
        PostMessage(control.Handle, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
        PostMessage(control.Handle, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
        var deadline = Environment.TickCount64 + 100;
        do
        {
            Application.DoEvents();
            Thread.Sleep(5);
        } while (Environment.TickCount64 < deadline);
    }

    private static void PostMouseWheel(Control control, short delta)
    {
        const uint WmMouseWheel = 0x020A;
        var wheelParameter = (IntPtr)(delta << 16);
        PostMessage(control.Handle, WmMouseWheel, wheelParameter, IntPtr.Zero);
        PumpMessages();
    }

    private static void PumpMessages()
    {
        var deadline = Environment.TickCount64 + 100;
        do
        {
            Application.DoEvents();
            Thread.Sleep(5);
        } while (Environment.TickCount64 < deadline);
    }

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr handle, int bar);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    private static IEnumerable<AccessibleObject> EnumerateAccessibilityTree(AccessibleObject root)
    {
        yield return root;
        for (var index = 0; index < root.GetChildCount(); index++)
        {
            var child = root.GetChild(index);
            if (child is null) continue;
            foreach (var descendant in EnumerateAccessibilityTree(child)) yield return descendant;
        }
    }

    private static void AssertTextAboveTrack(Control control, Color textColor, Color trackColor)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        var textRows = new List<int>();
        var trackRows = new List<int>();
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - textColor.R) + Math.Abs(pixel.G - textColor.G) + Math.Abs(pixel.B - textColor.B) <= 18)
                textRows.Add(y);
            if (pixel.ToArgb() == trackColor.ToArgb()) trackRows.Add(y);
        }

        Assert.NotEmpty(textRows);
        Assert.NotEmpty(trackRows);
        Assert.True(textRows.Max() < trackRows.Min(), $"Text ended at y={textRows.Max()}, but track started at y={trackRows.Min()}.");
    }

    private static int PrimaryPixelAverageX(Control control, int y, Color primary) =>
        ColorPixelAverageX(control, y, primary);

    private static int ColorPixelAverageX(Control control, int y, Color expected)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, control.ClientRectangle);
        var positions = Enumerable.Range(0, bitmap.Width)
            .Where(x => bitmap.GetPixel(x, Math.Clamp(y, 0, bitmap.Height - 1)).ToArgb() == expected.ToArgb())
            .ToArray();
        Assert.NotEmpty(positions);
        return (int)positions.Average();
    }

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in DescendantsAndSelf(child))
                yield return descendant;
    }

    private static IEnumerable<IntPtr> EnumerateTopLevelWindowsForCurrentThread()
    {
        var windows = new List<IntPtr>();
        _ = EnumThreadWindows(GetCurrentThreadId(), (window, _) => { windows.Add(window); return true; }, IntPtr.Zero);
        return windows;
    }

    private static string GetWindowClassName(IntPtr window)
    {
        var className = new System.Text.StringBuilder(128);
        _ = GetClassName(window, className, className.Capacity);
        return className.ToString();
    }

    private static bool HasRoundedWindowRegion(IntPtr window)
    {
        var region = CreateRectRgn(0, 0, 1, 1);
        if (region == IntPtr.Zero) return false;
        try
        {
            if (GetWindowRgn(window, region) <= 0) return false;
            var deviceContext = GetDC(window);
            if (deviceContext == IntPtr.Zero) return false;
            try { return !PtVisible(deviceContext, 0, 0); }
            finally { _ = ReleaseDC(window, deviceContext); }
        }
        finally { _ = DeleteObject(region); }
    }

    private static Bitmap CaptureVisibleClient(Control control)
    {
        var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var destination = graphics.GetHdc();
        var source = GetDC(control.Handle);
        try { Assert.True(BitBlt(destination, 0, 0, bitmap.Width, bitmap.Height, source, 0, 0, 0x00CC0020)); }
        finally { _ = ReleaseDC(control.Handle, source); graphics.ReleaseHdc(destination); }
        return bitmap;
    }

    private static Bitmap CaptureControl(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var deviceContext = graphics.GetHdc();
        try { Assert.True(PrintWindow(control.Handle, deviceContext, 2)); }
        finally { graphics.ReleaseHdc(deviceContext); }
        return bitmap;
    }

    private static int CountDifferentPixels(Bitmap first, Bitmap second)
    {
        var count = 0;
        for (var y = 0; y < Math.Min(first.Height, second.Height); y++)
        for (var x = 0; x < Math.Min(first.Width, second.Width); x++)
            if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) count++;
        return count;
    }

    private static void PumpFor(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static Color CaptureWindowPixel(Control control, Point point)
    {
        using var bitmap = CaptureControl(control);
        return bitmap.GetPixel(point.X, point.Y);
    }

    private static IEnumerable<Color> Pixels(Bitmap bitmap, Rectangle bounds)
    {
        for (var y = bounds.Top; y < bounds.Bottom; y++)
            for (var x = bounds.Left; x < bounds.Right; x++)
                yield return bitmap.GetPixel(x, y);
    }

    private static TextBox InnerTextBoxOf(ModernInputNumber input) =>
        input.Controls.OfType<ModernInput>().Single().InnerTextBox;

    private static Point CalendarDateCenter(Control calendar, DateTime date, CultureInfo culture)
    {
        var firstOfMonth = new DateTime(date.Year, date.Month, 1);
        var firstOffset = ((int)firstOfMonth.DayOfWeek - (int)culture.DateTimeFormat.FirstDayOfWeek + 7) % 7;
        var slot = firstOffset + date.Day - 1;
        var header = (int)Math.Round(44d * calendar.DeviceDpi / 96d);
        var cellWidth = calendar.ClientSize.Width / 7d;
        var cellHeight = (calendar.ClientSize.Height - header) / 7d;
        return new Point(
            (int)Math.Round((slot % 7 + .5d) * cellWidth),
            (int)Math.Round(header + (slot / 7 + 1.5d) * cellHeight));
    }

    private static void ClickCalendarDate(Control calendar, DateTime date, CultureInfo culture) =>
        ClickControl(calendar, CalendarDateCenter(calendar, date, culture));

    private static int CountPrimaryRingPixels(Bitmap bitmap, Point center, Color primary, int radius)
    {
        var count = 0;
        for (var angle = 0; angle < 360; angle += 3)
        {
            var radians = angle * Math.PI / 180d;
            var x = Math.Clamp(center.X + (int)Math.Round(Math.Cos(radians) * radius), 0, bitmap.Width - 1);
            var y = Math.Clamp(center.Y + (int)Math.Round(Math.Sin(radians) * radius), 0, bitmap.Height - 1);
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - primary.R) + Math.Abs(pixel.G - primary.G) + Math.Abs(pixel.B - primary.B) <= 30)
                count++;
        }
        return count;
    }

    private static int CountMatchingPixels(Bitmap bitmap, Rectangle bounds, Color expected, int tolerance)
    {
        var count = 0;
        bounds.Intersect(new Rectangle(Point.Empty, bitmap.Size));
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - expected.R) + Math.Abs(pixel.G - expected.G) + Math.Abs(pixel.B - expected.B) <= tolerance)
                count++;
        }
        return count;
    }

    private static int CountNonBackgroundPixels(Bitmap bitmap, Rectangle bounds, Color background, int tolerance)
    {
        var count = 0;
        bounds.Intersect(new Rectangle(Point.Empty, bitmap.Size));
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) +
                Math.Abs(pixel.B - background.B) > tolerance) count++;
        }
        return count;
    }

    private static int CountTextInkComponents(string text, Font font)
    {
        var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        using var bitmap = new Bitmap(size.Width + 8, size.Height + 8);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Black);
        TextRenderer.DrawText(graphics, text, font, new Rectangle(4, 4, size.Width, size.Height), Color.White,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return CountColumnRuns(bitmap, Color.Black);
    }

    private static int CountTextInkComponents(Bitmap bitmap, Color background) => CountColumnRuns(bitmap, background);

    private static int CountColumnRuns(Bitmap bitmap, Color background)
    {
        var runs = 0;
        var inRun = false;
        for (var x = 0; x < bitmap.Width; x++)
        {
            var hasInk = false;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) +
                    Math.Abs(pixel.B - background.B) > 120) { hasInk = true; break; }
            }
            if (hasInk && !inRun) runs++;
            inRun = hasInk;
        }
        return runs;
    }

    private static Rectangle FindScrollThumbBounds(Bitmap bitmap, Color background)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (Math.Abs(pixel.R - background.R) + Math.Abs(pixel.G - background.G) +
                Math.Abs(pixel.B - background.B) <= 12) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static Point FindScrollThumbCenter(Control overlay, Color background)
    {
        using var bitmap = CaptureControl(overlay);
        var bounds = FindScrollThumbBounds(bitmap, background);
        if (bounds.IsEmpty) throw new InvalidOperationException("Scroll thumb was not rendered.");
        return new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
    }

    private static int ScaleFor(Control control, int logical) =>
        (int)Math.Round(logical * control.DeviceDpi / 96d);

    private static void SendMouseMessage(Control control, uint message, Point screenPoint, int buttons)
    {
        var point = control.PointToClient(screenPoint);
        var packed = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(control.Handle, message, (IntPtr)buttons, packed);
    }

    private static void ClickControl(Control control, Point point)
    {
        const uint WmLButtonDown = 0x0201;
        const uint WmLButtonUp = 0x0202;
        Cursor.Position = control.PointToScreen(point);
        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(control.Handle, WmLButtonDown, (IntPtr)1, lParam);
        SendMessage(control.Handle, WmLButtonUp, IntPtr.Zero, lParam);
        Application.DoEvents();
    }

    private static void PostClickControl(Control control, Point point)
    {
        const uint WmLButtonDown = 0x0201;
        const uint WmLButtonUp = 0x0202;
        Cursor.Position = control.PointToScreen(point);
        var lParam = (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        PostMessage(control.Handle, WmLButtonDown, (IntPtr)1, lParam);
        PostMessage(control.Handle, WmLButtonUp, IntPtr.Zero, lParam);
        PumpMessages();
    }

    private static void DragControl(Control control, Point from, Point to)
    {
        const uint WmLButtonDown = 0x0201;
        const uint WmMouseMove = 0x0200;
        const uint WmLButtonUp = 0x0202;
        static IntPtr Pack(Point point) => (IntPtr)((point.Y << 16) | (point.X & 0xffff));
        SendMessage(control.Handle, WmLButtonDown, (IntPtr)1, Pack(from));
        SendMessage(control.Handle, WmMouseMove, (IntPtr)1, Pack(to));
        SendMessage(control.Handle, WmLButtonUp, IntPtr.Zero, Pack(to));
        Application.DoEvents();
    }

    private static void SendKey(Control control, Keys key)
    {
        WaitForNoKeyboardModifiers();
        const uint WmKeyDown = 0x0100;
        const uint WmKeyUp = 0x0101;
        SendMessage(control.Handle, WmKeyDown, (IntPtr)(int)key, IntPtr.Zero);
        SendMessage(control.Handle, WmKeyUp, (IntPtr)(int)key, IntPtr.Zero);
    }

    private static void WaitForNoKeyboardModifiers()
    {
        var deadline = Environment.TickCount64 + 3000;
        while (Control.ModifierKeys != Keys.None && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Assert.Equal(Keys.None, Control.ModifierKeys);
    }

    private sealed class WindowMessageMonitor : NativeWindow, IDisposable
    {
        private readonly Dictionary<int, int> _counts;
        private readonly Dictionary<int, int> _nonZeroResults;

        public WindowMessageMonitor(IntPtr handle, params int[] messages)
        {
            _counts = messages.Distinct().ToDictionary(message => message, _ => 0);
            _nonZeroResults = messages.Distinct().ToDictionary(message => message, _ => 0);
            AssignHandle(handle);
        }

        public int Count(int message) => _counts.TryGetValue(message, out var count) ? count : 0;
        public int NonZeroResultCount(int message) =>
            _nonZeroResults.TryGetValue(message, out var count) ? count : 0;

        protected override void WndProc(ref Message message)
        {
            var monitored = _counts.ContainsKey(message.Msg);
            if (monitored) _counts[message.Msg]++;
            base.WndProc(ref message);
            if (monitored && message.Result != IntPtr.Zero) _nonZeroResults[message.Msg]++;
        }

        public void Dispose() => ReleaseHandle();
    }

    private delegate bool EnumThreadWindowCallback(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder className, int maximumCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgn(IntPtr window, IntPtr region);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool PtVisible(IntPtr deviceContext, int x, int y);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr handle, IntPtr update, IntPtr region, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr deviceContext, uint flags);

    private sealed class PreferredSizeProbePanel : Panel
    {
        public int MeasurementCount { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            MeasurementCount++;
            return new Size(proposedSize.Width, Height);
        }
    }

    private sealed class ClickableModernSelect : ModernSelect
    {
        public void InvokeClick() => OnClick(EventArgs.Empty);
    }

    private sealed record IconChoice(string Name, IconMetadata Icon);

    private sealed record IconMetadata(string Key);

    private sealed record BoundChoice(int Id, string Name);

    private sealed class MutableBoundChoice(int id, string name)
    {
        public int Id { get; } = id;
        public string Name { get; set; } = name;
    }

    private sealed record NullableBoundChoice(int? Id, string Name);

    private sealed class DynamicDisplayItem(string label) : ICustomTypeDescriptor
    {
        public string Label { get; set; } = label;
        public AttributeCollection GetAttributes() => AttributeCollection.Empty;
        public string? GetClassName() => GetType().Name;
        public string? GetComponentName() => null;
        public TypeConverter GetConverter() => new();
        public EventDescriptor? GetDefaultEvent() => null;
        public PropertyDescriptor? GetDefaultProperty() => null;
        public object? GetEditor(Type editorBaseType) => null;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;
        public PropertyDescriptorCollection GetProperties() => GetProperties(null);
        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) =>
            new([new DynamicLabelPropertyDescriptor()]);
        public object GetPropertyOwner(PropertyDescriptor? descriptor) => this;

        private sealed class DynamicLabelPropertyDescriptor() : PropertyDescriptor("Label", null)
        {
            public override Type ComponentType => typeof(DynamicDisplayItem);
            public override bool IsReadOnly => false;
            public override Type PropertyType => typeof(string);
            public override bool CanResetValue(object component) => false;
            public override object? GetValue(object? component) => ((DynamicDisplayItem)component!).Label;
            public override void ResetValue(object component) { }
            public override void SetValue(object? component, object? value) =>
                ((DynamicDisplayItem)component!).Label = Convert.ToString(value) ?? string.Empty;
            public override bool ShouldSerializeValue(object component) => false;
        }
    }

    private sealed record NestedBoundChoice(ChoiceMetadata? Metadata);

    private sealed record ChoiceMetadata(int Id, string Name);

    private sealed record SameLabelValue(string Id)
    {
        public override string ToString() => "same label";
    }

    private sealed class TestTextCatalog(TextKey key, string value) : ITextCatalog
    {
        public bool TryGetText(TextKey candidate, CultureInfo culture, out string text)
        {
            text = candidate == key ? value : string.Empty;
            return candidate == key;
        }
    }

    private sealed class DesignModeSite : ISite
    {
        public IComponent Component => null!;
        public IContainer? Container => null;
        public bool DesignMode => true;
        public string? Name { get; set; }
        public object? GetService(Type serviceType) => null;
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "WinForms behavior test timed out.");
        Assert.Null(failure);
    }
}
