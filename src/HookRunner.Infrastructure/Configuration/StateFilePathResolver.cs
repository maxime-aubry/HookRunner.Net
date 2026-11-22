namespace HookRunner.Infrastructure;

/// <summary>Valide le chemin d'état relatif et le résout à l'intérieur de la solution.</summary>
internal sealed class StateFilePathResolver
{
    private readonly string relativePath;

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

    public string Resolve(string solutionRoot)
    {
        string resolved = Path.GetFullPath(relativePath, solutionRoot);
        string relative = Path.GetRelativePath(solutionRoot, resolved);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("HookRunner:StateFilePath must stay within the solution root.");
        }

        return resolved;
    }
}
