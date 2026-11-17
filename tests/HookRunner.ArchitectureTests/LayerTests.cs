using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace HookRunner.ArchitectureTests;

public sealed class LayerTests
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["HookRunner.Core"] = [],
        ["HookRunner.Application"] = ["HookRunner.Core"],
        ["HookRunner.Infrastructure"] = ["HookRunner.Core"],
        ["HookRunner.CLI"] = ["HookRunner.Application", "HookRunner.Infrastructure"],
    };

    [Fact]
    public void EveryProductionProjectHasAnArchitecturePolicy()
    {
        string root = RepositoryRoot();
        string[] projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(Allowed.Keys.Order(), projects.Select(Path.GetFileNameWithoutExtension).Order());
    }

    [Theory]
    [InlineData("HookRunner.Core")]
    [InlineData("HookRunner.Application")]
    [InlineData("HookRunner.Infrastructure")]
    [InlineData("HookRunner.CLI")]
    public void EvaluatedProjectReferencesMatchThePolicy(string assembly)
    {
        string manifest = Path.Combine(RepositoryRoot(), "artifacts", "inventory", assembly + ".txt");
        Assert.True(File.Exists(manifest), $"Missing evaluated build inventory: {manifest}");
        string[] lines = File.ReadAllLines(manifest);
        Assert.Equal(assembly, lines[1]);
        string[] references = [.. lines.Skip(4).Select(path => Path.GetFileNameWithoutExtension(path))];
        Assert.Equal(Allowed[assembly].Order(), references.Order());
    }

    [Theory]
    [InlineData("HookRunner.Core")]
    [InlineData("HookRunner.Application")]
    [InlineData("HookRunner.Infrastructure")]
    [InlineData("HookRunner.CLI")]
    public void CompiledAssembliesHaveNoForbiddenLayerDependencies(string assembly)
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"));
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        string[] references = [.. metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(name => name.StartsWith("HookRunner.", StringComparison.Ordinal))];
        Assert.All(references, reference => Assert.Contains(reference, Allowed[assembly]));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HookRunner.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot locate HookRunner.slnx");
    }
}
