namespace DP.WorkFlow;

/// <summary>节点声明的范围执行能力，不由共享层按具体节点类型猜测。</summary>
public enum EWorkflowVisionRange
{
    /// <summary>整图或已有事实，不接受新范围与坐标绑定。</summary>
    None,
    /// <summary>连续面积ROI及精确掩码，可绑定定位。</summary>
    Region,
    /// <summary>定向采样带，可绑定定位，不接受面积ROI。</summary>
    SamplingBand,
    /// <summary>原图几何事实，可绑定结果坐标表达，不重复变换输入点。</summary>
    GeometryFacts,
    /// <summary>仅原图轴对齐矩形；不接受面积掩码或定位变换。</summary>
    Rectangle
}
