using System.ComponentModel;

namespace ModernUI.WinForms;

/// <summary>保留原生菜单项、快捷键和 UIA 行为的现代右键菜单。</summary>
[Description("ModernContextMenu 现代上下文菜单")]
[DisplayName("现代上下文菜单")]
[ToolboxBitmap(typeof(ModernContextMenu), "Toolbox.Icons.Commands.bmp")]
[ToolboxItem(true)]
public sealed class ModernContextMenu : ContextMenuStrip
{
    private ModernTheme _theme = ModernUiSettings.DefaultTheme;

    public ModernContextMenu()
    {
        ShowImageMargin = true;
        Padding = new Padding(4);
        ApplyTheme();
    }

    /// <summary>用共享命令替换菜单项。</summary>
    /// <param name="commands">按显示顺序排列的命令。</param>
    public void SetCommands(IEnumerable<ModernCommand> commands)
    {
        ModernCompatibility.ThrowIfNull(commands, nameof(commands));
        Items.Clear();
        foreach (var command in commands)
        {
            if (command.Kind == ModernCommandKind.Separator)
            {
                Items.Add(new ToolStripSeparator());
                continue;
            }
            Items.Add(new CommandMenuItem(command, Theme));
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ModernTheme Theme
    {
        get => _theme;
        set { _theme = value ?? throw new ArgumentNullException(nameof(value)); ApplyTheme(); }
    }

    private sealed class CommandMenuItem : ToolStripMenuItem
    {
        private readonly ModernCommand _command;
        private ModernTheme _theme;

        public CommandMenuItem(ModernCommand command, ModernTheme theme)
        {
            _command = command;
            _theme = theme;
            UpdateImage(theme);
            Click += ExecuteCommand;
            command.PropertyChanged += CommandPropertyChanged;
            command.CanExecuteChanged += CanExecuteChanged;
            ApplyCommand();
        }

        private void ExecuteCommand(object? sender, EventArgs e) => _command.TryExecute();
        private void CommandPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            ApplyCommand();
            if (e.PropertyName is nameof(ModernCommand.Icon) or nameof(ModernCommand.Image)) UpdateImage(_theme);
        }
        private void CanExecuteChanged(object? sender, EventArgs e) => Enabled = _command.CanExecute;
        public void ApplyTheme(ModernTheme theme)
        {
            _theme = theme;
            UpdateImage(theme);
        }

        private void ApplyCommand()
        {
            Text = _command.Text;
            Enabled = _command.CanExecute;
            Visible = _command.Visible;
            Checked = _command.IsChecked;
            ShortcutKeys = _command.ShortcutKeys;
            ToolTipText = _command.Description;
        }

        private void UpdateImage(ModernTheme theme)
        {
            var previousOwned = Tag as Image;
            if (_command.Image is not null)
            {
                Image = _command.Image;
                Tag = null;
            }
            else if (_command.Icon != ModernIconKind.None)
            {
                var generated = ModernIcons.CreateBitmap(_command.Icon, theme.Text, 16);
                Image = generated;
                Tag = generated;
            }
            else
            {
                Image = null;
                Tag = null;
            }
            previousOwned?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _command.PropertyChanged -= CommandPropertyChanged;
                _command.CanExecuteChanged -= CanExecuteChanged;
                (Tag as Image)?.Dispose();
                Tag = null;
                Image = null;
            }
            base.Dispose(disposing);
        }
    }

    private void ApplyTheme()
    {
        BackColor = Theme.Control;
        ForeColor = Theme.Text;
        Renderer = new ToolStripProfessionalRenderer(new ModernMenuColorTable(Theme)) { RoundedEdges = true };
        ModernToolStripDropDownRegion.Attach(this, Theme.Radius);
        foreach (ToolStripItem item in Items)
        {
            item.BackColor = Theme.Control;
            item.ForeColor = Theme.Text;
            if (item is CommandMenuItem commandItem) commandItem.ApplyTheme(Theme);
        }
        Invalidate();
    }

    protected override void OnItemAdded(ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is null) return;
        e.Item.BackColor = Theme.Control;
        e.Item.ForeColor = Theme.Text;
    }

    private sealed class ModernMenuColorTable(ModernTheme theme) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => theme.Control;
        public override Color ImageMarginGradientBegin => theme.Control;
        public override Color ImageMarginGradientMiddle => theme.Control;
        public override Color ImageMarginGradientEnd => theme.Control;
        public override Color MenuItemSelected => theme.PrimaryBackground;
        public override Color MenuItemBorder => theme.Primary;
        public override Color MenuBorder => theme.Border;
        public override Color SeparatorDark => theme.BorderSecondary;
        public override Color SeparatorLight => theme.BorderSecondary;
        public override Color CheckBackground => theme.PrimaryBackground;
        public override Color CheckSelectedBackground => theme.PrimaryBackground;
    }
}
