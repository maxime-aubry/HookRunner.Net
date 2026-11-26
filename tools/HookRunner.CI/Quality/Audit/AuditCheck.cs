using System.Text.Json;

namespace HookRunner.CI;

internal static class AuditCheck
{
    /// <summary>Lit le rapport d'audit NuGet et écrit les vulnérabilités bloquantes sur stderr.</summary>
    /// <param name="path">Chemin du rapport JSON produit par NuGet.</param>
    /// <returns>0 en l'absence de vulnérabilité bloquante ; sinon 1.</returns>
    /// <remarks>Les erreurs de lecture ou de structure du rapport se propagent au routeur de commandes.</remarks>
    public static int Run(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string[] vulnerabilities = AuditGate.BlockingVulnerabilities(document.RootElement);
        foreach (string vulnerability in vulnerabilities)
        {
            Console.Error.WriteLine(vulnerability);
        }

        return vulnerabilities.Length == 0 ? 0 : 1;
    }
}
