namespace ScriptEngine.WinForms;

/// <summary>脚本编辑器定义跳转请求。</summary>
public sealed class ScriptDefinitionRequestedEventArgs : EventArgs
{
    /// <summary>创建指定源码位置的请求。</summary>
    public ScriptDefinitionRequestedEventArgs(int position) => Position = position;

    /// <summary>请求定义的字符位置。</summary>
    public int Position { get; }
}
