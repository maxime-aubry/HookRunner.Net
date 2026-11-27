namespace HookRunner.Infrastructure;

/// <summary>Recherche la première solution dans le dossier de l'agent puis ses parents.</summary>
internal static class SolutionRootLocator
{
    /// <summary>Cherche un fichier de solution dans le dossier de départ, puis dans chaque parent.</summary>
    /// <param name="workingDirectory">Dossier de l'agent depuis lequel commencer la recherche.</param>
    /// <returns>Le chemin absolu du premier dossier contenant une solution .sln ou .slnx.</returns>
    /// <exception cref="DirectoryNotFoundException">Le dossier est introuvable ou aucune solution n'est découverte.</exception>
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

    /// <summary>Reconnaît les deux extensions de solution sans distinction de casse.</summary>
    /// <param name="file">Fichier du dossier en cours d'exploration.</param>
    /// <returns>True pour une extension .sln ou .slnx ; sinon false.</returns>
    internal static bool IsSolution(FileInfo file)
        => file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
}
