namespace DP.WorkFlow;

/// <summary>视觉节点在工具箱中的分类；序号按“取图→处理→定位→测量→判定”的作业顺序排列。</summary>
public static class WorkflowVisionCategories
{
    /// <summary>图像获取。</summary>
    public const string Acquisition = "5.Vision/1.取图";
    /// <summary>整图预处理。</summary>
    public const string Processing = "5.Vision/2.图像处理";
    /// <summary>区域与掩膜。</summary>
    public const string Region = "5.Vision/3.区域";
    /// <summary>模板定位与本帧坐标系。</summary>
    public const string Location = "5.Vision/4.定位";
    /// <summary>卡尺、拟合与距离测量。</summary>
    public const string Measurement = "5.Vision/5.测量";
    /// <summary>点、线生成与坐标转换。</summary>
    public const string Geometry = "5.Vision/6.几何";
    /// <summary>连通域与颜色检测。</summary>
    public const string Inspection = "5.Vision/7.检测";
    /// <summary>读码与文字识别。</summary>
    public const string Recognition = "5.Vision/8.识别";
    /// <summary>手眼仿射标定。</summary>
    public const string Calibration = "5.Vision/9.标定";
}
