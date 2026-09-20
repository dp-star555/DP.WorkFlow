using System.ComponentModel;
using ModernPropertyGrid.WinForms;
using ModernUI.WinForms;

namespace ModernUI.WinForms.Gallery;

internal sealed partial class DemoCategoryForm
{
    private void BuildFeedback()
    {
        var alertCommand = new ModernCommand(() => Report("Alert ActionCommand 已执行")) { Text = "查看详情" };
        var alert = new ModernAlert
        {
            Width = 720, Height = 82, Text = "配置已保存", Description = "新参数将在下次运行时生效；单击正文执行 ActionCommand。",
            Status = ModernVisualStatus.Success, Closable = true, WrapText = true, ActionCommand = alertCommand
        };
        alert.Closed += (_, _) => Report("Alert 已由用户关闭");
        AddSection("页面内 Alert", "语义 Surface、Title/Description、ActionCommand、关闭、换行和 LiveRegion。", alert);

        var messages = Row(
            DemoButton("信息", () => ModernMessage.Info(this, "这是一条全局消息")),
            DemoButton("成功", () => ModernMessage.Success(this, "操作执行成功")),
            DemoButton("警告", () => ModernMessage.Warning(this, "请检查设备状态")),
            DemoButton("错误", () => ModernMessage.Error(this, "设备连接失败")),
            DemoButton("重复合并", () =>
            {
                ModernMessage.Info(this, "重复任务已加入队列");
                ModernMessage.Info(this, "重复任务已加入队列");
                Report("发送两条相同 Message，观察重复合并");
            }));
        AddSection("Message 队列", "Info/Success/Warning/Error、非阻塞显示、自动关闭、重复合并和最大可见队列。", messages);

        var actionCommand = new ModernCommand(() => Report("Notification“查看诊断”操作已执行"))
        {
            Text = "查看诊断", Icon = ModernIconKind.Search
        };
        var overlays = Row(
            DemoButton("普通通知", () => ModernNotification.Show(this, "诊断完成", "发现 2 条可优化建议。", ModernVisualStatus.Primary)),
            DemoButton("带操作通知", () => ModernNotification.Show(this, new ModernNotificationOptions
            {
                Title = "设备离线", Description = "Line Camera 01 连接已中断。", Status = ModernVisualStatus.Warning,
                Duration = 10000, ActionCommand = actionCommand, CloseOnAction = true
            })),
            DemoButton("确认对话框", () =>
            {
                var result = ModernDialog.Show(this, new ModernDialogOptions
                {
                    Title = "确认应用", Message = "确定要应用当前配置吗？", Status = ModernVisualStatus.Warning,
                    ConfirmText = "应用", CancelText = "返回"
                });
                Report($"DialogResult = {result}");
            }));
        AddSection("Notification 与 Dialog", "通知 Action/关闭/Owner 跟随；对话框默认按钮、Esc、语义状态、结果和焦点恢复。", overlays);

        var tooltip = Own(new ModernToolTip { InitialDelay = 150, Placement = ModernToolTipPlacement.Top });
        var tooltipTarget = new ModernButton { Text = "悬停查看 ToolTip", Width = 152 };
        tooltip.SetToolTipText(tooltipTarget, "这是跟随锚点、支持自动翻转和 DPI 的现代 ToolTip。\n移动或滚动页面时会重新定位。");
        var errorTooltip = DemoButton("错误气泡", () => tooltip.Show(tooltipTarget, "设备地址格式不正确。", ModernToolTipKind.Error, 3000));
        AddSection("ToolTip 与错误气泡", "Extender 悬停文本、Top/Bottom 自动翻转、即时 Show、错误语义和 Anchor 跟随。", Row(tooltipTarget, errorTooltip));

        var provider = Own(new ModernValidationProvider());
        var address = new ModernInput { Width = 320, Text = "192.168", PlaceholderText = "IP 地址" };
        var port = new ModernInputNumber { Width = 220, Minimum = 1, Maximum = 65535, Value = 0, DecimalPlaces = 0 };
        var summary = new ModernValidationSummary { Width = 720, Height = 76, Provider = provider };
        provider.SetValidation(address, ModernValidationState.Error, "请输入完整 IPv4 地址，例如 192.168.1.10");
        provider.SetValidation(port, ModernValidationState.Warning, "建议使用 1024 以上的端口");
        var validate = DemoButton("执行异步验证", async () =>
        {
            provider.SetAsyncValidator(address, async token =>
            {
                await Task.Delay(450, token);
                return address.Text.Count(character => character == '.') == 3
                    ? new ModernValidationOutcome(ModernValidationState.Success, "地址可用")
                    : new ModernValidationOutcome(ModernValidationState.Error, "IPv4 地址格式不完整");
            });
            var valid = await provider.ValidateAsync();
            Report($"异步验证完成，Valid = {valid}");
        });
        var clear = DemoButton("清除验证", () => { provider.Clear(); Report("验证结果已清除"); });
        AddSection("统一验证与摘要", "Error/Warning/Pending/Success、异步取消、Tooltip、摘要 LiveRegion；单击摘要定位首个错误。",
            Column(FormRows(("设备地址", address), ("端口", port)), summary, Row(validate, clear)));
    }
}
