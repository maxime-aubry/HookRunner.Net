using System.Diagnostics;

namespace HookRunner.ExecutionTests;

/// <summary>Lien de dossier jetable, sans privilège de lien symbolique sous Windows.</summary>
internal sealed class DirectoryLink : IDisposable
{
    private readonly string path;

    /// <summary>Crée une jonction Windows ou un lien symbolique Unix vers un dossier existant.</summary>
    /// <param name="path">Chemin du lien temporaire.</param>
    /// <param name="target">Dossier physique conservé lors du nettoyage.</param>
    /// <exception cref="IOException">La création native du lien échoue.</exception>
    public DirectoryLink(string path, string target)
    {
        this.path = path;
        if (OperatingSystem.IsWindows())
        {
            ProcessStartInfo start = new("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("/c");
            start.ArgumentList.Add("mklink");
            start.ArgumentList.Add("/J");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(target);
            using Process process = Process.Start(start)!;
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new IOException(error);
            }
        }
        else
        {
            Directory.CreateSymbolicLink(path, target);
        }
    }

    /// <summary>Supprime uniquement le lien, en conservant la cible.</summary>
    public void Dispose() => Directory.Delete(path);
}
