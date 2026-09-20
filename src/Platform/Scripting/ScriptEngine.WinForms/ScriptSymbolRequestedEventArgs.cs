namespace ScriptEngine.WinForms;

/// <summary>请求对光标所在符号执行项目级操作。</summary>
public sealed class ScriptSymbolRequestedEventArgs : EventArgs
{
    /// <summary>创建符号操作请求。</summary>
    /// <param name="position">光标所在的零基源码字符位置。</param>
    public ScriptSymbolRequestedEventArgs(int position) => Position = position;

    /// <summary>光标所在的零基源码字符位置。</summary>
    public int Position { get; }
}
