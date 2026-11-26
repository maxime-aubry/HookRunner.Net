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

    /// <summary>Recense récursivement les projets de production sous src.</summary>
    /// <param name="root">Racine du dépôt contenant le dossier src.</param>
    /// <returns>Les chemins des fichiers .csproj découverts.</returns>
    /// <exception cref="InvalidOperationException">Aucun projet de production n'est découvert.</exception>
    private static string[] DiscoverProjects(string root)
    {
        string[] projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        return projects.Length > 0 ? projects : throw new InvalidOperationException("No production projects discovered.");
    }

    /// <summary>Charge tous les rapports coverage.json et détache leurs racines des documents temporaires.</summary>
    /// <param name="directory">Dossier d'artefacts à parcourir récursivement.</param>
    /// <returns>Les racines JSON utilisables après libération des documents de lecture.</returns>
    /// <exception cref="FileNotFoundException">Aucun rapport coverage.json n'est découvert.</exception>
    /// <exception cref="JsonException">Un rapport contient un document JSON invalide.</exception>
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

    /// <summary>Confronte l'inventaire PDB d'un projet à l'ensemble des observations Coverlet.</summary>
    /// <param name="project">Chemin du .csproj déterminant le nom du fichier d'inventaire.</param>
    /// <param name="artifacts">Dossier contenant les inventaires de compilation.</param>
    /// <param name="reports">Rapports Coverlet des suites de tests.</param>
    /// <param name="root">Racine du dépôt utilisée pour résoudre les chemins déterministes.</param>
    /// <returns>Le verdict de couverture du projet et ses points non couverts.</returns>
    private static CoverageResult EvaluateProject(string project, string artifacts, List<JsonElement> reports, string root)
    {
        string path = Path.Combine(artifacts, "inventory", Path.GetFileNameWithoutExtension(project) + ".txt");
        ProductionInventory inventory = ProductionInventory.Read(path);
        return CoverageGate.Evaluate(inventory.Assembly, PdbSequencePointReader.Read(inventory.PdbPath), reports, root);
    }
}
