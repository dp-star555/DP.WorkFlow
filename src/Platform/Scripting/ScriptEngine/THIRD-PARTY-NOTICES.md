# ScriptEngine third-party notices

| Package / runtime asset | Version | Purpose | License |
|---|---:|---|---|
| Microsoft.CodeAnalysis.CSharp / Common | 4.11.0 | C# parsing, semantic analysis and compilation | MIT |
| Microsoft.CodeAnalysis.CSharp.Workspaces / Workspaces.Common | 4.11.0 | Optional project editing, symbol rename and semantic imports | MIT |
| Microsoft.CodeAnalysis.CSharp.Features / Features | 4.11.0 | Optional context-aware completion and completion commit changes | MIT |
| Humanizer.Core / System.Composition | 2.14.1 / 8.0.0 | Roslyn Workspaces transitive dependencies | MIT |
| Scintilla.NET | 5.3.2.9 | WinForms source editor wrapper | MIT |
| Scintilla / Lexilla native libraries | supplied by Scintilla.NET | Native editor and lexer runtime | Scintilla license |
| System.Collections.Immutable | 8.0.0 | Roslyn transitive dependency | MIT |
| System.Reflection.Metadata | 8.0.0 | Roslyn transitive dependency | MIT |
| System.Threading.Tasks.Extensions | 4.5.4 | .NET Framework 4.8 compatibility | MIT |

`Microsoft.NETFramework.ReferenceAssemblies` is a build-only package and is not part of the application runtime payload.

Release pipelines should retain the license files shipped by NuGet packages and ensure the architecture-appropriate Scintilla native directory is included.
