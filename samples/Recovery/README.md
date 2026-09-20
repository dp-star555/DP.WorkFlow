# 人工异常处理演示（纯软件模拟）

共享代码：`WorkflowRecoveryDemo.cs`，同时由WinForms、WPF样例与Windows集成测试编译。

在仓库根目录启动：

```powershell
dotnet run --project DP.WorkFlow/samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj --no-restore -- --recovery-demo
dotnet run --project DP.WorkFlow/samples/Legacy/WpfApptest/WpfApptest.csproj --no-restore -- --recovery-demo
```

点击运行后，在真实提示窗口选择：

- 继续原操作：最终模拟位置110、命令1次、进料1次。
- 回初始点重做：再确认人工步骤，最终模拟位置10、命令2次、进料1次。
- 停止：终止本次演示。

主窗口停止按钮仍可使用。关闭提示窗口不代表同意。处理图可打开查看和编辑，下一次根运行前重新编译；模拟工位Guard仍约束入口和位置。

不加参数仍启动默认视觉文件流程。演示不操作任何物理设备，不要把模拟Guard用于生产设备。

完整接口与边界见[人工交互说明](../../docs/nodes/operator-interaction.md)。
