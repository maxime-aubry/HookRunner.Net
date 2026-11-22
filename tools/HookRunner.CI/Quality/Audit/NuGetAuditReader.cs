using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Valide le rapport NuGet et parcourt ses dépendances directes et transitives.</summary>
internal static class NuGetAuditReader
{
    public static IEnumerable<(JsonElement Project, JsonElement Package, JsonElement Vulnerability)> Read(JsonElement report)
    {
        Validate(report);
        foreach (JsonElement project in report.GetProperty("projects").EnumerateArray())
        {
            foreach (JsonElement package in Packages(project))
            {
                if (package.TryGetProperty("vulnerabilities", out JsonElement vulnerabilities))
                {
                    foreach (JsonElement vulnerability in vulnerabilities.EnumerateArray())
                    {
                        yield return (project, package, vulnerability);
                    }
                }
            }
        }
    }

    private static void Validate(JsonElement report)
    {
        if (report.GetProperty("version").GetInt32() != 1 || report.GetProperty("projects").GetArrayLength() == 0)
        {
            throw new InvalidDataException("Unsupported or empty NuGet audit report.");
        }

        if (report.TryGetProperty("problems", out JsonElement problems) && problems.GetArrayLength() > 0)
        {
            throw new InvalidDataException("NuGet reported audit problems: " + problems);
        }
    }

    private static IEnumerable<JsonElement> Packages(JsonElement project)
    {
        // NuGet omits frameworks entirely when the project has no findings.
        if (!project.TryGetProperty("frameworks", out JsonElement frameworks))
        {
            yield break;
        }

        foreach (JsonElement framework in frameworks.EnumerateArray())
        {
            foreach (string group in new[] { "topLevelPackages", "transitivePackages" })
            {
                if (framework.TryGetProperty(group, out JsonElement packages))
                {
                    foreach (JsonElement package in packages.EnumerateArray())
                    {
                        yield return package;
                    }
                }
            }
        }
    }
}
