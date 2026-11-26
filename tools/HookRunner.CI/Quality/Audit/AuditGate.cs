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
        List<string> blocking = [];
        foreach ((JsonElement project, JsonElement package, JsonElement vulnerability) in NuGetAuditReader.Read(report))
        {
            string? severity = vulnerability.GetProperty("severity").GetString();
            if (IsBlockingSeverity(severity))
            {
                blocking.Add($"{project.GetProperty("path")}: {package.GetProperty("id")} {severity} {vulnerability.GetProperty("advisoryurl")}");
            }
        }

        return [.. blocking];
    }

    /// <summary>Applique le seuil de blocage aux sévérités de l'audit NuGet sans distinction de casse.</summary>
    /// <param name="severity">Sévérité déclarée par NuGet ; peut être null.</param>
    /// <returns>True pour High ou Critical ; false pour toute autre valeur.</returns>
    private static bool IsBlockingSeverity(string? severity)
        => string.Equals(severity, "High", StringComparison.OrdinalIgnoreCase)
            || string.Equals(severity, "Critical", StringComparison.OrdinalIgnoreCase);
}
