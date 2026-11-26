namespace HookRunner.Application;

/// <summary>Validation et sélection communes des stratégies dans le superviseur et dans l'enfant.</summary>
public static class HookSelection
{
    /// <summary>
    /// Valide tous les noms enregistrés puis sélectionne exactement une stratégie sans distinction de casse.
    /// </summary>
    /// <param name="strategies">
    /// Toutes les stratégies enregistrées par le consommateur ; la collection est énumérée intégralement.
    /// </param>
    /// <param name="name">Nom demandé, comparé avec OrdinalIgnoreCase sans suppression d'espaces.</param>
    /// <returns>L'instance enregistrée correspondant au nom demandé, sans exécuter son traitement.</returns>
    /// <exception cref="ArgumentNullException">La collection de stratégies est null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Un nom enregistré est vide, deux noms sont identiques sans distinction de casse, ou le nom demandé est
    /// inconnu.
    /// </exception>
    public static HookStrategy Select(IEnumerable<HookStrategy> strategies, string name)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        Dictionary<string, HookStrategy> hooks = BuildValidatedIndex(strategies);

        return hooks.TryGetValue(name, out HookStrategy? selected)
            ? selected
            : throw new InvalidOperationException($"Unknown hook: '{name}'.");
    }

    /// <summary>Construit l'index des hooks en validant tous les noms enregistrés.</summary>
    /// <param name="strategies">Stratégies à indexer sans créer ni exécuter de hook.</param>
    /// <returns>Les instances enregistrées, indexées par nom sans distinction de casse.</returns>
    /// <exception cref="InvalidOperationException">Un nom est vide ou dupliqué sans distinction de casse.</exception>
    private static Dictionary<string, HookStrategy> BuildValidatedIndex(IEnumerable<HookStrategy> strategies)
    {
        Dictionary<string, HookStrategy> hooks = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (HookStrategy strategy in strategies)
        {
            string hookName = strategy.Name;
            if (string.IsNullOrWhiteSpace(hookName))
            {
                throw new InvalidOperationException("Every hook must have a non-empty Name.");
            }

            if (!hooks.TryAdd(hookName, strategy))
            {
                throw new InvalidOperationException($"Duplicate hook Name: '{hookName}'.");
            }
        }

        return hooks;
    }
}
