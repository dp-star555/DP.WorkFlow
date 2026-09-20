using System.ComponentModel;
using ModernPropertyGrid.WinForms;

namespace ModernUI.WinForms.Gallery;

internal enum AcquisitionMode { Continuous, Triggered, SingleFrame }

internal sealed class DemoSettings : INotifyPropertyChanged
{
    private string _deviceName = "Line Camera 01";
    private bool _enabled = true;
    private AcquisitionMode _mode = AcquisitionMode.Continuous;
    private decimal _exposure = 1500;
    private double _gain = 4.5;
    private int _timeout = 3000;
    private string[] _outputContents = ["原始图像", "检测结果"];

    [Category("常规"), DisplayName("设备名称"), Description("显示在设备列表和运行日志中的名称。")]
    [PropertyOrder(10)]
    public string DeviceName { get => _deviceName; set => Set(ref _deviceName, value, nameof(DeviceName)); }

    [Category("常规"), DisplayName("启用设备"), Description("关闭后设备不会参与工作流运行。")]
    [PropertyOrder(20)]
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value, nameof(Enabled)); }

    [Category("采集"), DisplayName("采集模式"), Description("连续采集、外部触发或单帧采集。")]
    [PropertyOrder(10)]
    public AcquisitionMode Mode { get => _mode; set => Set(ref _mode, value, nameof(Mode)); }

    [Category("采集"), DisplayName("曝光时间"), Description("相机传感器每帧的曝光时间。")]
    [PropertyOrder(20), PropertyRange(10, 100000, 10, 0), PropertyUnit("μs")]
    public decimal Exposure { get => _exposure; set => Set(ref _exposure, value, nameof(Exposure)); }

    [Category("采集"), DisplayName("模拟增益"), Description("提高图像亮度，同时可能增加噪声。")]
    [PropertyOrder(30), PropertyRange(0, 24, .1, 1), PropertyUnit("dB")]
    public double Gain { get => _gain; set => Set(ref _gain, value, nameof(Gain)); }

    [Category("输出"), DisplayName("输出内容"), Description("选择工作流运行后需要保留的输出内容，可同时选择多项。")]
    [PropertyMultiSelect("原始图像", "检测结果", "特征数据", "运行日志")]
    public string[] OutputContents { get => _outputContents; set => Set(ref _outputContents, value, nameof(OutputContents)); }

    [Category("通信"), DisplayName("超时时间"), Description("等待设备响应的最长时间。")]
    [PropertyRange(100, 30000, 100, 0), PropertyUnit("ms")]
    public int Timeout { get => _timeout; set => Set(ref _timeout, value, nameof(Timeout)); }

    [Category("系统"), DisplayName("设备标识"), Description("系统生成的只读设备标识。")]
    public string DeviceId { get; } = "CAM-2026-A001";

    [Category("操作"), DisplayName("示例窗体"), Description("点击按钮打开一个新的示例窗体。")]
    [PropertyOrder(10)]
    public DemoPropertyAction OpenHelloWindow { get; } = new("打开窗体", owner =>
    {
        using var dialog = new HelloDemoForm();
        dialog.ShowDialog(owner);
    });

    [Browsable(false)]
    public string InternalState => "Hidden";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string name)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
