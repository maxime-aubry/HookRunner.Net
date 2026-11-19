using HookRunner.Application;

namespace HookRunner.ExecutionTests;

/// <summary>Solution temporaire jetable avec un dossier agent imbriqué et une invocation de référence.</summary>
internal sealed class SolutionFixture : IDisposable
{
    /// <summary>
    /// Crée une solution temporaire et un dossier agent imbriqué pour tester la recherche ascendante.
    /// </summary>
    /// <remarks>Le dossier et ses fichiers sont supprimés lors de Dispose.</remarks>
    public SolutionFixture()
    {
        Root = Directory.CreateTempSubdirectory("hookrunner-tests-").FullName;
        Directory.CreateDirectory(Path.Combine(Root, "agent", "nested"));
        File.WriteAllText(Path.Combine(Root, "Example.slnx"), "<Solution />");
    }

    /// <summary>Chemin absolu de la solution temporaire possédée par la fixture.</summary>
    public string Root { get; }

    /// <summary>Données neutres de l'événement déclencheur, avec le JSON original.</summary>
    public InvocationData Invocation => new("codex", "Stop", "session-42", null,
        Path.Combine(Root, "agent", "nested"), "{\"opaque\":true}");

    /// <summary>
    /// Supprime récursivement le dossier temporaire créé par cette fixture et tous ses fichiers.
    /// </summary>
    public void Dispose() => Directory.Delete(Root, true);
}
