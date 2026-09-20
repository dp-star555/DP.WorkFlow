namespace DP.WorkFlow.UI;

/// <summary>为尚未显式标注 WorkflowPropertyAttribute 的节点参数提供统一中文约定。</summary>
internal static class WorkflowPropertyChineseMetadata
{
    private static readonly IReadOnlyDictionary<string, (string Name, string Description)> Entries =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["Id"] = ("节点标识", "节点在当前流程文档中的唯一标识。"),
            ["Title"] = ("标题", "节点在设计画布和运行监视中的显示标题。"),
            ["FunctionKey"] = ("函数键", "宿主函数注册表中用于定位执行函数的稳定键。"),
            ["ResultVarKey"] = ("结果变量键", "节点执行完成后写入结果的流程变量名称；留空表示不写入变量。"),
            ["TimeoutMs"] = ("超时时间", "等待或设备操作允许的最长时间，单位为毫秒；达到该时间后按节点超时规则处理。"),
            ["TimeoutMilliseconds"] = ("脚本超时时间", "脚本协作式取消的最长时间，单位为毫秒；进程内脚本必须主动响应取消令牌。"),
            ["OverrideTimeoutMs"] = ("覆盖超时时间", "覆盖设备步骤默认超时时间，单位为毫秒；零值表示使用设备配置。"),
            ["PollIntervalMs"] = ("轮询间隔", "检查设备状态或等待条件的时间间隔，单位为毫秒。"),
            ["DelayMs"] = ("延迟时间", "节点等待后继续执行的时间，单位为毫秒。"),
            ["WaitTimeoutMs"] = ("等待超时时间", "等待条件允许的最长时间，单位为毫秒。"),
            ["Timeout"] = ("超时时间", "等待或设备操作允许的最长持续时间。"),
            ["TimeoutAsFalseBranch"] = ("超时走失败分支", "启用后超时沿 False 或 Timeout 输出继续；禁用后超时按节点失败处理。"),
            ["WaitForCompleted"] = ("等待完成", "启用后等待设备动作真正完成再继续；禁用后命令受理成功即继续。"),
            ["ContinueOnError"] = ("错误后继续", "启用后执行错误沿 Error 输出继续；禁用后错误会终止当前流程。"),
            ["IgnoreCase"] = ("忽略大小写", "比较字符串时是否忽略英文字母大小写。"),
            ["TrimBeforeCompare"] = ("比较前去除空白", "比较字符串前是否移除首尾空白字符。"),
            ["EqualityTolerance"] = ("相等容差", "数值比较时判断两个值相等所允许的最大误差。"),
            ["Iterations"] = ("循环次数", "循环体计划执行的总次数。"),
            ["Script"] = ("脚本正文", "受信任 C# 脚本的源代码；脚本节点请在右侧专用编辑区域修改。"),
            ["Interrupt"] = ("异常处理", "设备故障发生时使用的报警编码和可恢复中断策略。"),
            ["DeviceId"] = ("设备标识", "宿主设备服务中用于定位目标设备的稳定标识。"),
            ["AxisId"] = ("轴标识", "目标运动轴在设备中的稳定标识。"),
            ["RobotKey"] = ("机器人键", "宿主机器人服务中用于定位目标机器人的稳定键。"),
            ["StationKey"] = ("工站键", "产品流或工站服务中用于定位目标工站的稳定键。"),
            ["SlotKey"] = ("槽位键", "工站或设备中目标槽位的稳定键。"),
            ["ProductKey"] = ("产品键", "产品流服务中用于定位产品的稳定键。"),
            ["ReaderKey"] = ("读码器键", "宿主读码器服务中用于定位目标读码器的稳定键。"),
            ["IoKey"] = ("IO 点位键", "宿主 IO 服务中目标输入或输出点位的稳定键。"),
            ["SignalKey"] = ("信号键", "流程信号服务中用于读写或等待信号的稳定键。"),
            ["QueueKey"] = ("队列键", "流程队列服务中用于定位目标队列的稳定键。"),
            ["StepKey"] = ("步骤键", "轴运动步骤配置中的稳定键。"),
            ["StepKeySource"] = ("步骤键来源", "指定步骤键使用固定值还是流程绑定值。"),
            ["BindingValue"] = ("绑定值", "从流程变量、节点输出或其他可见数据源读取的绑定值。"),
            ["LiteralValue"] = ("固定值", "未使用绑定时直接采用的固定参数值。"),
            ["ValueSource"] = ("值来源", "指定参数使用固定值还是流程绑定值。"),
            ["ConditionSource"] = ("条件来源", "指定判断条件来自宿主函数还是流程输入绑定。"),
            ["Condition"] = ("条件", "用于决定节点输出分支的布尔条件。"),
            ["Operator"] = ("比较运算符", "对左右输入执行比较时使用的运算规则。"),
            ["Left"] = ("左值", "比较操作左侧的输入值。"),
            ["Right"] = ("右值", "比较操作右侧的输入值。"),
            ["DataType"] = ("数据类型", "执行转换或比较时采用的数据类型。"),
            ["TargetType"] = ("目标类型", "值转换完成后期望得到的数据类型。"),
            ["ValueType"] = ("值类型", "信号、队列或流程数据项保存的数据类型。"),
            ["WriteMode"] = ("写入方式", "数据写入信号、队列或设备时采用的更新方式。"),
            ["WaitMode"] = ("等待方式", "节点判断等待条件时采用的匹配方式。"),
            ["RemoveMode"] = ("移除方式", "从队列中移除数据时采用的规则。"),
            ["ExpectedState"] = ("期望状态", "等待信号或设备达到的目标布尔状态。"),
            ["ExpectedValue"] = ("期望值", "等待信号或设备达到的目标值。"),
            ["State"] = ("状态", "需要写入或等待的目标状态。"),
            ["Enabled"] = ("启用", "指定目标功能是否启用。"),
            ["AutoResetAfterMatched"] = ("匹配后自动复位", "条件匹配后是否自动清除当前信号状态。"),
            ["AutoCreateIfMissing"] = ("不存在时自动创建", "目标信号或队列不存在时是否自动创建。"),
            ["PreventDuplicate"] = ("禁止重复", "启用后不向队列加入已经存在的相同值。"),
            ["CountThreshold"] = ("数量阈值", "队列数量达到该值时判定等待条件成立。"),
            ["Items"] = ("配置项", "需要批量处理的配置项集合。"),
            ["InputMappings"] = ("输入映射", "父流程数据传入子流程时使用的变量和绑定映射。"),
            ["OutputMappings"] = ("输出映射", "子流程数据返回父流程时使用的变量和绑定映射。"),
            ["EntryNodeId"] = ("入口节点", "子流程开始执行时使用的入口节点标识。"),
            ["AlarmCode"] = ("报警编码", "设备故障或流程异常对应的报警编码。"),
            ["Message"] = ("消息", "运行记录、提示或报警中显示的文本。"),
            ["Prompt"] = ("提示内容", "需要向操作员显示的提示信息。"),
            ["TargetNodeId"] = ("目标节点", "跳转或恢复操作需要继续执行的目标节点标识。"),
            ["TargetVariableName"] = ("目标变量名", "映射结果写入目标作用域时使用的变量名称。"),
            ["ParentVariableName"] = ("父流程变量名", "输入映射从父流程读取的变量名称。"),
            ["ChildVariableName"] = ("子流程变量名", "输出映射从子流程读取的变量名称。"),
            ["Source"] = ("来源", "指定当前参数或映射值的数据来源。"),
            ["Address"] = ("地址", "目标设备、寄存器或通信对象使用的地址。"),
            ["Channel"] = ("通道", "目标设备或采集对象使用的通道编号。"),
            ["Position"] = ("目标位置", "运动轴或机器人需要到达的目标位置。"),
            ["Speed"] = ("速度", "设备动作或运动命令使用的目标速度。"),
            ["Acceleration"] = ("加速度", "运动命令使用的加速度。"),
            ["Deceleration"] = ("减速度", "运动命令使用的减速度。"),
            ["Tolerance"] = ("允许误差", "判断位置或测量结果达到目标值时允许的误差范围。"),
            ["PositionTolerance"] = ("位置容差", "判断运动轴到达目标位置时允许的误差。"),
            ["RetryCount"] = ("重试次数", "操作失败后允许自动重试的最大次数。"),
            ["IsOn"] = ("开启状态", "指定执行开启还是关闭操作。"),
            ["OutputKey"] = ("输出键", "节点输出结果中使用的稳定键。")
        };

    /// <summary>解析属性的中文显示名、分类和用途说明。</summary>
    /// <param name="propertyName">CLR 属性名称。</param>
    /// <returns>返回适合属性面板显示的名称、分类和说明。</returns>
    public static (string DisplayName, string Category, string Description) Resolve(string propertyName)
    {
        var item = Entries.TryGetValue(propertyName, out var known)
            ? known
            : (Name: propertyName, Description: $"用于配置节点的“{propertyName}”参数。请依据设备和流程设计要求填写。");
        return (item.Name, ResolveCategory(propertyName), item.Description);
    }

    /// <summary>根据属性名称推断中文分类。</summary>
    /// <param name="name">名称。</param>
    /// <returns>返回操作结果；具体含义参见方法说明。</returns>
    private static string ResolveCategory(string name)
    {
        if (name is "Id" or "Title") return "基本信息";
        if (name.Contains("Interrupt", StringComparison.Ordinal) || name.Contains("Alarm", StringComparison.Ordinal)) return "异常处理";
        if (name.Contains("Timeout", StringComparison.Ordinal) || name.Contains("Wait", StringComparison.Ordinal) || name.Contains("Poll", StringComparison.Ordinal)) return "等待与超时";
        if (name.Contains("Binding", StringComparison.Ordinal) || name.Contains("Source", StringComparison.Ordinal) || name.Contains("Literal", StringComparison.Ordinal)) return "数据来源";
        if (name.Contains("Result", StringComparison.Ordinal) || name.Contains("Output", StringComparison.Ordinal)) return "输出结果";
        if (name.Contains("Robot", StringComparison.Ordinal)) return "机器人";
        if (name.Contains("Axis", StringComparison.Ordinal) || name.Contains("Position", StringComparison.Ordinal) || name.Contains("Speed", StringComparison.Ordinal)) return "运动参数";
        if (name.Contains("Io", StringComparison.OrdinalIgnoreCase) || name.Contains("Signal", StringComparison.Ordinal)) return "信号与 IO";
        return "常规参数";
    }
}
