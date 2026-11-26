using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Résolution physique observée avec les entrées réelles du volume de test.</summary>
public sealed class CanonicalPathTests
{
    /// <summary>Un suffixe absent est conservé sans créer de fichier ni de dossier.</summary>
    [Fact]
    public void MissingSegmentsArePreservedWithoutCreatingEntries()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string expected = Path.Combine(root, "missing", "nested", "state.json");

        string resolved = CanonicalPath.Resolve(expected);

        Assert.Equal(expected, resolved);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "missing")));
    }

    /// <summary>Les segments relatifs et le séparateur final ne modifient pas le dossier physique.</summary>
    [Fact]
    public void RelativeSegmentsAndTrailingSeparatorAreNormalized()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string path = Path.Combine(root, "agent", "..", "agent", "nested") + Path.DirectorySeparatorChar;

        Assert.Equal(Path.Combine(root, "agent", "nested"), CanonicalPath.Resolve(path));
    }

    /// <summary>La casse physique est retrouvée uniquement lorsque le volume accepte l'alias demandé.</summary>
    [Fact]
    public void CaseAliasesFollowTheVolumeSemanticsForDirectoriesAndFiles()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string directory = Path.Combine(root, "CaseDirectory");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "CaseFile.json"), "{}");
        string directoryAlias = Path.Combine(root, "casedirectory");
        string fileAlias = Path.Combine(directory, "casefile.json");
        string expectedDirectory = Directory.Exists(directoryAlias) ? directory : directoryAlias;
        string expectedFile = File.Exists(fileAlias) ? Path.Combine(directory, "CaseFile.json") : fileAlias;

        Assert.Equal(expectedDirectory, CanonicalPath.Resolve(directoryAlias));
        Assert.Equal(expectedFile, CanonicalPath.Resolve(fileAlias));
    }

    /// <summary>Deux noms qui ne diffèrent que par la casse gardent leur identité lorsque le volume les distingue.</summary>
    [Fact]
    public void ExactSpellingTakesPriorityWhenCaseVariantsCoexist()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string first = Path.Combine(root, "CaseRoot");
        string second = Path.Combine(root, "caseroot");
        Directory.CreateDirectory(first);
        bool sameDirectory = Directory.Exists(second);
        Directory.CreateDirectory(second);

        Assert.Equal(first, CanonicalPath.Resolve(first));
        Assert.Equal(sameDirectory ? first : second, CanonicalPath.Resolve(second));
    }

    /// <summary>Un lien intermédiaire est résolu avant de conserver ou retrouver les segments suivants.</summary>
    /// <param name="relativePath">Suffixe demandé sous le lien de dossier.</param>
    /// <param name="exists">Indique si le fichier cible doit déjà exister.</param>
    [Theory]
    [InlineData("state.json", true)]
    [InlineData("missing/nested/state.json", false)]
    public void IntermediateDirectoryLinksResolveBeforeTheRemainingSegments(string relativePath, bool exists)
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string target = Directory.CreateDirectory(Path.Combine(root, "physical")).FullName;
        string suffix = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string expected = Path.Combine(target, suffix);
        if (exists)
        {
            File.WriteAllText(expected, "{}");
        }

        string alias = Path.Combine(root, "alias");
        using DirectoryLink link = new(alias, target);

        Assert.Equal(expected, CanonicalPath.Resolve(Path.Combine(alias, suffix)));
        Assert.Equal(exists, File.Exists(expected));
    }

    /// <summary>Le parcours continue dans une cible contenant elle-même un lien vers un autre dossier.</summary>
    [Fact]
    public void NestedDirectoryLinksResolveToTheFinalPhysicalFile()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string firstTarget = Directory.CreateDirectory(Path.Combine(root, "first-target")).FullName;
        string finalTarget = Directory.CreateDirectory(Path.Combine(root, "final-target")).FullName;
        string expected = Path.Combine(finalTarget, "state.json");
        File.WriteAllText(expected, "{}");
        using DirectoryLink inner = new(Path.Combine(firstTarget, "inner"), finalTarget);
        using DirectoryLink outer = new(Path.Combine(root, "outer"), firstTarget);

        Assert.Equal(expected, CanonicalPath.Resolve(Path.Combine(root, "outer", "inner", "state.json")));
    }

    /// <summary>Un alias dont la cible est un autre alias retrouve la même racine physique.</summary>
    [Fact]
    public void ChainedDirectoryLinksResolveToTheFinalPhysicalDirectory()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string target = Directory.CreateDirectory(Path.Combine(root, "physical")).FullName;
        string firstAlias = Path.Combine(root, "first-alias");
        string secondAlias = Path.Combine(root, "second-alias");
        using DirectoryLink first = new(firstAlias, target);
        using DirectoryLink second = new(secondAlias, firstAlias);

        Assert.Equal(target, CanonicalPath.Resolve(secondAlias));
    }

    /// <summary>Un lien dont la cible a disparu conserve le chemin physique fourni par le système.</summary>
    [Fact]
    public void BrokenDirectoryLinksPreserveTheMissingTargetPath()
    {
        using SolutionFixture fixture = new();
        string root = CanonicalPath.Resolve(fixture.Root);
        string target = Directory.CreateDirectory(Path.Combine(root, "removed-target")).FullName;
        string alias = Path.Combine(root, "alias");
        using DirectoryLink link = new(alias, target);
        Directory.Delete(target);

        Assert.Equal(Path.Combine(target, "state.json"), CanonicalPath.Resolve(Path.Combine(alias, "state.json")));
        Assert.False(Directory.Exists(target));
    }

    /// <summary>Une boucle de liens refusée par le système remonte comme erreur technique.</summary>
    [Fact]
    public void DirectoryLinkCyclesPropagateTheResolutionError()
    {
        using SolutionFixture fixture = new();
        string first = Path.Combine(fixture.Root, "first-alias");
        string second = Path.Combine(fixture.Root, "second-alias");
        using DirectoryLink firstLink = new(first, second);
        using DirectoryLink secondLink = new(second, first);

        Assert.Throws<IOException>(() => CanonicalPath.Resolve(first));
    }
}
