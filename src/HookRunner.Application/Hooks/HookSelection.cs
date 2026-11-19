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
        Dictionary<string, HookStrategy> hooks = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (HookStrategy strategy in strategies)
        {
            if (string.IsNullOrWhiteSpace(strategy.Name))
            {
                throw new InvalidOperationException("Every hook must have a non-empty Name.");
            }

            if (!hooks.TryAdd(strategy.Name, strategy))
            {
                throw new InvalidOperationException($"Duplicate hook Name: '{strategy.Name}'.");
            }
        }

        return hooks.TryGetValue(name, out HookStrategy? selected)
            ? selected
            : throw new InvalidOperationException($"Unknown hook: '{name}'.");
    }
}
