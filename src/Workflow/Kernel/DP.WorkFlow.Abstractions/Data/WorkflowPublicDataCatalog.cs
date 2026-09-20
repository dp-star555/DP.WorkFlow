namespace DP.WorkFlow;

/// <summary>设计期可绑定的公共数据声明；运行时值由独立 <see cref="IWorkflowPublicDataStore"/> 保存。</summary>
/// <param name="Key">运行时公共数据仓使用的稳定键。</param>
/// <param name="ValueType">设计期绑定分析使用的变量值类型。</param>
/// <param name="DisplayName">属性编辑器和绑定树中显示的名称。</param>
/// <param name="Category">绑定树中的分类名称。</param>
public sealed record WorkflowPublicDataDescriptor(
    string Key,
    Type ValueType,
    string DisplayName,
    string Category = "公共数据");

/// <summary>宿主为流程设计器提供的公共数据类型目录。</summary>
public sealed class WorkflowPublicDataCatalog
{
    private readonly Dictionary<string, WorkflowPublicDataDescriptor> _items = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>目录注册项新增或被同键注册项替换后发生。</summary>
    public event EventHandler? Changed;

    /// <summary>获取按分类和显示名称排序的线程安全快照。</summary>
    public IReadOnlyCollection<WorkflowPublicDataDescriptor> Items
    {
        get { lock (_sync) return _items.Values.OrderBy(item => item.Category, StringComparer.Ordinal).ThenBy(item => item.DisplayName, StringComparer.Ordinal).ToArray(); }
    }

    /// <summary>注册一个具有编译期已知值类型的公共数据声明。</summary>
    /// <typeparam name="T">公共数据值的设计期类型。</typeparam>
    /// <param name="key">公共数据稳定键；同键注册会替换现有声明。</param>
    /// <param name="displayName">设计器显示名称；为空时使用规范化后的键。</param>
    /// <param name="category">绑定树分类；为空时归入“公共数据”。</param>
    /// <returns>当前目录实例，便于链式注册。</returns>
    public WorkflowPublicDataCatalog Register<T>(string key, string? displayName = null, string category = "公共数据") =>
        Register(key, typeof(T), displayName, category);

    /// <summary>注册一个公共数据声明，并通知设计器刷新绑定候选。</summary>
    /// <param name="key">公共数据稳定键；同键注册会替换现有声明。</param>
    /// <param name="valueType">绑定分析和属性编辑器使用的值类型。</param>
    /// <param name="displayName">设计器显示名称；为空时使用规范化后的键。</param>
    /// <param name="category">绑定树分类；为空时归入“公共数据”。</param>
    /// <returns>当前目录实例，便于链式注册。</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> 为空或仅包含空白字符。</exception>
    /// <exception cref="ArgumentNullException"><paramref name="valueType"/> 为空。</exception>
    public WorkflowPublicDataCatalog Register(string key, Type valueType, string? displayName = null, string category = "公共数据")
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("公共数据键不能为空。", nameof(key));
        ArgumentNullException.ThrowIfNull(valueType);
        var normalized = key.Trim();
        var descriptor = new WorkflowPublicDataDescriptor(normalized, valueType,
            string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
            string.IsNullOrWhiteSpace(category) ? "公共数据" : category.Trim());
        lock (_sync) _items[normalized] = descriptor;
        Changed?.Invoke(this, EventArgs.Empty);
        return this;
    }

    /// <summary>尝试按稳定键获取公共数据声明。</summary>
    /// <param name="key">要查找的公共数据键。</param>
    /// <param name="descriptor">找到时返回声明，否则返回 <see langword="null"/>。</param>
    /// <returns>目录中存在该键时返回 <see langword="true"/>。</returns>
    public bool TryGet(string key, out WorkflowPublicDataDescriptor? descriptor)
    {
        lock (_sync) return _items.TryGetValue(key, out descriptor);
    }
}
