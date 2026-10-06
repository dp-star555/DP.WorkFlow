using DP.Vision;
using DP.Vision.Algorithms;

namespace DP.WorkFlow;

/// <summary>为 DP.Vision 输出类型登记中文成员名称；这些类型位于外部程序集，无法直接标注显示特性。</summary>
public static class WorkflowVisionOutputNames
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["FrameId"] = "图像标识",
        ["TemplateFrameId"] = "模板标识",
        ["Image"] = "图像",
        ["Status"] = "完成状态",
        ["Summary"] = "摘要",
        ["DisplayGeometry"] = "显示图形",
        ["CoordinateSystem"] = "坐标系",
        ["Found"] = "是否找到",
        ["Score"] = "匹配分数",
        ["CenterX"] = "中心X",
        ["CenterY"] = "中心Y",
        ["AngleDegrees"] = "角度(°)",
        ["Scale"] = "缩放",
        ["ReferenceX"] = "参考点X",
        ["ReferenceY"] = "参考点Y",
        ["ReferenceAngleDegrees"] = "参考方向(°)",
        ["Transform"] = "姿态变换",
        ["Reference"] = "模板参考",
        ["CenterPoint"] = "匹配中心",
        ["ReferencePoint"] = "参考点",
        ["ReferenceToImage"] = "参考坐标映射",
        ["MatchGeometry"] = "匹配轮廓",
        ["Bounds"] = "外接矩形",
        ["Count"] = "数量",
        ["Blobs"] = "Blob 列表",
        ["MeasuredCentroids"] = "质心",
        ["LocatedCentroids"] = "质心(双坐标)",
        ["Width"] = "宽度",
        ["Height"] = "高度",
        ["Region"] = "区域",
        ["Area"] = "面积",
        ["Model"] = "测量模型",
        ["A"] = "端点 A",
        ["B"] = "端点 B",
        ["MeasuredA"] = "端点 A(带来源)",
        ["MeasuredB"] = "端点 B(带来源)",
        ["MeasuredLine"] = "拟合直线",
        ["LocatedA"] = "端点 A(双坐标)",
        ["LocatedB"] = "端点 B(双坐标)",
        ["Radius"] = "半径",
        ["LocalRadius"] = "局部半径",
        ["RmsError"] = "均方根误差",
        ["LocalRmsError"] = "局部均方根误差",
        ["PointCount"] = "边缘点数",
        ["InlierIndices"] = "内点索引",
        ["InlierCount"] = "内点数",
        ["Start"] = "采样起点",
        ["End"] = "采样终点",
        ["LocatedStart"] = "采样起点(双坐标)",
        ["LocatedEnd"] = "采样终点(双坐标)",
        ["SampleStep"] = "采样步长",
        ["Profile"] = "灰度剖面",
        ["Edges"] = "边缘",
        ["Shape"] = "卡尺形状",
        ["Center"] = "圆心",
        ["StartAngleDegrees"] = "起始角(°)",
        ["SweepDegrees"] = "扫描角度(°)",
        ["Path"] = "扫描路径",
        ["Line"] = "直线卡尺原始结果",
        ["Gradient"] = "梯度",
        ["Position"] = "位置",
        ["MeasuredEdges"] = "边缘点",
        ["LocatedEdges"] = "边缘点(双坐标)",
        ["PixelCount"] = "像素数",
        ["Red"] = "红色均值",
        ["Green"] = "绿色均值",
        ["Blue"] = "蓝色均值",
        ["M11"] = "M11(X←X)",
        ["M12"] = "M12(X←Y)",
        ["Tx"] = "X 平移",
        ["M21"] = "M21(Y←X)",
        ["M22"] = "M22(Y←Y)",
        ["Ty"] = "Y 平移",
        ["RotationCenter"] = "旋转中心",
        ["X"] = "X",
        ["Y"] = "Y",
        ["ImagePosition"] = "原图坐标",
        ["LocalPosition"] = "局部坐标",
        ["ImageLength"] = "原图长度",
        ["Space"] = "坐标空间",
        ["Mode"] = "距离模式",
        ["Kind"] = "测量对象",
        ["Distance"] = "距离",
        ["Unit"] = "单位",
        ["UnitName"] = "单位名称",
        ["CalibrationRms"] = "标定误差",
        ["Id"] = "标识",
        ["Name"] = "名称",
        ["Version"] = "版本",
        ["Signature"] = "语义签名"
    };

    private static readonly Lazy<bool> Registered = new(() =>
    {
        foreach (var type in new[]
        {
            typeof(ImageFrame), typeof(BlobAnalysisResult), typeof(ColorAnalysisResult), typeof(EdgeMeasurementResult),
            typeof(TemplatePoseResult), typeof(AffineCalibration), typeof(Coordinate2D),
            typeof(RegionAnalysisResult), typeof(CaliperResult), typeof(VisionCaliperMeasurement), typeof(VisionCaliperEdge), typeof(RobustLineResult), typeof(VisionPoint), typeof(VisionLine),
            typeof(GeometricDistanceResult), typeof(VisionCoordinateSystemResult), typeof(VisionCoordinateDefinition)
        })
            WorkflowOutputDisplayNames.Register(type, Names);
        return true;
    });

    /// <summary>登记名称表；重复调用无副作用。</summary>
    public static void EnsureRegistered() => _ = Registered.Value;
}
