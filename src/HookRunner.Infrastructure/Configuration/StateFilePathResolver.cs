namespace HookRunner.Infrastructure;

/// <summary>Valide le chemin d'état relatif et le résout à l'intérieur de la solution.</summary>
internal sealed class StateFilePathResolver
{
    private readonly string relativePath;

    /// <summary>Conserve le chemin d'état configuré après vérification de son caractère relatif.</summary>
    /// <param name="relativePath">Chemin du fichier d'état relatif à la future racine de solution.</param>
    /// <exception cref="InvalidDataException">Le chemin est vide, composé d'espaces ou enraciné.</exception>
    public StateFilePathResolver(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("HookRunner:StateFilePath is required.");
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("HookRunner:StateFilePath must be relative to the solution root.");
        }

        this.relativePath = relativePath;
    }

    /// <summary>Résout le chemin d'état et refuse une destination hors de la racine physique de solution.</summary>
    /// <param name="solutionRoot">Racine absolue utilisée comme base de résolution.</param>
    /// <returns>Le chemin absolu normalisé du fichier d'état, sans créer de fichier ni de dossier.</returns>
    /// <exception cref="InvalidDataException">Le chemin normalisé sort de la racine de solution.</exception>
    public string Resolve(string solutionRoot)
    {
        string resolved = Path.GetFullPath(relativePath, solutionRoot);
        string relative = Path.GetRelativePath(solutionRoot, resolved);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("HookRunner:StateFilePath must stay within the solution root.");
        }

        string canonical = CanonicalPath.Resolve(resolved);
        string canonicalRoot = CanonicalPath.Resolve(solutionRoot);
        // La casse est déjà physique ; le séparateur distingue la racine d'un dossier frère au même préfixe.
        string rootPrefix = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (canonical != canonicalRoot && !canonical.StartsWith(rootPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("HookRunner:StateFilePath must stay within the solution root.");
        }

        if (Path.GetRelativePath(canonical, CanonicalPath.Resolve(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))) == ".")
        {
            throw new InvalidDataException("HookRunner:StateFilePath must be distinct from appsettings.json.");
        }

        return resolved;
    }
}
