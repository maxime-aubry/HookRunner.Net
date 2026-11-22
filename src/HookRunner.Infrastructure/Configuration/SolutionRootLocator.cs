namespace HookRunner.Infrastructure;

/// <summary>Recherche la première solution dans le dossier de l'agent puis ses parents.</summary>
internal static class SolutionRootLocator
{
    public static string Find(string workingDirectory)
    {
        DirectoryInfo? directory = new(workingDirectory);
        while (directory is not null)
        {
            if (directory.EnumerateFiles().Any(IsSolution))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No solution found above the agent working directory.");
    }

    private static bool IsSolution(FileInfo file)
        => file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
}
