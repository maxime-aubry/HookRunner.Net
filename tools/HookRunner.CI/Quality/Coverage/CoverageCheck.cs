using System.Text.Json;

namespace HookRunner.CI;

internal static class CoverageCheck
{
    /// <summary>
    /// Charge inventaires et rapports, évalue chaque projet de production et écrit les synthèses JSON et Markdown.
    /// </summary>
    /// <param name="root">Chemin absolu de la racine du dépôt contenant src et artifacts.</param>
    /// <returns>1 si une assembly échoue au contrôle de couverture, sinon 0.</returns>
    /// <remarks>
    /// Les rapports sont lus sous artifacts/coverage et les inventaires sous artifacts/inventory. La synthèse JSON
    /// est aussi écrite sur stdout.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Aucun projet de production n'est découvert.</exception>
    /// <exception cref="FileNotFoundException">
    /// Aucun rapport Coverlet n'existe ou une assembly attendue est absente.
    /// </exception>
    public static int Run(string root)
    {
        string artifacts = Path.Combine(root, "artifacts");
        string[] projects = DiscoverProjects(root);
        List<JsonElement> reports = ReadReports(Path.Combine(artifacts, "coverage"));
        CoverageResult[] results = [.. projects.Select(project => EvaluateProject(project, artifacts, reports, root))];
        string json = CoverageReportWriter.Write(Path.Combine(artifacts, "coverage"), results);
        Console.WriteLine(json);
        return results.Any(result => result.Status == "failed") ? 1 : 0;
    }

    private static string[] DiscoverProjects(string root)
    {
        string[] projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        return projects.Length > 0 ? projects : throw new InvalidOperationException("No production projects discovered.");
    }

    private static List<JsonElement> ReadReports(string directory)
    {
        string[] paths = Directory.GetFiles(directory, "coverage.json", SearchOption.AllDirectories);
        if (paths.Length == 0)
        {
            throw new FileNotFoundException("No Coverlet JSON reports were produced.");
        }

        List<JsonElement> reports = [];
        foreach (string path in paths)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            reports.Add(document.RootElement.Clone());
        }

        return reports;
    }

    private static CoverageResult EvaluateProject(string project, string artifacts, List<JsonElement> reports, string root)
    {
        string path = Path.Combine(artifacts, "inventory", Path.GetFileNameWithoutExtension(project) + ".txt");
        ProductionInventory inventory = ProductionInventory.Read(path);
        return CoverageGate.Evaluate(inventory.Assembly, PdbSequencePointReader.Read(inventory.PdbPath), reports, root);
    }
}
