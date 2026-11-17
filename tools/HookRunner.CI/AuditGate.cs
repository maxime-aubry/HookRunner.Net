using System.Text.Json;

namespace HookRunner.CI;

public static class AuditGate
{
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

        var blocking = new List<string>();
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
