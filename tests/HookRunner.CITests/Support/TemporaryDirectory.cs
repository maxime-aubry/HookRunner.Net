namespace HookRunner.CITests;

internal sealed class TemporaryDirectory : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("hookrunner-ci-tests-").FullName;

    /// <summary>Crée les dossiers parents et écrit un fichier dans le dossier temporaire du test.</summary>
    /// <param name="relativePath">Chemin du fichier relatif à la racine temporaire.</param>
    /// <param name="content">Contenu remplaçant l'éventuel fichier existant.</param>
    /// <returns>Le chemin du fichier écrit.</returns>
    public string Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Supprime récursivement le dossier temporaire et les fichiers créés par le test.</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
}
