using HookRunner.Application;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Sélection exacte de projets à travers les options publiques préparées.</summary>
public sealed class ProjectSelectionTests
{
    /// <summary>Après le premier dossier de solutions, les autres niveaux sont ignorés.</summary>
    /// <param name="requested">Nom uniquement référencé dans un autre niveau, ou nom local valide.</param>
    [Theory]
    [InlineData("Local")]
    [InlineData("Parent")]
    [InlineData("Child")]
    public void StopsAtTheNearestSolutionDirectory(string requested)
    {
        using SolutionFixture fixture = new();
        string nearest = Path.Combine(fixture.Root, "agent");
        CreateProject(fixture.Root, "Parent.csproj");
        string local = CreateProject(nearest, "Local.csproj");
        CreateProject(nearest, "nested/Child.csproj");
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"),
            "<Solution><Project Path=\"Parent.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(nearest, "Nearest.SLNX"),
            "<Solution><Project Path=\"Local.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(nearest, "nested", "Child.slnx"),
            "<Solution><Project Path=\"Child.csproj\" /></Solution>");
        InvocationData invocation = fixture.Invocation with { WorkingDirectory = nearest };
        HookOptionsPreparer preparer = new(120, "Hooks/state.json");

        if (requested == "Local")
        {
            HookOptions options = preparer.Prepare(new RunHookArgs("hook", null, [requested]), invocation);
            Assert.Equal(nearest, options.SolutionRoot);
            Assert.Equal(local, Assert.Single(options.Projects).ProjectFilePath);
        }
        else
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                preparer.Prepare(new RunHookArgs("hook", null, [requested]), invocation));
            Assert.Contains("not referenced", exception.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Deux références au même fichier physique ne rendent pas son nom ambigu.</summary>
    [Fact]
    public void DeduplicatesReferencesThroughADirectoryAlias()
    {
        using SolutionFixture fixture = new();
        string project = CreateProject(fixture.Root, "src/Api.csproj");
        using DirectoryLink link = new(Path.Combine(fixture.Root, "alias"), Path.Combine(fixture.Root, "src"));
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"),
            "<Solution><Project Path=\"src/Api.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(fixture.Root, "Other.slnx"),
            "<Solution><Project Path=\"alias/Api.csproj\" /></Solution>");

        HookOptions options = new HookOptionsPreparer(120, "Hooks/state.json")
            .Prepare(new RunHookArgs("hook", null, ["Api"]), fixture.Invocation);

        Assert.Equal(project, Assert.Single(options.Projects).ProjectFilePath);
    }

    /// <summary>Des noms distincts vers le même fichier physique ne sélectionnent qu'un projet.</summary>
    /// <param name="aliasFirst">True demande l'alias en premier et conserve ce nom dans les options.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeduplicatesDifferentFileNamesForTheSamePhysicalProject(bool aliasFirst)
    {
        using SolutionFixture fixture = new();
        string project = CreateProject(fixture.Root, "src/Api.csproj");
        string alias = Path.Combine(fixture.Root, "Alias.csproj");
        CreateFileAlias(alias, project);
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"),
            "<Solution><Project Path=\"src/Api.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(fixture.Root, "Other.slnx"),
            "<Solution><Project Path=\"Alias.csproj\" /></Solution>");
        string[] names = ["Api", "Alias"];
        string expectedPath = project;
        if (aliasFirst)
        {
            names = ["Alias", "Api"];
            expectedPath = alias;
        }

        HookOptions options = new HookOptionsPreparer(120, "Hooks/state.json")
            .Prepare(new RunHookArgs("hook", null, names), fixture.Invocation);

        Assert.Equal(new TargetProject(names[0], expectedPath, Path.GetDirectoryName(expectedPath)!),
            Assert.Single(options.Projects));
    }

    /// <summary>Un alias ambigu reste invalide même si l'une de ses cibles a déjà été sélectionnée.</summary>
    [Fact]
    public void RejectsAnAmbiguousAliasAfterSelectingItsPhysicalTarget()
    {
        using SolutionFixture fixture = new();
        string project = CreateProject(fixture.Root, "Api.csproj");
        CreateFileAlias(Path.Combine(fixture.Root, "Alias.csproj"), project);
        CreateProject(fixture.Root, "other/Alias.csproj");
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"),
            "<Solution><Project Path=\"Api.csproj\" /><Project Path=\"Alias.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(fixture.Root, "Other.slnx"),
            "<Solution><Project Path=\"other/Alias.csproj\" /></Solution>");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new HookOptionsPreparer(120, "Hooks/state.json")
                .Prepare(new RunHookArgs("hook", null, ["Api", "Alias"]), fixture.Invocation));

        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Un projet référencé hors racine reste refusé, même derrière un lien de dossier.</summary>
    /// <param name="throughLink">True passe par un chemin logique situé dans la racine.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsProjectsOutsideThePhysicalSolutionRoot(bool throughLink)
    {
        using SolutionFixture fixture = new();
        using SolutionFixture outside = new();
        string project = CreateProject(outside.Root, "Outside.csproj");
        using DirectoryLink link = new(Path.Combine(fixture.Root, "escape"), outside.Root);
        string reference = Path.GetRelativePath(fixture.Root, project);
        if (throughLink)
        {
            reference = "escape/Outside.csproj";
        }

        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"),
            $"<Solution><Project Path=\"{reference}\" /></Solution>");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new HookOptionsPreparer(120, "Hooks/state.json")
                .Prepare(new RunHookArgs("hook", null, ["Outside"]), fixture.Invocation));

        Assert.Contains("Outside", exception.Message, StringComparison.Ordinal);
        Assert.Contains("outside the solution root", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Deux fichiers distincts restent ambigus même lorsque leurs noms diffèrent seulement par la casse.</summary>
    /// <param name="classicSolution">True utilise une solution .sln sans dossiers logiques séparant les projets.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsOnlyARequestedAmbiguousName(bool classicSolution)
    {
        using SolutionFixture fixture = new();
        CreateProject(fixture.Root, "one/Api.csproj");
        CreateProject(fixture.Root, "two/API.csproj");
        string safe = CreateProject(fixture.Root, "Safe.csproj");
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"), """
            <Solution>
              <Folder Name="/One/"><Project Path="one/Api.csproj" /></Folder>
              <Folder Name="/Two/"><Project Path="two/API.csproj" /></Folder>
              <Project Path="Safe.csproj" />
            </Solution>
            """);
        if (classicSolution)
        {
            File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(fixture.Root, "Other.sln"), """
                Microsoft Visual Studio Solution File, Format Version 12.00
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "First", "one\Api.csproj", "{C2BD5C34-1DCA-4F38-BAE0-0F5773075519}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Second", "two\API.csproj", "{D3BD5C34-1DCA-4F38-BAE0-0F5773075519}"
                EndProject
                Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Safe", "Safe.csproj", "{E3BD5C34-1DCA-4F38-BAE0-0F5773075519}"
                EndProject
                Global
                EndGlobal
                """);
        }

        HookOptionsPreparer preparer = new(120, "Hooks/state.json");

        HookOptions valid = preparer.Prepare(new RunHookArgs("hook", null, ["Safe"]), fixture.Invocation);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            preparer.Prepare(new RunHookArgs("hook", null, ["api"]), fixture.Invocation));

        Assert.Equal(safe, Assert.Single(valid.Projects).ProjectFilePath);
        Assert.Contains("ambiguous", exception.Message, StringComparison.Ordinal);
        Assert.Contains("api", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Un nom doit désigner un fichier C# exact, référencé et présent sur disque.</summary>
    /// <param name="name">Nom demandé qui ne désigne pas un projet C# utilisable.</param>
    /// <param name="diagnostic">Raison précise du refus.</param>
    [Theory]
    [InlineData("Ap", "not referenced")]
    [InlineData("Api.csproj", "not referenced")]
    [InlineData("NotTheFileName", "not referenced")]
    [InlineData("Unknown", "not referenced")]
    [InlineData("Unreferenced", "not referenced")]
    [InlineData("VisualBasic", "not referenced")]
    [InlineData("Missing", "does not exist")]
    public void RejectsProjectsThatAreNotExactReferencedExistingCSharpFiles(string name, string diagnostic)
    {
        using SolutionFixture fixture = new();
        CreateProject(fixture.Root, "Api.csproj");
        CreateProject(fixture.Root, "Unreferenced.csproj");
        CreateProject(fixture.Root, "VisualBasic.vbproj");
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"), """
            <Solution>
              <Project Path="Api.csproj" />
              <Project Path="Missing.csproj" />
              <Project Path="VisualBasic.vbproj" />
            </Solution>
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new HookOptionsPreparer(120, "Hooks/state.json")
                .Prepare(new RunHookArgs("hook", null, ["Api", name]), fixture.Invocation));

        Assert.Contains(name, exception.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Une sélection explicite doit contenir uniquement des noms exacts non vides.</summary>
    /// <param name="name">Élément invalide à refuser avant de rechercher les projets.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../Api")]
    [InlineData("src/Api")]
    [InlineData("src\\Api")]
    [InlineData("C:Api")]
    [InlineData("*")]
    [InlineData("Api?")]
    [InlineData("[Aa]pi")]
    public void RejectsEmptyNamesPathsAndWildcards(string? name)
    {
        using SolutionFixture fixture = new();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new HookOptionsPreparer(120, "Hooks/state.json")
                .Prepare(new RunHookArgs("hook", null, [name!]), fixture.Invocation));

        Assert.Contains("project name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Les deux formats sont réunis, avec noms de fichiers, espaces et répétitions normalisés.</summary>
    [Fact]
    public void ResolvesFileNamesAcrossAllSolutionsAtTheSelectedLevel()
    {
        using SolutionFixture fixture = new();
        string api = CreateProject(fixture.Root, "src/Api/Api.csproj");
        string worker = CreateProject(fixture.Root, "src/Worker/Worker.csproj");
        File.WriteAllText(Path.Combine(fixture.Root, "Example.slnx"), """
            <Solution>
              <Folder Name="/Services/">
                <Project Path="src/Api/Api.csproj" />
              </Folder>
            </Solution>
            """);
        File.WriteAllText(Path.Combine(fixture.Root, "Other.sln"), """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Display name", "src\Worker\Worker.csproj", "{C2BD5C34-1DCA-4F38-BAE0-0F5773075519}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Different display", "src\Api\Api.csproj", "{D3BD5C34-1DCA-4F38-BAE0-0F5773075519}"
            EndProject
            Global
            EndGlobal
            """);

        HookOptions options = new HookOptionsPreparer(120, "Hooks/state.json")
            .Prepare(new RunHookArgs("hook", null, [" api ", "WORKER", "API"]), fixture.Invocation);

        Assert.Equal(fixture.Root, options.SolutionRoot);
        Assert.Equal(fixture.Root, options.DefaultWorkingDirectory);
        Assert.Equal(
        [
            new TargetProject("Api", api, Path.GetDirectoryName(api)!),
            new TargetProject("Worker", worker, Path.GetDirectoryName(worker)!),
        ], options.Projects);
    }

    /// <summary>Crée un projet existant sans nécessiter de compilation ni de modèle réel.</summary>
    /// <param name="root">Dossier temporaire possédé par le test.</param>
    /// <param name="relativePath">Chemin du fichier projet.</param>
    /// <returns>Chemin absolu du projet créé.</returns>
    private static string CreateProject(string root, string relativePath)
    {
        string path = Path.GetFullPath(relativePath, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>NotTheFileName</AssemblyName></PropertyGroup></Project>");
        return path;
    }

    /// <summary>Crée un alias de fichier ou signale le privilège Windows manquant au moteur de tests.</summary>
    /// <param name="path">Chemin du lien, nettoyé avec la fixture propriétaire.</param>
    /// <param name="target">Fichier projet physique existant.</param>
    /// <exception cref="IOException">La création échoue pour une raison autre que le privilège Windows requis.</exception>
    private static void CreateFileAlias(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xFFFF) == 1314)
        {
            Assert.Skip("Creating file symbolic links requires Windows Developer Mode or the symbolic-link privilege.");
        }
    }
}
