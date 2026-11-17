using System.Text.Json;

namespace HookRunner.CI;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        var reports = new List<JsonElement>();
        foreach (string path in reportPaths)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            reports.Add(document.RootElement.Clone());
        }

        var results = new List<CoverageResult>();
        foreach (string project in projects)
        {
            string[] inventory = File.ReadAllLines(Path.Combine(artifacts, "inventory", Path.GetFileNameWithoutExtension(project) + ".txt"));
            if (!File.Exists(inventory[2]))
            {
                throw new FileNotFoundException("Production assembly missing", inventory[2]);
            }

            results.Add(CoverageGate.Evaluate(inventory[1], CoverageGate.ReadSequencePoints(inventory[3]), reports));
        }

        string json = JsonSerializer.Serialize(results, JsonOptions);
        File.WriteAllText(Path.Combine(artifacts, "coverage", "gate.json"), json + Environment.NewLine);
        string[] summary = [.. results.Select(result => $"| {result.Assembly} | {result.Status} | {result.UncoveredLines.Length} | {result.UncoveredBranches.Length} |")];
        File.WriteAllLines(Path.Combine(artifacts, "coverage", "gate.md"), ["| Assembly | Status | Uncovered lines | Uncovered branches |", "|---|---|---|---|", .. summary]);
        Console.WriteLine(json);
        return results.Any(result => result.Status == "failed") ? 1 : 0;
    }

    private static int CheckAudit(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        string[] vulnerabilities = AuditGate.BlockingVulnerabilities(document.RootElement);
        foreach (string vulnerability in vulnerabilities)
        {
            Console.Error.WriteLine(vulnerability);
        }

        return vulnerabilities.Length == 0 ? 0 : 1;
    }

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
