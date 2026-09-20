namespace ScriptEngine.WinForms;

/// <summary>
/// 将代码片段状态、换行保持和成对字符决策集中到一个可独立验证的输入行为模块。
/// 模块不依赖窗口句柄，Scintilla 控件只负责执行返回的文本编辑。
/// </summary>
internal sealed class ScriptEditorInputBehavior
{
    private string? _pendingSnippetShortcut;
    private int _pendingSnippetStart = -1;
    private int _pendingSnippetCaret = -1;

    public IReadOnlyList<string> SnippetShortcuts => Snippets.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();

    public ScriptSnippetDecision ProcessTab(
        string source,
        int caret,
        int selectionLength,
        bool control,
        bool alt,
        bool shift)
    {
        if (control || alt || shift || selectionLength != 0)
        {
            Reset();
            return ScriptSnippetDecision.None;
        }
        var position = Math.Max(0, Math.Min(source.Length, caret));
        var start = RoslynScriptService.GetCompletionStart(source, position);
        if (start >= position)
        {
            Reset();
            return ScriptSnippetDecision.None;
        }
        var shortcut = source.Substring(start, position - start);
        if (!Snippets.ContainsKey(shortcut))
        {
            Reset();
            return ScriptSnippetDecision.None;
        }
        if (string.Equals(_pendingSnippetShortcut, shortcut, StringComparison.OrdinalIgnoreCase)
            && _pendingSnippetStart == start
            && _pendingSnippetCaret == position)
        {
            Reset();
            return new ScriptSnippetDecision(ScriptSnippetDecisionKind.Expand, shortcut, start, position);
        }
        _pendingSnippetShortcut = shortcut;
        _pendingSnippetStart = start;
        _pendingSnippetCaret = position;
        return new ScriptSnippetDecision(ScriptSnippetDecisionKind.Armed, shortcut, start, position);
    }

    public bool TryCreateSnippetEdit(
        string shortcut,
        string source,
        int replaceStart,
        int replaceEnd,
        string selectedText,
        out ScriptSnippetEdit edit)
    {
        if (!Snippets.TryGetValue(shortcut, out var template))
        {
            edit = default;
            return false;
        }
        var start = Math.Max(0, Math.Min(source.Length, replaceStart));
        var end = Math.Max(start, Math.Min(source.Length, replaceEnd));
        var lineStart = source.LastIndexOf('\n', Math.Max(0, start - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var indentationEnd = lineStart;
        while (indentationEnd < start && source[indentationEnd] is ' ' or '\t') indentationEnd++;
        var indentation = source.Substring(lineStart, indentationEnd - lineStart);
        var newline = DetectLineEnding(source);
        var expanded = template
            .Replace("$selection$", selectedText ?? string.Empty)
            .Replace("\n", newline + indentation);
        var marker = expanded.IndexOf("$end$", StringComparison.Ordinal);
        expanded = expanded.Replace("$end$", string.Empty);
        edit = new ScriptSnippetEdit(
            start,
            end - start,
            expanded,
            start + (marker >= 0 ? marker : expanded.Length));
        return true;
    }

    public ScriptCompletionEdit CreateCompletionEdit(
        RoslynScriptCompletionItem item,
        string source,
        int caret)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        var position = Math.Max(0, Math.Min(source.Length, caret));
        var fallbackStart = RoslynScriptService.GetCompletionStart(source, position);
        var start = Math.Max(0, Math.Min(source.Length, item.ReplacementStart ?? fallbackStart));
        var originalEnd = start + (item.ReplacementLength ?? Math.Max(0, position - start));
        var extendedSimpleSpan = item.ReplacementStart == fallbackStart && originalEnd < position;
        IReadOnlyList<RoslynScriptTextChange> changes;
        if (item.TextChanges is { Count: > 1 })
        {
            changes = item.TextChanges.Select(change =>
            {
                var changeStart = Math.Max(0, Math.Min(source.Length, change.Start));
                var length = extendedSimpleSpan && change.Start == start
                    ? position - changeStart
                    : change.Length;
                return new RoslynScriptTextChange(
                    changeStart,
                    Math.Max(0, Math.Min(source.Length - changeStart, length)),
                    change.NewText);
            }).ToArray();
        }
        else
        {
            var end = Math.Max(start, Math.Min(source.Length, extendedSimpleSpan ? position : originalEnd));
            changes = new[] { new RoslynScriptTextChange(start, end - start, item.InsertionText) };
        }
        var newPosition = extendedSimpleSpan
            ? start + item.InsertionText.Length + item.CaretOffset
            : item.NewCaretPosition ?? start + item.InsertionText.Length + item.CaretOffset;
        var finalLength = source.Length + changes.Sum(change => change.NewText.Length - change.Length);
        return new ScriptCompletionEdit(
            changes,
            Math.Max(0, Math.Min(finalLength, newPosition)));
    }

    public IEnumerable<RoslynScriptCompletionItem> GetSnippetCompletions(string prefix) => Snippets.Keys
        .Where(key => string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        .Select(key => new RoslynScriptCompletionItem(
            key + " 代码片段",
            key,
            "插入 C# 代码片段",
            RoslynScriptCompletionKind.Snippet,
            FilterText: key,
            SortText: key,
            InlineDescription: "代码片段"));

    public void Reset()
    {
        _pendingSnippetShortcut = null;
        _pendingSnippetStart = -1;
        _pendingSnippetCaret = -1;
    }

    public static bool ShouldApplyAutomaticIndent(char character, string trimmedLine) => character switch
    {
        '\n' => true,
        '}' => string.Equals(trimmedLine, "}", StringComparison.Ordinal),
        '{' => trimmedLine is "{" or "{}",
        _ => false
    };

    public static char GetAutomaticClosingCharacter(
        char character,
        int selectionLength,
        char? nextCharacter,
        bool shouldPairQuote)
    {
        if (selectionLength != 0) return '\0';
        var closing = character switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0'
        };
        if (closing == '\0' || character is '"' or '\'' && !shouldPairQuote) return '\0';
        return nextCharacter is null || char.IsWhiteSpace(nextCharacter.Value) || ")]};,".IndexOf(nextCharacter.Value) >= 0
            ? closing
            : '\0';
    }

    public static bool TryGetPairedBraceOpening(string source, int caret, out int openingPosition)
    {
        openingPosition = -1;
        if (caret <= 0 || caret >= source.Length || source[caret] != '}') return false;
        var previous = caret - 2;
        while (previous >= 0 && source[previous] is ' ' or '\t' or '\r') previous--;
        if (previous < 0 || source[previous] != '{') return false;
        openingPosition = previous;
        return true;
    }

    public static bool ShouldDeletePair(string source, int caret, int selectionLength) =>
        selectionLength == 0
        && caret > 0
        && caret < source.Length
        && IsPair(source[caret - 1], source[caret]);

    public static bool ShouldSkipClosingCharacter(string source, int caret, int selectionLength, char character) =>
        selectionLength == 0 && caret >= 0 && caret < source.Length && source[caret] == character;

    private static string DetectLineEnding(string source)
    {
        var lineFeed = source.IndexOf('\n');
        if (lineFeed < 0) return Environment.NewLine;
        return lineFeed > 0 && source[lineFeed - 1] == '\r' ? "\r\n" : "\n";
    }

    private static bool IsPair(char opening, char closing) =>
        opening == '(' && closing == ')'
        || opening == '[' && closing == ']'
        || opening == '{' && closing == '}'
        || opening == '"' && closing == '"'
        || opening == '\'' && closing == '\'';

    private static readonly IReadOnlyDictionary<string, string> Snippets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["if"] = "if (true)\n{\n    $end$\n}",
            ["switch"] = "switch (value)\n{\n    case 0:\n        $end$\n        break;\n    default:\n        break;\n}",
            ["for"] = "for (var index = 0; index < count; index++)\n{\n    $end$\n}",
            ["foreach"] = "foreach (var item in items)\n{\n    $end$\n}",
            ["try"] = "try\n{\n    $end$\n}\ncatch (Exception exception)\n{\n    throw;\n}",
            ["using"] = "using (var resource = value)\n{\n    $end$\n}",
            ["class"] = "public sealed class ClassName\n{\n    $end$\n}",
            ["program"] = "using ScriptEngine;\n\npublic sealed class Program : ICSharpProgram\n{\n    public async ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken)\n    {\n        $end$\n        return null;\n    }\n}"
        };
}

internal enum ScriptSnippetDecisionKind
{
    None,
    Armed,
    Expand
}

internal readonly record struct ScriptSnippetDecision(
    ScriptSnippetDecisionKind Kind,
    string Shortcut,
    int Start,
    int End)
{
    public static ScriptSnippetDecision None { get; } = new(ScriptSnippetDecisionKind.None, string.Empty, 0, 0);
}

internal readonly record struct ScriptSnippetEdit(
    int Start,
    int Length,
    string NewText,
    int NewCaretPosition);

internal sealed record ScriptCompletionEdit(
    IReadOnlyList<RoslynScriptTextChange> Changes,
    int NewCaretPosition);
