using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Point d'entrée du programme de contrôle CI.</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Route la commande CI vers le contrôle de couverture, d'audit ou de liens de documentation.
    /// </summary>
    /// <param name="args">coverage ou docs suivi de la racine du dépôt, ou audit suivi du rapport JSON.</param>
    /// <returns>
    /// 0 si le contrôle réussit, 1 s'il échoue ou rencontre une erreur de données prise en charge.
    /// </returns>
    /// <remarks>
    /// Les erreurs attendues de lecture, format ou contrat sont écrites sur stderr. Les autres exceptions restent
    /// visibles à l'hôte.
    /// </remarks>
    private static int Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["coverage", string root] => CheckCoverage(Path.GetFullPath(root)),
                ["audit", string path] => CheckAudit(path),
                ["docs", string root] => CheckDocumentation(Path.GetFullPath(root)),
                _ => throw new ArgumentException("Usage: HookRunner.CI coverage|docs <repository> OR audit <report.json>"),
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

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
    private static int CheckCoverage(string root)
    {
        string artifacts = Path.Combine(root, "artifacts");
        string[] projects = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        if (projects.Length == 0)
        {
            throw new InvalidOperationException("No production projects discovered.");
        }

        string[] reportPaths = Directory.GetFiles(Path.Combine(artifacts, "coverage"), "coverage.json", SearchOption.AllDirectories);
        if (reportPaths.Length == 0)
        {
            throw new FileNotFoundException("No Coverlet JSON reports were produced.");
        }
        List<JsonElement> reports = [];
        foreach (string path in reportPaths)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            reports.Add(document.RootElement.Clone());
        }

        List<CoverageResult> results = [];
        foreach (string project in projects)
        {
            string[] inventory = File.ReadAllLines(Path.Combine(artifacts, "inventory", Path.GetFileNameWithoutExtension(project) + ".txt"));
            if (!File.Exists(inventory[2]))
            {
                throw new FileNotFoundException("Production assembly missing", inventory[2]);
            }

            results.Add(CoverageGate.Evaluate(inventory[1], CoverageGate.ReadSequencePoints(inventory[3]), reports, root));
        }

        string json = JsonSerializer.Serialize(results, JsonOptions);
        File.WriteAllText(Path.Combine(artifacts, "coverage", "gate.json"), json + Environment.NewLine);
        string[] summary = [.. results.Select(result => $"| {result.Assembly} | {result.Status} | {result.UncoveredLines.Length} | {result.UncoveredBranches.Length} |")];
        File.WriteAllLines(Path.Combine(artifacts, "coverage", "gate.md"), ["| Assembly | Status | Uncovered lines | Uncovered branches |", "|---|---|---|---|", .. summary]);
        Console.WriteLine(json);
        return results.Any(result => result.Status == "failed") ? 1 : 0;
    }

    /// <summary>Lit le rapport d'audit NuGet et écrit les vulnérabilités bloquantes sur stderr.</summary>
    /// <param name="path">Chemin du rapport JSON produit par NuGet.</param>
    /// <returns>0 en l'absence de vulnérabilité bloquante ; sinon 1.</returns>
    /// <remarks>Les erreurs de lecture ou de structure du rapport se propagent au routeur de commandes.</remarks>
    private static int CheckAudit(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string[] vulnerabilities = AuditGate.BlockingVulnerabilities(document.RootElement);
        foreach (string vulnerability in vulnerabilities)
        {
            Console.Error.WriteLine(vulnerability);
        }

        return vulnerabilities.Length == 0 ? 0 : 1;
    }

    /// <summary>
    /// Vérifie l'existence des cibles locales des liens Markdown de la racine et du dossier docs.
    /// </summary>
    /// <param name="root">Chemin absolu de la racine du dépôt.</param>
    /// <returns>
    /// 0 après vérification de tous les liens locaux et écriture du nombre de fichiers contrôlés sur stdout.
    /// </returns>
    /// <remarks>
    /// Les fragments d'ancre sont retirés ; les liens absolus et externes sont ignorés. Les cibles sont décodées
    /// puis résolues relativement au fichier Markdown. Ce contrôle ne valide pas les ancres ni le contenu.
    /// </remarks>
    /// <exception cref="FileNotFoundException">
    /// Une cible locale de lien n'existe ni comme fichier ni comme dossier.
    /// </exception>
    private static int CheckDocumentation(string root)
    {
        List<string> files = [.. Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)];
        files.AddRange(Directory.GetFiles(root, "*.md"));
        foreach (string file in files)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), @"\]\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                string target = match.Groups[1].Value.Split('#')[0].Trim('<', '>');
                if (target.Length == 0 || Uri.TryCreate(target, UriKind.Absolute, out _))
                {
                    continue;
                }

                string resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(target)));
                if (!File.Exists(resolved) && !Directory.Exists(resolved))
                {
                    throw new FileNotFoundException($"Broken documentation link in {file}: {target}");
                }
            }
        }

        Console.WriteLine($"Checked local file links in {files.Count} Markdown files (anchors and external links are not checked).");
        return 0;
    }
}
