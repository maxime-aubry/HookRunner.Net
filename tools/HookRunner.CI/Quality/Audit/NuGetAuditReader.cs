using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Valide le rapport NuGet et parcourt ses dépendances directes et transitives.</summary>
internal static class NuGetAuditReader
{
    /// <summary>Valide le rapport et énumère les vulnérabilités avec leur projet et leur package d'origine.</summary>
    /// <param name="report">Racine JSON du rapport d'audit NuGet.</param>
    /// <returns>Les vulnérabilités des packages directs et transitifs, sans filtrage de sévérité.</returns>
    /// <remarks>La lecture est différée ; le document JSON doit rester disponible pendant l'énumération.</remarks>
    /// <exception cref="InvalidDataException">La version est incompatible, aucun projet n'est présent ou la collecte a échoué.</exception>
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

    /// <summary>Refuse un rapport de version non prise en charge, sans projet ou signalant un problème de collecte.</summary>
    /// <param name="report">Rapport NuGet dont les champs de contrôle sont inspectés.</param>
    /// <exception cref="InvalidDataException">La version diffère de 1, projects est vide ou problems contient une entrée.</exception>
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

    /// <summary>Parcourt les packages directs et transitifs de chaque framework du projet.</summary>
    /// <param name="project">Entrée de projet NuGet pouvant omettre la propriété frameworks.</param>
    /// <returns>Les packages présents ; une séquence vide si aucun framework n'est déclaré.</returns>
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
