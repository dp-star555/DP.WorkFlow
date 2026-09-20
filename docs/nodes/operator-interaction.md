# 真实人工交互与恢复演示

本轮在[恢复V1](fault-recovery-v1.md)基础上补齐真实人工选择/确认窗口。后续已接入[联合恢复与处置步骤继续](joint-recovery.md)，现有窗口可直接呈现“原故障＋当前受阻步骤”，不需要嵌套处理图。只展示工程师预设的选项，不开放任意节点跳转，也不替代工位恢复Guard。

## 1. 分层

- `WorkflowOperatorInteraction`：位于 `DP.WorkFlow.Nodes.Process`，UI无关的任务控制器，实现 `IWorkflowOperatorService`。负责排队、取消、任务身份和响应去重。
- `DP.WorkFlow.Process.UI.WinForms`：提供 `DP.WorkFlow.OperatorUI.WinForms.WorkflowWinFormsOperatorService`。
- `DP.WorkFlow.Process.UI.Wpf`：提供 `DP.WorkFlow.OperatorUI.Wpf.WorkflowWpfOperatorService`。
- 两个桌面工程只依赖Process运行契约及各自桌面框架；Kernel和节点不引用桌面工程。

同一交互服务实例一次只展示一个任务。多个请求可以排队；排队请求的取消不会误取消当前提示。

## 2. 装配

WinForms，在主窗体UI线程创建，不要求此时已有句柄：

```csharp
using DP.WorkFlow.OperatorUI.WinForms;

_operatorService = new WorkflowWinFormsOperatorService(this);
services.Add<IWorkflowOperatorService>(_operatorService);
```

WPF，在主窗口Dispatcher线程创建：

```csharp
using DP.WorkFlow.OperatorUI.Wpf;

_operatorService = new WorkflowWpfOperatorService(this);
services.Add<IWorkflowOperatorService>(_operatorService);
```

关闭宿主时先 `await runtimeHost.StopAsync()`，再释放交互服务、运行绑定及其他资源。不要先销毁设备或交互服务再假定工作流已经退出。

两平台提示都是非模态所属窗口，不禁用主界面的停止操作。不设置默认确认按钮，初始焦点放在只读操作指引；可以通过Tab导航后主动操作按钮。

## 3. 响应语义

- **选择**：只能提交预设键；大小写不敏感匹配后返回配置键。
- **确认**：只确认当前人工步骤，不表示设备已满足恢复条件。
- **关闭提示窗口**：抛出 `WorkflowOperatorPromptDismissedException`，不会默认选择第一项或当作确认。
- **运行取消/服务Dispose**：取消当前及排队任务；旧窗口响应不能推进新任务。
- **隐藏并重新显示主窗口**：重建提示窗口，保留尚未回答的同一个任务。
- **WinForms句柄重建**：重新呈现未回答任务，不强制提前创建主窗体句柄。

在V1的异常处理子流程中，关闭提示而不回答属于处置未完成，当前实现保守终止；它不是另一个隐式的“继续”选项。正常停止分支应由工程师在 `OperatorChoice.OptionsText` 和控制连线中明确配置。

控件按钮即时禁用防止重复点击；真正的去重由共享控制器按任务ID完成，不依赖按钮是否已刷新。独立调用交互接口时，响应接受也不代表后续工作流恢复已批准。

## 4. 无界面接入与边界

`WorkflowOperatorInteraction.CurrentPrompt` 提供只读任务快照；呈现器监听 `PromptChanged` 后重新读取当前状态，不依赖事件顺序。通过 `TryChoose(id, key)`、`TryConfirm(id)`、`TryDismiss(id)` 提交响应。

这是受信任进程内接口，不是远程鉴权协议。未实现操作员登录、权限审批、跨进程会话或完整故障工作台。没有界面呈现器时不会自动选择答案，调用方必须主动回应或取消。

选项允许2至32项作为防误配置预算，但现场建议仅配置少量必要方案。选项键和名称不能为空，键不能重复。

## 5. 可以直接启动的双平台演示

在仓库根目录运行：

```powershell
dotnet run --project DP.WorkFlow/samples/DP.WorkFlow.WinForms.Sample/WinFormsApp_test.csproj --no-restore -- --recovery-demo

dotnet run --project DP.WorkFlow/samples/Legacy/WpfApptest/WpfApptest.csproj --no-restore -- --recovery-demo
```

不加参数仍是原视觉文件演示。原自动确认 `DemoOperatorService` 已移除，两个宿主使用真实交互适配器。

打开后点击“运行”。根图为：

```text
Start → 模拟进料 → 本次操作入口 → 模拟轴运动 → End
```

独立的“工程师预设的模拟处置”块包含三个预设分支：

1. **继续原操作**：模拟位置已从100到110，仅完成响应丢失；检查完成后继续，不重发位移。
2. **回初始点重做**：人员确认后执行“模拟位置置0”，再请求从 `OperationStart` 重执行。
3. **停止**：结束本次演示。

预期运行结果：

| 分支 | 最终模拟位置 | 模拟运动命令次数 | 模拟进料次数 |
|---|---:|---:|---:|
| 继续原操作 | 110 | 1 | 1 |
| 回初始点重做 | 10 | 2 | 1 |

可在节点输出及“模拟恢复结果”Trace查看计数。仅演示模式注册 `Sample.RecoveryFeed/Move` 节点；读取含这些节点的演示文档也应使用该模式。

示例只支持从根流程启动。下一次运行前会从当前文档重新编译处理块，因此编辑确认后的处理图会生效；运行中的处理计划不会随文档修改。模拟Guard只认可 `OperationStart`、位置0和进料一次；修改入口或工艺必须同步编写相应Guard，不能去掉验证凑通流程。

**所有位置、进料与运动命令均是软件模拟，不连接任何轴、机器人或真实IO。** 按钮操作是真实桌面交互，不代表机械运动与安全互锁已验收。

## 6. 验证

- `OperatorInteractionTests`：无默认答案、只读选项、有限键、过期与重复响应、当前/排队取消、Dispose、观察器故障。
- `OperatorWindowTests`：双平台真实控件按钮驱动演示、模拟命令次数、关闭不确认、Host.StopAsync取消窗口、隐藏重显、句柄重建以及编辑后重绑定处理图。
- 原生窗口截图已检查，WinForms换行和初始选择高亮问题已修正。

最终全量708/708（Windows353），Solution Release及独立WPF Debug/Release零警告错误，工程核对45/45。日志：`/tmp/operator-workflow-final.log`、`/tmp/operator-release.log`、`/tmp/operator-wpf-debug.log`、`/tmp/operator-wpf-release.log`、`/tmp/operator-projects.log`（Git Bash /tmp）。

测试使用按钮事件和消息循环，不等于物理键鼠、操作员权限或设备安全认证。外部故障Case接口、受控设备中断及复杂并行回退仍未开放。
