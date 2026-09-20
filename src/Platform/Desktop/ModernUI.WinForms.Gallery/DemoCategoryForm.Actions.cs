using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildActions()
    {
        var loading = new ModernButton { Text = "模拟提交", ButtonType = ModernButtonType.Primary, Icon = ModernIconKind.Play, Width = 120 };
        loading.Click += (_, _) =>
        {
            loading.Loading = true;
            Report("开始异步提交，重复点击已被阻止");
            var timer = new System.Windows.Forms.Timer { Interval = 900 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                loading.Loading = false;
                Report("异步提交完成");
                ModernMessage.Success(this, "配置提交成功");
            };
            timer.Start();
        };
        var buttons = Row(
            new ModernButton { Text = "默认按钮" },
            new ModernButton { Text = "主要操作", ButtonType = ModernButtonType.Primary, Icon = ModernIconKind.Play, Width = 112 },
            new ModernButton { Text = "文本操作", ButtonType = ModernButtonType.Text, StrongHoverFeedback = true, Icon = ModernIconKind.Settings, Width = 112 },
            new ModernButton { Text = "危险操作", ButtonType = ModernButtonType.Danger, Width = 104 },
            loading,
            new ModernButton { Text = "不可用", Enabled = false });
        AddSection("按钮变体与加载", "Default、Primary、Text、Danger、图标、Loading、Disabled；点击“模拟提交”观察防重复执行。", buttons);

        var commandEnabled = true;
        var runCommand = new ModernCommand(() => Report("共享“运行”命令已执行"))
        {
            Text = "运行", Description = "运行当前工作流", Icon = ModernIconKind.Play, ShortcutKeys = Keys.Control | Keys.R
        };
        var autoSaveCommand = new ModernCommand(() => Report("自动保存命令切换"))
        {
            Text = "自动保存", Description = "可勾选命令", Icon = ModernIconKind.Settings, CheckOnExecute = true
        };
        var customCommandImage = Own(new Bitmap(16, 16));
        using (var graphics = Graphics.FromImage(customCommandImage))
        {
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.DeepSkyBlue);
            graphics.FillEllipse(brush, 1, 1, 14, 14);
        }
        var diagnosticsCommand = new ModernCommand(() => Report("诊断命令已执行"))
        {
            Text = "诊断", Description = "打开运行诊断", Image = customCommandImage
        };
        var commandBar = new ModernCommandBar { Width = 720 };
        commandBar.Commands.Add(runCommand);
        commandBar.Commands.Add(autoSaveCommand);
        commandBar.Commands.Add(new ModernCommand { Kind = ModernCommandKind.Separator });
        commandBar.Commands.Add(diagnosticsCommand);
        commandBar.Commands.Add(new ModernCommand(() => Report("日志命令已执行")) { Text = "运行日志", Icon = ModernIconKind.Search });

        var commandButton = new ModernButton { Command = runCommand, ButtonType = ModernButtonType.Primary, Width = 108 };
        var toggleCommand = DemoButton("切换可执行", () =>
        {
            commandEnabled = !commandEnabled;
            runCommand.Enabled = commandEnabled;
            Report($"运行命令 CanExecute = {commandEnabled}");
        });
        var contextTarget = new ModernButton { Text = "右键打开菜单", Width = 132 };
        var contextMenu = Own(new ModernContextMenu());
        contextMenu.SetCommands([runCommand, autoSaveCommand, new ModernCommand { Kind = ModernCommandKind.Separator }, diagnosticsCommand]);
        contextTarget.ContextMenuStrip = contextMenu;
        var manager = Own(new ModernCommandManager { Owner = this });
        manager.Commands.Add(runCommand);
        AddSection("业务命令栏与快捷键", "CommandBar 面向 ModernCommand：自动同步可执行状态、快捷键与 ContextMenu，并按可用宽度将业务操作收进 Overflow；它不是原生 ToolStrip 的替代品。",
            Column(commandBar, Row(commandButton, toggleCommand, contextTarget)));

        var link = new ModernLinkLabel { Text = "打开控件文档", AutoSize = true };
        link.LinkClicked += (_, _) => Report("ModernLinkLabel.LinkClicked");
        var menu = new ModernMenuStrip { Dock = DockStyle.None, Width = 700 };
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.Add("保存", null, (_, _) => Report("MenuStrip 保存"));
        fileMenu.DropDownItems.Add("退出", null, (_, _) => Report("MenuStrip 退出"));
        menu.Items.Add(fileMenu);
        menu.Items.Add(new ToolStripMenuItem("帮助(&H)", null, (_, _) => Report("MenuStrip 帮助")));
        var toolStrip = new ModernToolStrip { Dock = DockStyle.None, Width = 700 };
        toolStrip.Items.Add(new ToolStripButton("运行", null, (_, _) => runCommand.TryExecute()));
        toolStrip.Items.Add(new ToolStripButton("诊断", null, (_, _) => diagnosticsCommand.TryExecute()));
        toolStrip.Items.Add(new ToolStripSeparator());
        toolStrip.Items.Add(new ToolStripLabel("保留原生 ToolStripItem/Overflow/键盘语义"));
        AddSection("原生菜单与工具栏兼容层", "ModernToolStrip 用于必须保留 ToolStripItem、Designer、原生 Overflow、助记键与 UIA 的现有界面；新业务命令优先使用上方 CommandBar。",
            Column(Row(link), menu, toolStrip));
    }
}
