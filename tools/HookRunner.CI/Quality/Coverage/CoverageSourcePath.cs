namespace HookRunner.CI;

internal static class CoverageSourcePath
{
    public static string Resolve(string file, string? repositoryRoot)
    {
        string normalized = file.Replace('\\', '/');
        return repositoryRoot is not null && normalized.StartsWith("/_/", StringComparison.Ordinal)
            ? repositoryRoot.Replace('\\', '/').TrimEnd('/') + "/" + normalized[3..]
            : normalized;
    }
}
