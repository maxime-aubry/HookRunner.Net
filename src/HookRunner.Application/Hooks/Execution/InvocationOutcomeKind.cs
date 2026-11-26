namespace HookRunner.Application;

/// <summary>Verdict d'une invocation après supervision et éventuelle sauvegarde du compteur.</summary>
internal enum InvocationOutcomeKind
{
    /// <summary>Le hook a réussi et son compteur a été remis à zéro.</summary>
    Success,
    /// <summary>Le hook a demandé une correction dans la limite configurée.</summary>
    CorrectionRequested,
    /// <summary>Le hook a demandé une correction alors que le plafond était atteint.</summary>
    CorrectionLimitReached,
    /// <summary>Le hook isolé a retourné un échec technique.</summary>
    TechnicalFailure,
    /// <summary>L'échéance a été atteinte sans exécution isolée encore active.</summary>
    ConfirmedTimeout,
}
