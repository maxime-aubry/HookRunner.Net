using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Confinement physique du chemin d'état, indépendant des lettres de lecteur.</summary>
public sealed class StateFilePathResolverTests
{
    /// <summary>Un dossier frère partageant le préfixe de la solution reste extérieur à celle-ci.</summary>
    [Fact]
    public void DirectoryLinkCannotEscapeToASiblingSharingTheRootPrefix()
    {
        using SolutionFixture fixture = new();
        string root = Directory.CreateDirectory(Path.Combine(fixture.Root, "solution")).FullName;
        string sibling = Directory.CreateDirectory(Path.Combine(fixture.Root, "solution-sibling")).FullName;
        using DirectoryLink link = new(Path.Combine(root, "escape"), sibling);
        StateFilePathResolver resolver = new("escape/state.json");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => resolver.Resolve(root));

        Assert.Contains("StateFilePath must stay within the solution root", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(sibling, "state.json")));
    }

    /// <summary>Un lien vers un sous-dossier physique de la solution conserve le chemin logique demandé.</summary>
    [Fact]
    public void DirectoryLinkWithinTheSolutionPreservesTheLogicalStatePath()
    {
        using SolutionFixture fixture = new();
        string target = Directory.CreateDirectory(Path.Combine(fixture.Root, "physical")).FullName;
        using DirectoryLink link = new(Path.Combine(fixture.Root, "alias"), target);
        StateFilePathResolver resolver = new("alias/state.json");

        Assert.Equal(Path.Combine(fixture.Root, "alias", "state.json"), resolver.Resolve(fixture.Root));
        Assert.False(File.Exists(Path.Combine(target, "state.json")));
    }

    /// <summary>La racine physique elle-même n'est pas considérée comme une sortie de la solution.</summary>
    [Fact]
    public void TheSolutionRootItselfRemainsWithinTheBoundary()
    {
        using SolutionFixture fixture = new();
        StateFilePathResolver resolver = new(".");

        Assert.Equal(fixture.Root, resolver.Resolve(fixture.Root));
    }

    /// <summary>Une racine de volume conserve un seul séparateur pour reconnaître ses descendants.</summary>
    [Fact]
    public void AVolumeRootAcceptsADescendantStatePath()
    {
        using SolutionFixture fixture = new();
        string root = Path.GetPathRoot(fixture.Root)!;
        string expected = Path.Combine(fixture.Root, "state.json");
        StateFilePathResolver resolver = new(Path.GetRelativePath(root, expected));

        Assert.Equal(expected, resolver.Resolve(root));
        Assert.False(File.Exists(expected));
    }
}
