using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Produit les synthèses JSON et Markdown à partir des verdicts de couverture.</summary>
internal static class CoverageReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Écrit les verdicts dans gate.json et gate.md en remplaçant les synthèses précédentes.</summary>
    /// <param name="directory">Dossier de couverture existant recevant les deux fichiers.</param>
    /// <param name="results">Verdicts à sérialiser dans leur ordre de présentation.</param>
    /// <returns>La synthèse JSON indentée, sans le saut de ligne final ajouté au fichier.</returns>
    public static string Write(string directory, CoverageResult[] results)
    {
        string json = JsonSerializer.Serialize(results, JsonOptions);
        File.WriteAllText(Path.Combine(directory, "gate.json"), json + Environment.NewLine);
        string[] summary = [.. results.Select(result => $"| {result.Assembly} | {result.Status} | {result.UncoveredLines.Length} | {result.UncoveredBranches.Length} |")];
        File.WriteAllLines(Path.Combine(directory, "gate.md"), ["| Assembly | Status | Uncovered lines | Uncovered branches |", "|---|---|---|---|", .. summary]);
        return json;
    }
}
