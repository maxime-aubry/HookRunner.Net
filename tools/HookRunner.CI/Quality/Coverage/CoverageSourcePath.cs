namespace HookRunner.CI;

internal static class CoverageSourcePath
{
    /// <summary>Normalise les séparateurs et remplace le préfixe déterministe /_/ par la racine locale disponible.</summary>
    /// <param name="file">Chemin source issu du PDB ou d'un rapport Coverlet.</param>
    /// <param name="repositoryRoot">Racine du dépôt ; null désactive le remplacement du préfixe.</param>
    /// <returns>Le chemin normalisé avec des barres obliques, éventuellement rattaché au dépôt local.</returns>
    public static string Resolve(string file, string? repositoryRoot)
    {
        string normalized = file.Replace('\\', '/');
        return repositoryRoot is not null && normalized.StartsWith("/_/", StringComparison.Ordinal)
            ? repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/" + normalized[3..]
            : normalized;
    }
}
