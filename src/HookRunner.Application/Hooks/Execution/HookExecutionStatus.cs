namespace HookRunner.Application;

/// <summary>Statut d'exécution communiqué par l'exécuteur isolé à l'application.</summary>
public enum HookExecutionStatus
{
    /// <summary>Le hook s'est terminé normalement.</summary>
    Success,
    /// <summary>
    /// Statut réservé à un échec de validation métier, non produit par le point d'entrée enfant actuel.
    /// </summary>
    ValidationFailure,
    /// <summary>Une erreur technique a empêché l'exécution normale du hook.</summary>
    TechnicalFailure,
}
