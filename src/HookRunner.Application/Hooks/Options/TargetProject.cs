namespace HookRunner.Application;

/// <summary>Description d'un projet cible résolu avec son nom et ses chemins absolus.</summary>
/// <param name="Name">Nom du projet cible, issu du nom de son fichier .csproj.</param>
/// <param name="ProjectFilePath">Chemin absolu du fichier .csproj cible.</param>
/// <param name="DirectoryPath">Chemin absolu du dossier contenant le projet cible.</param>
public sealed record TargetProject(string Name, string ProjectFilePath, string DirectoryPath);
