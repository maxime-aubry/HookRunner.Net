using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Politique de blocage des vulnérabilités NuGet à partir d'un rapport versionné.</summary>
public static class AuditGate
{
    /// <summary>
    /// Parcourt un rapport NuGet et retient les vulnérabilités High ou Critical, directes et transitives.
    /// </summary>
    /// <param name="report">Racine du rapport JSON NuGet version 1 contenant au moins un projet.</param>
    /// <returns>
    /// Les diagnostics bloquants avec projet, package, sévérité et lien d'avis ; un tableau vide si aucun n'est
    /// bloquant.
    /// </returns>
    /// <remarks>
    /// Un projet sans propriété frameworks est accepté comme dépourvu de signalement. Les sévérités Low et
    /// Moderate ne bloquent pas.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    /// La version est incompatible, la liste de projets est vide ou NuGet signale un problème de collecte.
    /// </exception>
    public static string[] BlockingVulnerabilities(JsonElement report)
    {
        if (report.GetProperty("version").GetInt32() != 1 || report.GetProperty("projects").GetArrayLength() == 0)
        {
            throw new InvalidDataException("Unsupported or empty NuGet audit report.");
        }

        if (report.TryGetProperty("problems", out JsonElement problems) && problems.GetArrayLength() > 0)
        {
            throw new InvalidDataException("NuGet reported audit problems: " + problems);
        }

        List<string> blocking = [];
        foreach (JsonElement project in report.GetProperty("projects").EnumerateArray())
        {
            // NuGet omits frameworks entirely when the project has no findings.
            if (!project.TryGetProperty("frameworks", out JsonElement frameworks))
            {
                continue;
            }

            foreach (JsonElement framework in frameworks.EnumerateArray())
            {
                foreach (string group in new[] { "topLevelPackages", "transitivePackages" })
                {
                    if (!framework.TryGetProperty(group, out JsonElement packages))
                    {
                        continue;
                    }

                    foreach (JsonElement package in packages.EnumerateArray())
                    {
                        if (!package.TryGetProperty("vulnerabilities", out JsonElement vulnerabilities))
                        {
                            continue;
                        }

                        foreach (JsonElement vulnerability in vulnerabilities.EnumerateArray())
                        {
                            string? severity = vulnerability.GetProperty("severity").GetString();
                            if (string.Equals(severity, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase))
                            {
                                blocking.Add($"{project.GetProperty("path")}: {package.GetProperty("id")} {severity} {vulnerability.GetProperty("advisoryurl")}");
                            }
                        }
                    }
                }
            }
        }

        return [.. blocking];
    }
}
