using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Contrôle de couverture complète fondé sur les rapports Coverlet et l'inventaire des PDB.</summary>
public static class CoverageGate
{
    /// <summary>
    /// Lit les points de séquence visibles d'un PDB portable pour inventorier les lignes réellement exécutables.
    /// </summary>
    /// <param name="pdbPath">Chemin du PDB portable produit avec l'assembly à contrôler.</param>
    /// <returns>
    /// Les numéros de lignes distincts par fichier, avec chemins normalisés sur des barres obliques.
    /// </returns>
    /// <remarks>
    /// Les points masqués, sans document et les sources générées sous obj sont exclus. Le flux et le lecteur de
    /// métadonnées sont libérés après lecture.
    /// </remarks>
    /// <exception cref="BadImageFormatException">
    /// Le fichier ne contient pas les métadonnées d'un PDB portable exploitable.
    /// </exception>
    /// <exception cref="IOException">Le PDB ne peut pas être ouvert ou lu.</exception>
    public static Dictionary<string, HashSet<string>> ReadSequencePoints(string pdbPath)
        => PdbSequencePointReader.Read(pdbPath);

    /// <summary>
    /// Fusionne les rapports de couverture d'une assembly et les confronte à l'inventaire indépendant du PDB.
    /// </summary>
    /// <param name="assembly">Nom de l'assembly, sans extension, comparé de manière ordinale.</param>
    /// <param name="expected">Lignes exécutables attendues par fichier d'après le PDB.</param>
    /// <param name="reports">
    /// Rapports Coverlet JSON de toutes les suites ; ils sont lus sans être modifiés.
    /// </param>
    /// <param name="repositoryRoot">Racine locale du dépôt pour résoudre les chemins PDB déterministes /_/.</param>
    /// <returns>
    /// Un résultat not-applicable pour un inventaire vide, passed si toutes les lignes et branches sont couvertes,
    /// sinon failed avec les points manquants.
    /// </returns>
    /// <remarks>
    /// Un point est couvert si au moins une suite l'a exécuté. Une assembly ou un document absent du collecteur
    /// échoue dès qu'il contient des lignes attendues. Les documents et branches des rapports participent aussi au
    /// contrôle.
    /// </remarks>
    public static CoverageResult Evaluate(string assembly, Dictionary<string, HashSet<string>> expected, IEnumerable<JsonElement> reports, string? repositoryRoot = null)
    {
        if (expected.Count == 0)
        {
            return new(assembly, "not-applicable", [], []);
        }

        CoverageSnapshot coverage = CoverletCoverageReader.Read(assembly, reports, repositoryRoot);
        IncludeExpectedLines(coverage, expected, repositoryRoot);
        string[] uncoveredLines = [.. coverage.Lines.Where(pair => !pair.Value).Select(pair => pair.Key.ToString()).Order()];
        string[] uncoveredBranches = [.. coverage.Branches.Where(pair => !pair.Value).Select(pair => pair.Key.ToString()).Order()];
        string status = coverage.Found && uncoveredLines.Length == 0 && uncoveredBranches.Length == 0 ? "passed" : "failed";
        return new(assembly, status, uncoveredLines, uncoveredBranches);
    }

    /// <summary>Ajoute comme non couvertes les lignes du PDB absentes des observations du collecteur.</summary>
    /// <param name="coverage">Observations à compléter sans remplacer les lignes déjà couvertes.</param>
    /// <param name="expected">Inventaire indépendant des lignes exécutables par fichier.</param>
    /// <param name="repositoryRoot">Racine locale pour les chemins /_/ ; null conserve ces chemins.</param>
    private static void IncludeExpectedLines(CoverageSnapshot coverage, Dictionary<string, HashSet<string>> expected, string? repositoryRoot)
    {
        // The independent PDB inventory detects documents omitted by the collector.
        foreach ((string file, HashSet<string> fileLines) in expected)
        {
            string resolved = CoverageSourcePath.Resolve(file, repositoryRoot);
            foreach (string line in fileLines)
            {
                coverage.Lines.TryAdd(new SourceLine(resolved, line), false);
            }
        }
    }
}
