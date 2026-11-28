using HookRunner.Application;
using Microsoft.VisualStudio.SolutionPersistence.Model;

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
        IReadOnlyList<string> references = SolutionProjectReader.Read(root);
        TargetProject[] selected = [.. names.Select(name => ResolveProject(root, references, name))];

        return [.. selected.DistinctBy(project => CanonicalPath.Resolve(project.ProjectFilePath), StringComparer.Ordinal)];
    }

    /// <summary>Résout un nom unique et vérifie le fichier avant de construire le projet cible.</summary>
    /// <param name="root">Racine de la solution.</param>
    /// <param name="references">Chemins absolus des projets C# référencés.</param>
    /// <param name="name">Nom demandé validé.</param>
    /// <returns>Le projet cible avec son chemin logique et son nom de fichier.</returns>
    /// <exception cref="InvalidDataException">Le nom est inconnu ou ambigu, ou le fichier est absent ou hors racine.</exception>
    private static TargetProject ResolveProject(string root, IReadOnlyList<string> references, string name)
    {
        string path = FindUniqueProjectPath(references, name);
        ValidateProjectFile(root, path, name);
        return new TargetProject(Path.GetFileNameWithoutExtension(path), path, Path.GetDirectoryName(path)!);
    }

    /// <summary>Exige une seule correspondance physique pour le nom demandé.</summary>
    /// <param name="references">Chemins absolus des projets C# référencés.</param>
    /// <param name="name">Nom demandé validé, comparé sans distinction de casse.</param>
    /// <returns>Le premier chemin logique correspondant au fichier physique unique.</returns>
    /// <exception cref="InvalidDataException">Le nom est inconnu ou désigne plusieurs fichiers physiques.</exception>
    private static string FindUniqueProjectPath(IReadOnlyList<string> references, string name)
    {
        string[] matches = [.. references
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(CanonicalPath.Resolve, StringComparer.Ordinal)];
        if (matches.Length == 0)
        {
            throw new InvalidDataException($"Project '{name}' is not referenced as a C# project in the selected solutions.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidDataException($"Project name '{name}' is ambiguous: {string.Join(", ", matches)}.");
        }

        return matches[0];
    }

    /// <summary>Exige un fichier existant dans le périmètre physique, même en présence de liens.</summary>
    /// <param name="root">Racine de la solution.</param>
    /// <param name="path">Fichier projet demandé.</param>
    /// <param name="name">Nom demandé utilisé dans le diagnostic.</param>
    /// <exception cref="InvalidDataException">Le fichier projet est absent ou sort de la racine.</exception>
    private static void ValidateProjectFile(string root, string path, string name)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Project '{name}' does not exist: '{path}'.");
        }

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
}
