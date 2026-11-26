namespace HookRunner.CI;

/// <summary>Inventaire de compilation validé avant l'évaluation de couverture.</summary>
/// <param name="Assembly">Nom de l'assembly de production sans extension.</param>
/// <param name="AssemblyPath">Chemin de l'assembly dont l'existence est vérifiée à la lecture.</param>
/// <param name="PdbPath">Chemin du PDB portable à ouvrir pour inventorier les lignes exécutables.</param>
internal sealed record ProductionInventory(string Assembly, string AssemblyPath, string PdbPath)
{
    /// <summary>Lit les chemins de compilation d'un projet et vérifie l'existence de son assembly.</summary>
    /// <param name="path">Fichier d'inventaire contenant le projet, le nom d'assembly et les chemins de l'assembly et du PDB.</param>
    /// <returns>L'inventaire de l'assembly ; le PDB sera ouvert lors de l'évaluation de couverture.</returns>
    /// <exception cref="InvalidDataException">Une des quatre premières lignes manque ou est vide.</exception>
    /// <exception cref="FileNotFoundException">L'inventaire ou l'assembly déclarée est introuvable.</exception>
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
