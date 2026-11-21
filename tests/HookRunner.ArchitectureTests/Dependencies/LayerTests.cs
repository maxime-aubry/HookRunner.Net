using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace HookRunner.ArchitectureTests;

/// <summary>Scénarios vérifiant les dépendances autorisées entre les couches de production.</summary>
public sealed class LayerTests
{
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal)
    {
        ["HookRunner.Domain"] = [],
        ["HookRunner.Application"] = ["HookRunner.Domain"],
        ["HookRunner.Infrastructure"] = ["HookRunner.Domain", "HookRunner.Application"],
        ["HookRunner.Cli"] = ["HookRunner.Application", "HookRunner.Infrastructure"],
    };

    /// <summary>
    /// Vérifie que chaque projet de production découvert possède une entrée dans le graphe autorisé.
    /// </summary>
    [Fact]
    public void EveryProductionProjectHasAnArchitecturePolicy()
    {
        string root = RepositoryRoot();
        string[] projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.Equal(Allowed.Keys.Order(), projects.Select(Path.GetFileNameWithoutExtension).Order());
    }

    /// <summary>
    /// Compare les références évaluées par MSBuild au graphe exhaustif des dépendances autorisées.
    /// </summary>
    /// <param name="assembly">Nom du projet de production dont l'inventaire doit être vérifié.</param>
    /// <remarks>Exige l'inventaire produit par la compilation sous artifacts/inventory.</remarks>
    [Theory]
    [InlineData("HookRunner.Domain")]
    [InlineData("HookRunner.Application")]
    [InlineData("HookRunner.Infrastructure")]
    [InlineData("HookRunner.Cli")]
    public void EvaluatedProjectReferencesMatchThePolicy(string assembly)
    {
        string manifest = Path.Combine(RepositoryRoot(), "artifacts", "inventory", assembly + ".txt");
        Assert.True(File.Exists(manifest), $"Missing evaluated build inventory: {manifest}");
        string[] lines = File.ReadAllLines(manifest);
        Assert.Equal(assembly, lines[1]);
        string[] references = [.. lines.Skip(4).Select(path => Path.GetFileNameWithoutExtension(path))];
        Assert.Equal(Allowed[assembly].Order(), references.Order());
    }

    /// <summary>
    /// Inspecte les références d'assembly compilées et refuse tout lien vers une couche non autorisée.
    /// </summary>
    /// <param name="assembly">Nom de l'assembly de production à lire dans le dossier de sortie des tests.</param>
    [Theory]
    [InlineData("HookRunner.Domain")]
    [InlineData("HookRunner.Application")]
    [InlineData("HookRunner.Infrastructure")]
    [InlineData("HookRunner.Cli")]
    public void CompiledAssembliesHaveNoForbiddenLayerDependencies(string assembly)
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"));
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Assert.Equal(assembly, metadata.GetString(metadata.GetAssemblyDefinition().Name));
        string[] references = [.. metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(name => name.StartsWith("HookRunner.", StringComparison.Ordinal))];
        Assert.All(references, reference => Assert.Contains(reference, Allowed[assembly]));
    }

    /// <summary>
    /// Vérifie dans l'en-tête PE que Cli reste une bibliothèque et laisse l'exécutable au consommateur.
    /// </summary>
    [Fact]
    public void CliRemainsAClassLibrary()
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "HookRunner.Cli.dll"));
        using PEReader pe = new(stream);
        Assert.True(pe.PEHeaders.IsDll, "HookRunner.Cli must remain a class library; the consumer supplies the executable.");
    }

    /// <summary>Remonte depuis le dossier des tests jusqu'à HookRunner.slnx.</summary>
    /// <returns>Le chemin absolu de la racine du dépôt.</returns>
    /// <exception cref="DirectoryNotFoundException">
    /// La solution n'est trouvée dans aucun dossier ascendant.
    /// </exception>
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
