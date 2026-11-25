namespace DP.WorkFlow.UI;

/// <summary>公共数据清单中的一行：登记项，加上哪些节点在用、核对结果。</summary>
/// <param name="Declaration">登记项；未登记的使用行为推断出的登记。</param>
/// <param name="Declared">是否已登记到清单。</param>
/// <param name="Uses">使用它的节点（含子画布）。</param>
/// <param name="Diagnostics">与这个键有关的核对结果。</param>
public sealed record WorkflowPublicDataRow(
    WorkflowPublicDataDeclaration Declaration,
    bool Declared,
    IReadOnlyList<WorkflowPublicDataUse> Uses,
    IReadOnlyList<WorkflowValidationError> Diagnostics)
{
    /// <summary>键。</summary>
    public string Key => Declaration.Key;

    /// <summary>已登记但没有节点使用。</summary>
    public bool Unused => Declared && Uses.Count == 0;

    /// <summary>有错误级核对结果。</summary>
    public bool HasError => Diagnostics.Any(item => item.Severity == WorkflowValidationSeverity.Error);

    /// <summary>状态文字：类型不一致 / 未登记 / 未使用 / 正常。</summary>
    public string StatusText => HasError ? "有错误" : !Declared ? "未登记" : Unused ? "未使用" : Diagnostics.Count > 0 ? "有警告" : "正常";
}

/// <summary>
/// 公共数据清单页的模型（R2-d）：列出流程登记的公共数据和节点实际的使用，支持登记、修改、删除、
/// 把节点用到但没登记的项补进清单。修改都经过设计器会话，可以撤销。
/// </summary>
/// <remarks>清单登记在根文档上，子画布与根画布共用；核对覆盖根画布和全部子画布。</remarks>
public sealed class WorkflowPublicDataManifestModel : IDisposable
{
    private readonly WorkflowDesignerSession _session;
    private readonly Func<WorkflowDocument> _rootDocument;
    private IReadOnlyList<WorkflowPublicDataRow> _rows = Array.Empty<WorkflowPublicDataRow>();
    private IReadOnlyList<WorkflowValidationError> _diagnostics = Array.Empty<WorkflowValidationError>();

    /// <summary>初始化模型。</summary>
    /// <param name="session">根画布会话（子画布会话也可以，清单共用）。</param>
    /// <param name="rootDocument">取根文档；为空时用会话的文档。</param>
    public WorkflowPublicDataManifestModel(WorkflowDesignerSession session, Func<WorkflowDocument>? rootDocument = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _rootDocument = rootDocument ?? (() => session.Document);
        _session.Changed += OnSessionChanged;
        Refresh();
    }

    /// <summary>清单与使用的合并结果：先按清单顺序列出已登记的，再列出未登记的使用。</summary>
    public IReadOnlyList<WorkflowPublicDataRow> Rows => _rows;

    /// <summary>全部核对结果。</summary>
    public IReadOnlyList<WorkflowValidationError> Diagnostics => _diagnostics;

    /// <summary>节点用到但还没登记的项数。</summary>
    public int UndeclaredCount => _rows.Count(row => !row.Declared);

    /// <summary>行或核对结果变化时发生。</summary>
    public event EventHandler? Changed;

    /// <summary>重新收集使用并核对。</summary>
    public void Refresh()
    {
        var document = _rootDocument();
        var uses = WorkflowPublicDataManifest.CollectUses(document);
        _diagnostics = ValidateAgainstSession(document);
        var byKey = uses.GroupBy(use => use.Declaration.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<WorkflowPublicDataUse>)group.ToArray(), StringComparer.Ordinal);
        var rows = new List<WorkflowPublicDataRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in _session.PublicData)
        {
            if (declaration is null || !seen.Add(declaration.Key ?? string.Empty)) continue;
            rows.Add(new WorkflowPublicDataRow(declaration, true,
                byKey.TryGetValue(declaration.Key ?? string.Empty, out var used) ? used : Array.Empty<WorkflowPublicDataUse>(),
                DiagnosticsFor(declaration.Key ?? string.Empty, used)));
        }
        foreach (var inferred in WorkflowPublicDataManifest.Infer(document).Where(item => !seen.Contains(item.Key)))
        {
            var used = byKey[inferred.Key];
            rows.Add(new WorkflowPublicDataRow(inferred, false, used, DiagnosticsFor(inferred.Key, used)));
        }
        _rows = rows;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>登记或修改一项（按键）。原键与新键不同时视为改键：去掉原登记，再登记新键，一次撤销。</summary>
    /// <param name="declaration">登记项。</param>
    /// <param name="originalKey">修改前的键；新增时为空。</param>
    /// <exception cref="ArgumentException">登记项本身不合法（见 <see cref="Check"/>）。</exception>
    public void Save(WorkflowPublicDataDeclaration declaration, string? originalKey = null)
    {
        Guard.NotNull(declaration);
        if (Check(declaration, originalKey) is { } problem)
            throw new ArgumentException(problem, nameof(declaration));
        var item = declaration with { Key = declaration.Key.Trim(), DisplayName = string.IsNullOrWhiteSpace(declaration.DisplayName) ? null : declaration.DisplayName!.Trim() };
        if (item.Kind == WorkflowPublicDataKind.Queue) item = item with { DefaultValueText = null };
        if (string.IsNullOrWhiteSpace(item.DefaultValueText)) item = item with { DefaultValueText = null };
        var before = _session.PublicData.ToArray();
        var after = before.ToList();
        var index = after.FindIndex(existing => string.Equals(existing.Key, originalKey ?? item.Key, StringComparison.Ordinal));
        if (index >= 0) after[index] = item; else after.Add(item);
        _session.ExecuteDocumentOperation(() => Replace(after.ToArray()), () => Replace(before));
        Refresh();
    }

    /// <summary>删除一项登记；节点仍在用时，它会以“未登记”出现在列表里。</summary>
    /// <param name="key">键。</param>
    public bool Remove(string key)
    {
        var removed = _session.RemovePublicData(key);
        if (removed) Refresh();
        return removed;
    }

    /// <summary>把节点用到但没登记的项全部补进清单（一次撤销）。</summary>
    /// <returns>补进的项数。</returns>
    public int DeclareMissing()
    {
        var added = _session.DeclareMissingPublicData(_rootDocument());
        if (added.Count > 0) Refresh();
        return added.Count;
    }

    /// <summary>检查一项登记；没问题返回空，否则返回原因。</summary>
    /// <param name="declaration">登记项。</param>
    /// <param name="originalKey">修改前的键；新增时为空。</param>
    public string? Check(WorkflowPublicDataDeclaration declaration, string? originalKey = null)
    {
        Guard.NotNull(declaration);
        var key = declaration.Key?.Trim();
        if (string.IsNullOrEmpty(key)) return "键不能为空。";
        if (!string.Equals(key, originalKey, StringComparison.Ordinal)
            && _session.PublicData.Any(item => string.Equals(item.Key, key, StringComparison.Ordinal)))
            return $"键 {key} 已经登记过。";
        if (declaration.Kind == WorkflowPublicDataKind.Signal && declaration.ValueType != E_SignalValueType.Boolean)
            return "bool 信号的值类型必须是 Boolean。";
        if (declaration.Kind != WorkflowPublicDataKind.Queue && !string.IsNullOrWhiteSpace(declaration.DefaultValueText)
            && !CanConvert(declaration.DefaultValueText!.Trim(), declaration.ValueType))
            return $"默认值 \"{declaration.DefaultValueText}\" 不是 {declaration.ValueType}。";
        return null;
    }

    /// <summary>解除事件订阅。</summary>
    public void Dispose() => _session.Changed -= OnSessionChanged;

    private void Replace(WorkflowPublicDataDeclaration[] items)
    {
        _session.PublicData.Clear();
        _session.PublicData.AddRange(items);
    }

    // 子画布会话与根会话共用同一个清单列表，所以直接按根文档核对。
    private static IReadOnlyList<WorkflowValidationError> ValidateAgainstSession(WorkflowDocument document) =>
        WorkflowPublicDataManifest.Validate(document);

    private IReadOnlyList<WorkflowValidationError> DiagnosticsFor(string key, IReadOnlyList<WorkflowPublicDataUse>? uses)
    {
        _ = uses;
        // 核对结果的文字写成“公共数据 键 …”或“信号 键 …”，按键后面的分隔符匹配，避免 A 与 AB 互相误配。
        var markers = new[] { " " + key + " ", " " + key + "（", " " + key + "。", " " + key + "，" };
        return _diagnostics.Where(item => markers.Any(marker => item.Message.IndexOf(marker, StringComparison.Ordinal) >= 0)).ToArray();
    }

    private void OnSessionChanged(object? sender, WorkflowDesignerChangedEventArgs e)
    {
        if (e.Kind == WorkflowDesignerChangeKind.Document) Refresh();
    }

    private static bool CanConvert(string text, E_SignalValueType type)
    {
        try
        {
            _ = WorkflowPublicDataValues.Convert(text, type);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}
