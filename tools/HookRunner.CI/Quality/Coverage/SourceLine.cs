namespace HookRunner.CI;

/// <summary>Identité d'une ligne source dans les observations de couverture.</summary>
/// <param name="File">Chemin source normalisé.</param>
/// <param name="Line">Numéro de ligne tel qu'il apparaît dans l'inventaire ou le rapport.</param>
internal readonly record struct SourceLine(string File, string Line)
{
    /// <summary>Formate l'identifiant d'une ligne pour les diagnostics de couverture.</summary>
    /// <returns>Le chemin source suivi du numéro de ligne, séparés par deux-points.</returns>
    public override string ToString() => $"{File}:{Line}";
}
