using Microsoft.Extensions.Configuration;

namespace HookRunner.Cli;

/// <summary>Valeurs globales chargées à côté du programme compilé avant préparation d'une invocation.</summary>
/// <param name="TimeoutSeconds">
/// Timeout global positif en secondes, chargé depuis la configuration ou fixé à 120.
/// </param>
/// <param name="StateFilePath">Chemin du fichier d'état ; aucune persistance n'est effectuée par ce type.</param>
internal sealed record HookRunnerConfiguration(int TimeoutSeconds, string StateFilePath)
{
    /// <summary>
    /// Charge appsettings.json depuis le dossier du programme compilé et lit la section HookRunner.
    /// </summary>
    /// <returns>
    /// Le timeout configuré, ou 120 secondes s'il est absent, et le chemin d'état encore non résolu.
    /// </returns>
    /// <remarks>
    /// Le fichier est obligatoire et la configuration est libérée après lecture. Un chemin d'état absent devient
    /// une chaîne vide qui sera refusée lors de la préparation.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Le timeout présent n'est pas un entier strictement positif représentable par Int32.
    /// </exception>
    /// <exception cref="FileNotFoundException">Le fichier de configuration obligatoire est absent.</exception>
    /// <exception cref="InvalidDataException">
    /// Le fichier de configuration ne peut pas être chargé comme configuration JSON.
    /// </exception>
    public static HookRunnerConfiguration Load()
    {
        using ConfigurationRoot configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        string? timeoutValue = configuration["HookRunner:TimeoutSeconds"];
        int timeoutSeconds = timeoutValue is null ? 120 : TimeoutSecondsParser.Parse(timeoutValue);
        string stateFilePath = configuration["HookRunner:StateFilePath"] ?? string.Empty;
        return new HookRunnerConfiguration(timeoutSeconds, stateFilePath);
    }
}
