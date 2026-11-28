using Microsoft.Build.Construction;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace HookRunner.Infrastructure;

/// <summary>Réunit les références C# des solutions du dossier retenu sans évaluer les projets.</summary>
internal static class SolutionProjectReader
{
    /// <summary>Lit toutes les solutions dans un ordre stable et élimine les chemins identiques.</summary>
    /// <param name="root">Dossier des solutions ; aucun parent ni sous-dossier n'est exploré.</param>
    /// <returns>Les chemins absolus normalisés, dans l'ordre de leur première référence.</returns>
    /// <exception cref="Microsoft.Build.Exceptions.InvalidProjectFileException">Une solution .sln est illisible ou mal formée.</exception>
    /// <exception cref="SolutionException">Une solution .slnx ne respecte pas le format attendu.</exception>
    /// <exception cref="IOException">L'énumération du dossier ou la lecture d'une solution échoue.</exception>
    /// <exception cref="UnauthorizedAccessException">L'accès au dossier ou à une solution est refusé.</exception>
    /// <remarks>Toutes les solutions sont lues avant le retour ; les erreurs remontent sans interception.</remarks>
    public static IReadOnlyList<string> Read(string root)
    {
        return [.. ReadProjectPaths(root).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Lit les formats de solution avec le parseur Microsoft sans charger ni évaluer les projets.</summary>
    /// <param name="root">Dossier des solutions ; aucun parent ni sous-dossier n'est exploré.</param>
    /// <returns>Les chemins absolus normalisés des projets C# référencés.</returns>
    /// <exception cref="Microsoft.Build.Exceptions.InvalidProjectFileException">Une solution .sln est illisible ou mal formée.</exception>
    /// <exception cref="SolutionException">Une solution .slnx ne respecte pas le format attendu.</exception>
    /// <exception cref="IOException">L'énumération du dossier ou la lecture d'une solution échoue.</exception>
    /// <exception cref="UnauthorizedAccessException">L'accès au dossier ou à une solution est refusé.</exception>
    /// <remarks>La lecture est différée à l'énumération ; ses erreurs remontent sans interception.</remarks>
    private static IEnumerable<string> ReadProjectPaths(string root)
    {
        foreach (FileInfo solution in new DirectoryInfo(root).EnumerateFiles().Where(SolutionRootLocator.IsSolution).OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            foreach (string projectPath in ReadSolutionProjectPaths(solution))
            {
                string path = projectPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                if (Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.GetFullPath(path, root);
                }
            }
        }
    }

    /// <summary>Lit les références sans imposer aux solutions classiques l'unicité des noms de fichiers projets.</summary>
    /// <param name="solution">Solution du dossier retenu.</param>
    /// <returns>Chemins des projets tels qu'ils sont référencés dans la solution.</returns>
    /// <exception cref="Microsoft.Build.Exceptions.InvalidProjectFileException">La solution .sln est illisible ou mal formée.</exception>
    /// <exception cref="SolutionException">La solution .slnx ne respecte pas le format attendu.</exception>
    /// <exception cref="IOException">La lecture du fichier de solution échoue.</exception>
    /// <exception cref="UnauthorizedAccessException">L'accès au fichier de solution est refusé.</exception>
    /// <remarks>Les erreurs de lecture et d'analyse du parseur choisi remontent sans interception.</remarks>
    private static IEnumerable<string> ReadSolutionProjectPaths(FileInfo solution)
    {
        if (solution.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase))
        {
            // Le modèle SolutionPersistence exige des noms de fichiers uniques par dossier logique,
            // alors qu'une solution classique peut contenir une ambiguïté non demandée.
            return SolutionFile.Parse(solution.FullName).ProjectsInOrder
                .Where(project => project.ProjectType != SolutionProjectType.SolutionFolder)
                .Select(project => project.RelativePath);
        }

        ISolutionSerializer serializer = SolutionSerializers.GetSerializerByMoniker(solution.FullName)!;
        // Le port Prepare est synchrone ; la lecture reste confinée à l'adaptateur de fichiers.
        SolutionModel model = serializer.OpenAsync(solution.FullName, CancellationToken.None).GetAwaiter().GetResult();
        return model.SolutionProjects.Select(project => project.FilePath);
    }
}
