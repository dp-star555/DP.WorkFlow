# ScriptEngine.WinForms.Demo

用于直观看到当前 `RoslynScriptEditorControl` 的最小 WinForms 宿主。

演示内容：

- C# 语法和语义高亮、XML 文档标签分色、行号和换行智能缩进
- 缩进引导线和当前行背景提示
- 完整 Roslyn CompletionService 补全：输入满 2 个标识符字符后自动弹出，输入 `.` 立即显示成员，支持模糊筛选、分类列、文档侧栏、未导入类型自动 using 和 `Ctrl+Space`
- 类、方法、`if`、`switch`、循环、异常处理块和 `#region` 的收缩/展开及行内 `…` 提示
- 实时 Roslyn 错误/警告波浪线、右侧文档概览标记和点击跳转
- 编译诊断和问题定位
- 实现 `ICSharpProgram` 的脚本编译与运行
- 宿主上下文、脚本标准输出和返回值
- 外部 DLL 引用管理

## 运行

在仓库根目录执行：

```powershell
dotnet run --project samples/ScriptEngine.WinForms.Demo/ScriptEngine.WinForms.Demo.csproj
```

程序启动后可以直接点击“编译检查”或“运行脚本”。删除示例代码中的一个分号，可以观察诊断列表和编辑器错误标记。

单文件发布需要保留 Scintilla 托管包装器及 `x64/x86` 原生卫星，并启用全部内容自解压。仓库验证脚本会执行真实 UI/编译冒烟：

```powershell
./tools/run-script-engine-editor-matrix.ps1 -IncludeSingleFilePublish
```
