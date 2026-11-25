# 人工异常处理演示（纯软件模拟）

共享代码：`WorkflowRecoveryDemo.cs`，同时由WinForms、WPF样例与Windows集成测试编译。

在仓库根目录启动：

```powershell
dotnet run --project DP.WorkFlow/samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj --no-restore -- --recovery-demo
dotnet run --project DP.WorkFlow/samples/Legacy/WpfApptest/WpfApptest.csproj --no-restore -- --recovery-demo
```

点击运行后，在真实提示窗口选择：

- 就地恢复：最终模拟位置110、命令1次、进料1次。
- 回初始点重做：再确认人工步骤，最终模拟位置10、命令2次、进料1次。
- 停止：终止本次演示。

主窗口停止按钮仍可使用。关闭提示窗口不代表同意。模拟轴故障后流程停在停点上，由统管层的故障会话运行文档里的处理块；处理块可打开查看和编辑，每次故障都按当前文档查找。

"就地恢复"调用模拟轴的就地恢复（位置已在目标上就按完成往下走）；"回初始点重做"用"回退再继续"回到标好的恢复入口 OperationStart（入口之后没有完成的动作，所以不用回退任何东西）。

不加参数仍启动默认视觉文件流程。演示不操作任何物理设备。

完整接口与边界见[人工交互说明](../../docs/nodes/operator-interaction.md)。
