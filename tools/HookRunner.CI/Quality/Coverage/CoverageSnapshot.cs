namespace HookRunner.CI;

internal sealed class CoverageSnapshot
{
    public bool Found { get; set; }
    public Dictionary<SourceLine, bool> Lines { get; } = [];
    public Dictionary<SourceBranch, bool> Branches { get; } = [];
}

/// <summary>Identité d'une ligne source dans les observations de couverture.</summary>
/// <param name="File">Chemin source normalisé.</param>
/// <param name="Line">Numéro de ligne tel qu'il apparaît dans l'inventaire ou le rapport.</param>
internal readonly record struct SourceLine(string File, string Line)
{
    /// <summary>Formate l'identifiant d'une ligne pour les diagnostics de couverture.</summary>
    /// <returns>Le chemin source suivi du numéro de ligne, séparés par deux-points.</returns>
    public override string ToString() => $"{File}:{Line}";
}

/// <summary>Identité d'une branche Coverlet distinguée par sa méthode, son offset et son chemin.</summary>
/// <param name="File">Chemin source normalisé.</param>
/// <param name="Line">Numéro de ligne associé à la branche.</param>
/// <param name="Method">Nom de la méthode contenant la branche.</param>
/// <param name="Offset">Offset de l'instruction de branchement.</param>
/// <param name="Path">Identifiant du chemin de branchement.</param>
internal readonly record struct SourceBranch(string File, string Line, string Method, string Offset, string Path)
{
    /// <summary>Formate l'identifiant complet d'une branche pour les diagnostics de couverture.</summary>
    /// <returns>Le fichier, la ligne, la méthode, l'offset et le chemin de branche observés par Coverlet.</returns>
    public override string ToString() => $"{File}:{Line}: {Method} offset {Offset} path {Path}";
}
