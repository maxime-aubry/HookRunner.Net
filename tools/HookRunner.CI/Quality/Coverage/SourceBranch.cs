namespace HookRunner.CI;

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
