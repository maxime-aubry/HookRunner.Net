using System.Diagnostics;

namespace HookRunner.ExecutionTests;

/// <summary>Expose un dossier temporaire sous une lettre de lecteur Windows sans créer de volume.</summary>
internal sealed class MappedDrive : IDisposable
{
    /// <summary>Associe une lettre libre au dossier existant pour tester des racines Windows distinctes.</summary>
    /// <param name="directory">Dossier possédé par la fixture, dont le contenu reste inchangé.</param>
    /// <exception cref="IOException">L'association native échoue.</exception>
    public MappedDrive(string directory)
    {
        Root = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(letter => $"{(char)letter}:\\")
            .Except(Directory.GetLogicalDrives(), StringComparer.OrdinalIgnoreCase)
            .Last();
        Run(Root[..2], directory);
    }

    /// <summary>Racine du lecteur temporaire, terminée par un séparateur.</summary>
    public string Root { get; }

    /// <summary>Retire uniquement l'association de lecteur, sans supprimer son dossier cible.</summary>
    public void Dispose() => Run(Root[..2], "/D");

    /// <summary>Exécute une association ou sa libération et propage les erreurs natives.</summary>
    /// <param name="drive">Lettre du lecteur suivie de deux-points.</param>
    /// <param name="target">Dossier cible ou option /D pour retirer l'association.</param>
    /// <exception cref="IOException">La commande subst échoue, avec son diagnostic natif.</exception>
    private static void Run(string drive, string target)
    {
        ProcessStartInfo start = new("subst.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(drive);
        start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"subst failed: {output}{error}");
        }
    }
}
