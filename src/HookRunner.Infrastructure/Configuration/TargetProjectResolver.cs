using HookRunner.Application;
using Microsoft.Build.Construction;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace HookRunner.Infrastructure;

/// <summary>Résout les noms de fichiers projets dans les seules solutions de la racine retenue.</summary>
internal static class TargetProjectResolver
{
    /// <summary>Réunit les références et conserve l'ordre des noms demandés sans répétition.</summary>
    /// <param name="root">Premier dossier contenant des solutions.</param>
    /// <param name="requestedNames">Noms sans extension demandés par l'appelant.</param>
    /// <returns>Les projets sélectionnés avec leurs chemins absolus.</returns>
    /// <exception cref="InvalidDataException">
    /// Un nom est invalide, inconnu ou ambigu, ou un projet est absent ou hors racine.
    /// </exception>
    /// <exception cref="Microsoft.Build.Exceptions.InvalidProjectFileException">Une solution .sln est illisible ou mal formée.</exception>
    /// <exception cref="SolutionException">Une solution .slnx ne respecte pas le format attendu.</exception>
    /// <exception cref="IOException">L'énumération du dossier ou la lecture d'une solution échoue.</exception>
    /// <exception cref="UnauthorizedAccessException">L'accès au dossier ou à une solution est refusé.</exception>
    /// <remarks>Les erreurs des lecteurs de solutions remontent sans interception, avant toute exécution du hook.</remarks>
    public static IReadOnlyList<TargetProject> Resolve(string root, IReadOnlyList<string> requestedNames)
    {
        if (requestedNames.Count == 0)
        {
            return [];
        }

        string[] names = [.. requestedNames.Select(ValidateName).Distinct(StringComparer.OrdinalIgnoreCase)];
        string[] references = [.. ReadProjectPaths(root).Distinct(StringComparer.Ordinal)];
        List<TargetProject> selected = [];
        foreach (string name in names)
        {
            string[] matches = [.. references.Where(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase)).DistinctBy(CanonicalPath.Resolve, StringComparer.Ordinal)];
            if (matches.Length == 0)
            {
                throw new InvalidDataException($"Project '{name}' is not referenced as a C# project in the selected solutions.");
            }

            if (matches.Length > 1)
            {
                throw new InvalidDataException($"Project name '{name}' is ambiguous: {string.Join(", ", matches)}.");
            }

            string path = matches[0];
            if (!File.Exists(path))
            {
                throw new InvalidDataException($"Project '{name}' does not exist: '{path}'.");
            }

            ValidateLocation(root, path, name);
            selected.Add(new TargetProject(Path.GetFileNameWithoutExtension(path), path, Path.GetDirectoryName(path)!));
        }

        return [.. selected.DistinctBy(project => CanonicalPath.Resolve(project.ProjectFilePath), StringComparer.Ordinal)];
    }

    /// <summary>Vérifie le périmètre physique, notamment en présence de liens de dossier.</summary>
    /// <param name="root">Racine de la solution.</param>
    /// <param name="path">Fichier projet existant demandé.</param>
    /// <param name="name">Nom demandé utilisé dans le diagnostic.</param>
    /// <exception cref="InvalidDataException">Le fichier projet sort de la racine.</exception>
    private static void ValidateLocation(string root, string path, string name)
    {
        string canonicalRoot = CanonicalPath.Resolve(root);
        string canonicalPath = CanonicalPath.Resolve(path);
        if (!CanonicalPath.IsWithinRoot(canonicalRoot, canonicalPath))
        {
            throw new InvalidDataException($"Project '{name}' is outside the solution root: '{path}'.");
        }
    }

    /// <summary>Refuse les sélections vides, les chemins et les motifs de recherche.</summary>
    /// <param name="name">Nom brut demandé.</param>
    /// <returns>Nom exact débarrassé de ses espaces extérieurs.</returns>
    /// <exception cref="InvalidDataException">Le nom n'est pas un nom de projet sélectionnable.</exception>
    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':', '*', '?', '[', ']']) >= 0 || name.Trim() is "." or "..")
        {
            throw new InvalidDataException($"Invalid project name: '{name}'. Specify an exact file name without extension, path or wildcard.");
        }

        return name.Trim();
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
