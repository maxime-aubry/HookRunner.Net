namespace HookRunner.Application;

public static class HookSelection
{
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
