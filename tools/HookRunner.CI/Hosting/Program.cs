using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Point d'entrée du programme de contrôle CI.</summary>
internal static class Program
{
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
                ["coverage", string root] => CoverageCheck.Run(Path.GetFullPath(root)),
                ["audit", string path] => AuditCheck.Run(path),
                ["docs", string root] => DocumentationLinkCheck.Run(Path.GetFullPath(root)),
                _ => throw new ArgumentException("Usage: HookRunner.CI coverage|docs <repository> OR audit <report.json>"),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or BadImageFormatException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
