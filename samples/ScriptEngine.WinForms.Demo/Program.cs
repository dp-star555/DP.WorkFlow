using System.Reflection;
using System.Runtime.Loader;

namespace ScriptEngine.WinForms.Demo;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += ResolveSingleFileSatellite;
        var scintillaPath = FindScintillaAssembly();
        if (scintillaPath is not null) AssemblyLoadContext.Default.LoadFromAssemblyPath(scintillaPath);
        ApplicationConfiguration.Initialize();
        if (args.Any(argument => string.Equals(argument, "--single-file-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            RunSingleFileSmoke();
            return;
        }
        var formType = typeof(Program).Assembly.GetType(
            "ScriptEngine.WinForms.Demo.DemoForm",
            throwOnError: true)!;
        using var form = (Form)Activator.CreateInstance(formType)!;
        Application.Run(form);
    }

    private static void RunSingleFileSmoke()
    {
        using var editor = new global::ScriptEngine.WinForms.RoslynScriptEditorControl
        {
            LiveDiagnosticsEnabled = false,
            Text = "using ScriptEngine; public sealed class Program : ICSharpProgram { public ValueTask<object?> ExecuteAsync(CSharpProgramContext context, CancellationToken cancellationToken) => new ValueTask<object?>(42); }"
        };
        editor.CreateControl();
        var diagnostics = editor.CompileProgram();
        if (diagnostics.Any(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error))
            throw new InvalidOperationException(string.Join(Environment.NewLine, diagnostics.Select(item => item.Message)));
    }

    private static Assembly? ResolveSingleFileSatellite(AssemblyLoadContext context, AssemblyName name)
    {
        if (!string.Equals(name.Name, "Scintilla.NET", StringComparison.OrdinalIgnoreCase)) return null;
        var path = FindScintillaAssembly();
        return path is null ? null : context.LoadFromAssemblyPath(path);
    }

    private static string? FindScintillaAssembly()
    {
        var candidates = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath)
        };
        return candidates
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory!, "Scintilla.NET.dll"))
            .FirstOrDefault(File.Exists);
    }
}
