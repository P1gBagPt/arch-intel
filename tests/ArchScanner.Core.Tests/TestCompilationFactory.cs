using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ArchScanner.Core.Tests;

/// <summary>
/// Builds minimal in-memory compilations from string literals — no MSBuildWorkspace, no disk I/O
/// (Section 8.1). Shared across discovery/resolution/heuristic unit tests.
/// </summary>
public static class TestCompilationFactory
{
    public static (Compilation Compilation, SyntaxTree Tree) CreateSingleFile(string code, string fileName = "Test.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(code, path: fileName);
        var compilation = Create([tree]);
        return (compilation, tree);
    }

    /// <summary>Compiles `code` alongside <see cref="FrameworkStubs.Source"/> so FQN checks against MediatR/EF Core/ASP.NET Core/etc. resolve without real package references.</summary>
    public static (Compilation Compilation, SyntaxTree Tree) CreateWithFrameworkStubs(string code, string fileName = "Test.cs")
    {
        var stubsTree = CSharpSyntaxTree.ParseText(FrameworkStubs.Source, path: "FrameworkStubs.cs");
        var tree = CSharpSyntaxTree.ParseText(code, path: fileName);
        var compilation = Create([stubsTree, tree]);
        return (compilation, tree);
    }

    public static Compilation Create(IEnumerable<SyntaxTree> trees)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };

        var netCoreAssemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var assemblyName in new[] { "System.Runtime", "System.Collections", "netstandard", "System.Linq" })
        {
            var path = Path.Combine(netCoreAssemblyPath, assemblyName + ".dll");
            if (File.Exists(path))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
