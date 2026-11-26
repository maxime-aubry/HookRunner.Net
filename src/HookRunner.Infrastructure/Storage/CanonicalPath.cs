namespace HookRunner.Infrastructure;

/// <summary>Normalise les chemins physiques sans confondre les dossiers de worktrees distincts.</summary>
internal static class CanonicalPath
{
    private static readonly EnumerationOptions PhysicalNameOptions = new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        AttributesToSkip = 0,
    };

    /// <summary>Résout chaque composant symbolique existant et normalise les séparateurs.</summary>
    /// <param name="path">Chemin logique d'une racine ou d'un fichier d'état.</param>
    /// <returns>Chemin absolu physique, même si les derniers composants n'existent pas encore.</returns>
    /// <remarks>Les erreurs d'inspection du système de fichiers restent des erreurs techniques.</remarks>
    public static string Resolve(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!.ToUpperInvariant();
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (entry.Exists)
            {
                // Une correspondance exacte distingue deux dossiers de casse différente sur un volume sensible.
                current = Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(current)!, segment, PhysicalNameOptions)
                    .OrderByDescending(candidate => Path.GetFileName(candidate).Equals(segment, StringComparison.Ordinal))
                    .First(candidate => Path.GetFileName(candidate).Equals(segment, StringComparison.OrdinalIgnoreCase));
                entry = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (entry.LinkTarget is not null)
                {
                    current = Resolve(entry.ResolveLinkTarget(returnFinalTarget: true)!.FullName);
                }
            }
        }

        return Path.TrimEndingDirectorySeparator(current);
    }
}
