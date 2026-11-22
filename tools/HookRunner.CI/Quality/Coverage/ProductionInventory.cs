namespace HookRunner.CI;

/// <summary>Inventaire de compilation validé avant l'évaluation de couverture.</summary>
internal sealed record ProductionInventory(string Assembly, string AssemblyPath, string PdbPath)
{
    public static ProductionInventory Read(string path)
    {
        string[] fields = File.ReadAllLines(path);
        if (fields.Length < 4 || fields.Take(4).Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException($"Invalid production inventory: {path}");
        }

        ProductionInventory inventory = new(fields[1], fields[2], fields[3]);
        if (!File.Exists(inventory.AssemblyPath))
        {
            throw new FileNotFoundException("Production assembly missing", inventory.AssemblyPath);
        }

        return inventory;
    }
}
