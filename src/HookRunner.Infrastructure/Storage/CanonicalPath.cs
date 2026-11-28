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
    /// <remarks>
    /// Le suffixe absent est conservé sans création d'entrée. L'inspection d'existence suit les règles de .NET ;
    /// les erreurs qu'elle masque ne sont pas distinguées d'une absence.
    /// Un lien cassé est résolu si .NET fournit sa cible, même absente. Les boucles signalées par .NET
    /// provoquent une erreur technique ; ce module n'ajoute pas de détection de cycle.
    /// Les erreurs remontées par l'énumération ou la résolution des liens sont propagées sans récupération.
    /// </remarks>
    public static string Resolve(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)!.ToUpperInvariant();
        string current = root;
        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = ResolveSegment(current, segment);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>Compare des chemins physiques déjà normalisés sans confondre les dossiers frères.</summary>
    /// <param name="canonicalRoot">Racine physique obtenue par Resolve.</param>
    /// <param name="canonicalPath">Chemin physique à situer, obtenu par Resolve.</param>
    /// <returns>True pour la racine elle-même ou l'un de ses descendants.</returns>
    public static bool IsWithinRoot(string canonicalRoot, string canonicalPath)
    {
        string rootPrefix = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return canonicalPath == canonicalRoot || canonicalPath.StartsWith(rootPrefix, StringComparison.Ordinal);
    }

    /// <summary>Résout un segment existant ou conserve son nom lorsque l'entrée est absente.</summary>
    /// <param name="parent">Chemin physique du parent déjà parcouru.</param>
    /// <param name="segment">Nom du prochain fichier ou dossier.</param>
    /// <returns>Chemin du segment, avec sa casse physique et sa cible finale lorsqu'elles existent.</returns>
    /// <remarks>Les cibles symboliques sont à nouveau parcourues pour retrouver leur casse physique.</remarks>
    private static string ResolveSegment(string parent, string segment)
    {
        FileSystemInfo? entry = FindExistingEntry(parent, segment);
        if (entry is null)
        {
            return Path.Combine(parent, segment);
        }

        if (entry.LinkTarget is null)
        {
            return entry.FullName;
        }

        // L'entrée est identifiée comme lien ; une mutation concurrente n'est pas récupérée.
        FileSystemInfo target = entry.ResolveLinkTarget(returnFinalTarget: true)!;
        return Resolve(target.FullName);
    }

    /// <summary>Retrouve le nom physique et les métadonnées d'une entrée reconnue comme existante.</summary>
    /// <param name="parent">Dossier dans lequel chercher l'entrée.</param>
    /// <param name="segment">Nom demandé, dont la casse peut être un alias accepté par le volume.</param>
    /// <returns>L'entrée avec sa casse physique, ou null si l'inspection ne confirme pas son existence.</returns>
    /// <remarks>Une correspondance exacte distingue deux noms de casse différente sur un volume sensible.</remarks>
    private static FileSystemInfo? FindExistingEntry(string parent, string segment)
    {
        string path = Path.Combine(parent, segment);
        FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!entry.Exists)
        {
            return null;
        }

        return new DirectoryInfo(parent).EnumerateFileSystemInfos(segment, PhysicalNameOptions)
            .OrderByDescending(candidate => candidate.Name.Equals(segment, StringComparison.Ordinal))
            .First(candidate => candidate.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
    }
}
