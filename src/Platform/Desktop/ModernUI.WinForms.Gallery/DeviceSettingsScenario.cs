using System.ComponentModel;

namespace ModernUI.WinForms.Gallery;

/// <summary>业务场景使用的可绑定设备参数；不属于控件库公开接口。</summary>
internal sealed class DeviceSettingsModel : INotifyPropertyChanged
{
    private string _name = "Line Camera 01";
    private string _address = "192.168.1.20";
    private decimal _port = 3956;
    private decimal _timeout = 3000;
    private bool _enabled = true;
    private string _modeId = "continuous";
    private string _notes = "Main inspection camera";

    public string Name { get => _name; set => Set(ref _name, value ?? string.Empty, nameof(Name)); }
    public string Address { get => _address; set => Set(ref _address, value ?? string.Empty, nameof(Address)); }
    public decimal Port { get => _port; set => Set(ref _port, value, nameof(Port)); }
    public decimal Timeout { get => _timeout; set => Set(ref _timeout, value, nameof(Timeout)); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value, nameof(Enabled)); }
    public string ModeId { get => _modeId; set => Set(ref _modeId, value ?? string.Empty, nameof(ModeId)); }
    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty, nameof(Notes)); }

    public DeviceSettingsSnapshot Capture() => new(Name, Address, Port, Timeout, Enabled, ModeId, Notes);

    public void Restore(DeviceSettingsSnapshot snapshot)
    {
        Name = snapshot.Name;
        Address = snapshot.Address;
        Port = snapshot.Port;
        Timeout = snapshot.Timeout;
        Enabled = snapshot.Enabled;
        ModeId = snapshot.ModeId;
        Notes = snapshot.Notes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed record DeviceSettingsSnapshot(
    string Name,
    string Address,
    decimal Port,
    decimal Timeout,
    bool Enabled,
    string ModeId,
    string Notes);

/// <summary>确保业务场景异步操作先于绑定和验证资源被取消。</summary>
internal sealed class DeviceScenarioLifetime(Action disposing) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Action? _disposing = disposing ?? throw new ArgumentNullException(nameof(disposing));
    public CancellationToken Token => _cancellation.Token;

    public void Dispose()
    {
        var callback = Interlocked.Exchange(ref _disposing, null);
        if (callback is null) return;
        _cancellation.Cancel();
        callback();
        _cancellation.Dispose();
    }
}
