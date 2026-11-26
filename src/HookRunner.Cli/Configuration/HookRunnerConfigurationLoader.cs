using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace HookRunner.Cli;

/// <summary>Charge le fichier du programme consommateur et interprète les valeurs de la section HookRunner.</summary>
internal static class HookRunnerConfigurationLoader
{
    /// <summary>Charge obligatoirement appsettings.json depuis le dossier fourni et libère la configuration.</summary>
    /// <param name="baseDirectory">Dossier absolu contenant la configuration du programme compilé.</param>
    /// <returns>Les valeurs configurées, avec défauts de 120 secondes et trois demandes, et chemin non résolu.</returns>
    /// <remarks>
    /// Toutes les surcharges de hooks sont vérifiées. Un chemin d'état absent devient une chaîne vide qui sera
    /// refusée lors de la préparation ; aucune validation du système de fichiers cible n'est effectuée ici.
    /// </remarks>
    /// <exception cref="ArgumentException">Le timeout présent n'est pas un entier Int32 strictement positif.</exception>
    /// <exception cref="FileNotFoundException">Le fichier de configuration obligatoire est absent.</exception>
    /// <exception cref="InvalidDataException">
    /// Le fichier n'est pas une configuration JSON valide, un plafond est invalide ou un hook surcharge le chemin d'état.
    /// </exception>
    public static HookRunnerConfiguration Load(string baseDirectory)
    {
        using ConfigurationRoot configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        return ReadConfiguration(configuration.GetSection("HookRunner"));
    }

    /// <summary>Interprète les valeurs globales et les surcharges sans résoudre les options d'une invocation.</summary>
    /// <param name="section">Section HookRunner du fichier chargé.</param>
    /// <returns>Les valeurs typées avec leurs défauts et les plafonds par nom de hook.</returns>
    private static HookRunnerConfiguration ReadConfiguration(IConfigurationSection section)
    {
        string? timeoutValue = section["TimeoutSeconds"];
        int timeoutSeconds = timeoutValue is null ? 120 : TimeoutSecondsParser.Parse(timeoutValue);
        string stateFilePath = section["StateFilePath"] ?? string.Empty;
        int limit = ParseLimit(section["MaxBlockingRequests"] ?? "3");
        return new HookRunnerConfiguration(timeoutSeconds, stateFilePath, limit, ReadHookLimits(section.GetSection("Hooks")));
    }

    /// <summary>Collecte les plafonds par hook et refuse toute surcharge du chemin d'état global.</summary>
    /// <param name="section">Section Hooks contenant les surcharges par nom de hook.</param>
    /// <returns>Les seuls plafonds explicitement définis, avec comparaison des noms sans distinction de casse.</returns>
    private static Dictionary<string, int> ReadHookLimits(IConfigurationSection section)
    {
        Dictionary<string, int> hookLimits = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (IConfigurationSection hook in section.GetChildren())
        {
            if (hook["StateFilePath"] is not null)
            {
                throw new InvalidDataException("HookRunner:StateFilePath is global and cannot be overridden per hook.");
            }

            string? hookLimit = hook["MaxBlockingRequests"];
            if (hookLimit is not null)
            {
                hookLimits.Add(hook.Key, ParseLimit(hookLimit));
            }
        }

        return hookLimits;
    }

    /// <summary>Valide un plafond configuré sans signe ni fraction.</summary>
    /// <param name="value">Valeur textuelle de configuration.</param>
    /// <returns>Entier non négatif, zéro désactivant le blocage métier.</returns>
    /// <exception cref="InvalidDataException">La valeur n'est pas un entier non négatif Int32.</exception>
    private static int ParseLimit(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int limit))
        {
            throw new InvalidDataException("MaxBlockingRequests must be a non-negative Int32 integer.");
        }

        return limit;
    }
}
