using System.Globalization;
using System.Resources;
using System.Text;

namespace ModernUI.Localization;

/// <summary>稳定标识一个模块拥有的用户文本。</summary>
public readonly record struct TextKey(string Module, string Name)
{
    public override string ToString() => $"{Module}.{Name}";
}

public enum TextDirection { LeftToRight, RightToLeft }

public interface ITextCatalog
{
    bool TryGetText(TextKey key, CultureInfo culture, out string text);
}

public interface ILocalizationContext
{
    LocalizationSnapshot Current { get; }
    event EventHandler<LocaleChangedEventArgs> Changed;
    string Text(TextKey key, IReadOnlyDictionary<string, object?>? arguments = null);
}

public interface ILocalizationManager
{
    ILocalizationContext Context { get; }
    Task<LocaleChangeResult> ChangeLocaleAsync(CultureInfo culture, CancellationToken cancellationToken = default);
}

public sealed record LocalizationSnapshot(CultureInfo Culture, ITextCatalog TextCatalog, TextDirection TextDirection);
public sealed class LocaleChangedEventArgs(LocalizationSnapshot previous, LocalizationSnapshot current) : EventArgs
{
    public LocalizationSnapshot Previous { get; } = previous;
    public LocalizationSnapshot Current { get; } = current;
}
public sealed record LocaleChangeResult(bool Changed, CultureInfo Culture);

/// <summary>把模块 ResourceManager 适配到统一文本目录。</summary>
public sealed class ResourceManagerTextCatalog(string module, ResourceManager resourceManager) : ITextCatalog
{
    public bool TryGetText(TextKey key, CultureInfo culture, out string text)
    {
        text = string.Empty;
        if (!string.Equals(key.Module, module, StringComparison.Ordinal)) return false;
        text = resourceManager.GetString(key.Name, culture) ?? string.Empty;
        return text.Length > 0;
    }
}

/// <summary>按注册顺序组合多个模块资源目录。</summary>
public sealed class CompositeTextCatalog(params ITextCatalog[] catalogs) : ITextCatalog
{
    private readonly IReadOnlyList<ITextCatalog> _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
    public bool TryGetText(TextKey key, CultureInfo culture, out string text)
    {
        foreach (var catalog in _catalogs)
            if (catalog.TryGetText(key, culture, out text)) return true;
        text = string.Empty;
        return false;
    }
}

/// <summary>线程安全地发布不可变本地化快照，并串行化语言切换。</summary>
public sealed class LocalizationManager : ILocalizationManager, IDisposable
{
    private readonly Func<CultureInfo, CancellationToken, Task<LocalizationSnapshot>> _snapshotLoader;
    private readonly SemaphoreSlim _switchLock = new(1, 1);
    private readonly MutableLocalizationContext _context;
    private readonly SynchronizationContext? _synchronizationContext = SynchronizationContext.Current;

    public LocalizationManager(LocalizationSnapshot initial,
        Func<CultureInfo, CancellationToken, Task<LocalizationSnapshot>> snapshotLoader)
    {
        _context = new MutableLocalizationContext(initial ?? throw new ArgumentNullException(nameof(initial)));
        _snapshotLoader = snapshotLoader ?? throw new ArgumentNullException(nameof(snapshotLoader));
    }
    public ILocalizationContext Context => _context;
    public async Task<LocaleChangeResult> ChangeLocaleAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        CompatibilityGuard.NotNull(culture, nameof(culture));
        await _switchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.Equals(_context.Current.Culture.Name, culture.Name, StringComparison.OrdinalIgnoreCase))
                return new LocaleChangeResult(false, _context.Current.Culture);
            var snapshot = await _snapshotLoader(culture, cancellationToken).ConfigureAwait(false);
            await PublishAsync(snapshot).ConfigureAwait(false);
            return new LocaleChangeResult(true, snapshot.Culture);
        }
        finally { _switchLock.Release(); }
    }
    private Task PublishAsync(LocalizationSnapshot snapshot)
    {
        if (_synchronizationContext is null || ReferenceEquals(SynchronizationContext.Current, _synchronizationContext))
        {
            _context.Publish(snapshot);
            return Task.CompletedTask;
        }
        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synchronizationContext.Post(_ =>
        {
            try { _context.Publish(snapshot); completion.SetResult(null); }
            catch (Exception exception) { completion.SetException(exception); }
        }, null);
        return completion.Task;
    }
    public void Dispose() => _switchLock.Dispose();
}

/// <summary>适用于固定语言或测试的只读上下文。</summary>
public sealed class FixedLocalizationContext(LocalizationSnapshot snapshot) : ILocalizationContext
{
    public LocalizationSnapshot Current { get; } = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    public event EventHandler<LocaleChangedEventArgs>? Changed { add { } remove { } }
    public string Text(TextKey key, IReadOnlyDictionary<string, object?>? arguments = null) =>
        LocalizationText.Resolve(Current, key, arguments);
}

internal sealed class MutableLocalizationContext(LocalizationSnapshot initial) : ILocalizationContext
{
    private LocalizationSnapshot _current = initial;
    public LocalizationSnapshot Current => Volatile.Read(ref _current);
    public event EventHandler<LocaleChangedEventArgs>? Changed;
    public string Text(TextKey key, IReadOnlyDictionary<string, object?>? arguments = null) =>
        LocalizationText.Resolve(Current, key, arguments);
    public void Publish(LocalizationSnapshot snapshot)
    {
        CompatibilityGuard.NotNull(snapshot, nameof(snapshot));
        var previous = Interlocked.Exchange(ref _current, snapshot);
        Changed?.Invoke(this, new LocaleChangedEventArgs(previous, snapshot));
    }
}

/// <summary>解析和格式化 RESX 中的命名占位符，支持 {name}、{value:N2}、{{ 和 }}。</summary>
public static class LocalizationTemplate
{
    public static IReadOnlySet<string> GetPlaceholderNames(string template)
    {
        CompatibilityGuard.NotNull(template, nameof(template));
        var names = new HashSet<string>(StringComparer.Ordinal);
        Parse(template, (name, _) => { names.Add(name); return string.Empty; }, substitute: false);
#if NETSTANDARD2_0
        return new ReadOnlySet<string>(names);
#else
        return names;
#endif
    }

    public static string Format(string template, CultureInfo culture,
        IReadOnlyDictionary<string, object?>? arguments = null)
    {
        CompatibilityGuard.NotNull(template, nameof(template));
        CompatibilityGuard.NotNull(culture, nameof(culture));
        return Parse(template, (name, format) =>
        {
            if (arguments is null || !arguments.TryGetValue(name, out var value))
                throw new FormatException($"Localization placeholder '{{{name}}}' has no argument.");
            return value switch
            {
                null => string.Empty,
                IFormattable formattable => formattable.ToString(format, culture) ?? string.Empty,
                _ => Convert.ToString(value, culture) ?? string.Empty
            };
        }, substitute: true);
    }

    private static string Parse(string template, Func<string, string?, string> resolve, bool substitute)
    {
        var result = new StringBuilder(template.Length);
        for (var index = 0; index < template.Length; index++)
        {
            var character = template[index];
            if (character == '{' && index + 1 < template.Length && template[index + 1] == '{')
            {
                if (substitute) result.Append('{');
                index++;
                continue;
            }
            if (character == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                if (substitute) result.Append('}');
                index++;
                continue;
            }
            if (character == '}') throw new FormatException("Localization template contains an unmatched '}'.");
            if (character != '{')
            {
                if (substitute) result.Append(character);
                continue;
            }
            var end = template.IndexOf('}', index + 1);
            if (end < 0) throw new FormatException("Localization template contains an unmatched '{'.");
            var token = template.Substring(index + 1, end - index - 1);
            var separator = token.IndexOf(':');
            var name = (separator < 0 ? token : token.Substring(0, separator)).Trim();
            var format = separator < 0 ? null : token.Substring(separator + 1);
            if (name.Length == 0 || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.')))
                throw new FormatException($"Localization placeholder '{{{token}}}' is invalid.");
            var value = resolve(name, format);
            if (substitute) result.Append(value);
            index = end;
        }
        return result.ToString();
    }
}

public sealed record LocalizationResourceIssue(string Key, string Message);

/// <summary>验证目标语言的 Key 集和命名占位符是否与默认资源一致。</summary>
public static class LocalizationResourceValidator
{
    public static IReadOnlyList<LocalizationResourceIssue> Validate(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> target)
    {
        CompatibilityGuard.NotNull(baseline, nameof(baseline));
        CompatibilityGuard.NotNull(target, nameof(target));
        var issues = new List<LocalizationResourceIssue>();
        foreach (var pair in baseline)
        {
            var key = pair.Key;
            var template = pair.Value;
            if (!target.TryGetValue(key, out var translated))
            {
                issues.Add(new(key, "Target resource is missing the key."));
                continue;
            }
            try
            {
                var expected = LocalizationTemplate.GetPlaceholderNames(template);
                var actual = LocalizationTemplate.GetPlaceholderNames(translated);
                if (!expected.SetEquals(actual))
                    issues.Add(new(key, $"Placeholder mismatch. Expected [{string.Join(", ", expected)}], actual [{string.Join(", ", actual)}]."));
            }
            catch (FormatException exception) { issues.Add(new(key, exception.Message)); }
        }
        foreach (var extra in target.Keys.Except(baseline.Keys, StringComparer.Ordinal))
            issues.Add(new(extra, "Target resource contains an unknown key."));
        return issues;
    }
}

internal static class LocalizationText
{
    public static string Resolve(LocalizationSnapshot snapshot, TextKey key, IReadOnlyDictionary<string, object?>? arguments)
    {
        if (!TryWithFallback(snapshot.TextCatalog, key, snapshot.Culture, out var template)) return key.ToString();
        return LocalizationTemplate.Format(template, snapshot.Culture, arguments);
    }
    private static bool TryWithFallback(ITextCatalog catalog, TextKey key, CultureInfo culture, out string text)
    {
        for (var current = culture; ; current = current.Parent)
        {
            if (catalog.TryGetText(key, current, out text)) return true;
            if (current.Equals(CultureInfo.InvariantCulture)) break;
        }
        text = string.Empty;
        return false;
    }
}
