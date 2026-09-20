namespace ScriptEngine.Language;

/// <summary>核心与桌面适配器共享的确定性补全匹配排序。</summary>
internal static class RoslynScriptCompletionMatcher
{
    public static int GetScore(string candidate, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 100;
        if (candidate.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return 0;
        if (candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            return 10 + candidate.Length - pattern.Length;
        var contains = candidate.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
        if (contains >= 0) return 100 + contains;

        var patternIndex = 0;
        var gaps = 0;
        for (var index = 0; index < candidate.Length && patternIndex < pattern.Length; index++)
        {
            if (char.ToUpperInvariant(candidate[index]) == char.ToUpperInvariant(pattern[patternIndex])) patternIndex++;
            else if (patternIndex > 0) gaps++;
        }
        return patternIndex == pattern.Length ? 300 + gaps : int.MaxValue;
    }
}
