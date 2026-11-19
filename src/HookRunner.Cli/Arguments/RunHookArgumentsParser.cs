using HookRunner.Application;

namespace HookRunner.Cli;

/// <summary>Analyse des arguments applicatifs de la commande execute.</summary>
internal static class RunHookArgumentsParser
{
    /// <summary>Analyse execute, le nom du hook et les options explicites de l'invocation Codex.</summary>
    /// <param name="args">Arguments applicatifs, sans les options du lanceur dotnet.</param>
    /// <returns>Les arguments typés ; timeout absent à null et projets absents sous forme de liste vide.</returns>
    /// <remarks>
    /// Les noms de projets sont séparés sur les virgules et leurs espaces périphériques retirés. Leur résolution
    /// reste à la charge du préparateur.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// La commande, le nom, l'agent, le timeout ou une option est absent, répété ou invalide.
    /// </exception>
    public static RunHookArgs Parse(string[] args)
    {
        if (args.Length < 2 || args[0] != "execute"
            || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Usage: execute <hook-name> --agent codex [--timeout <seconds>].");
        }

        Dictionary<string, string> options = ParseOptions(args);
        if (!options.TryGetValue("--agent", out string? agent) || agent != "codex")
        {
            throw new ArgumentException("An explicit --agent codex is required.");
        }

        int? timeoutSeconds = options.TryGetValue("--timeout", out string? timeoutValue)
            ? TimeoutSecondsParser.Parse(timeoutValue)
            : null;
        string[] projectNames = options.TryGetValue("--projects", out string? projectSelection)
            ? projectSelection.Split(',', StringSplitOptions.TrimEntries)
            : [];
        return new RunHookArgs(args[1], timeoutSeconds, projectNames);
    }

    /// <summary>
    /// Collecte les paires nom-valeur suivant le nom du hook en refusant les options inconnues ou répétées.
    /// </summary>
    /// <param name="args">Commande complète dont les options commencent au troisième argument.</param>
    /// <returns>
    /// Un dictionnaire sensible à la casse contenant agent, timeout et projets lorsqu'ils sont fournis.
    /// </returns>
    /// <exception cref="ArgumentException">Une option est inconnue, répétée ou dépourvue de valeur.</exception>
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = [with(StringComparer.Ordinal)];
        for (int index = 2; index < args.Length; index += 2)
        {
            string optionName = args[index];
            if (index + 1 >= args.Length || !options.TryAdd(optionName, args[index + 1]))
            {
                throw new ArgumentException($"Missing or repeated option: '{optionName}'.");
            }

            if (optionName is not ("--agent" or "--timeout" or "--projects"))
            {
                throw new ArgumentException($"Unknown option: '{optionName}'.");
            }
        }

        return options;
    }
}
