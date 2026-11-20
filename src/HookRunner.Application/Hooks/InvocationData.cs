namespace HookRunner.Application;

/// <summary>
/// Données neutres de l'événement déclencheur, indépendantes des modèles de protocole des agents.
/// </summary>
/// <param name="Agent">Identifiant de l'agent déclencheur, codex dans l'adaptateur actuel.</param>
/// <param name="EventName">Nom de l'événement natif ayant déclenché l'invocation.</param>
/// <param name="SessionId">Identifiant de session fourni par l'agent, sans valeur de secours.</param>
/// <param name="Project">Information de projet facultative fournie par l'agent ; null pour Codex.</param>
/// <param name="WorkingDirectory">Dossier d'origine de l'agent, avant recherche de la racine de solution.</param>
/// <param name="OriginalJson">
/// Texte JSON original conservé sans modification, champs supplémentaires inclus.
/// </param>
public sealed record InvocationData(
    string Agent,
    string EventName,
    string SessionId,
    string? Project,
    string WorkingDirectory,
    string OriginalJson);
