using System.Reflection.Metadata;
using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Contrôle de couverture complète fondé sur les rapports Coverlet et l'inventaire des PDB.</summary>
public static class CoverageGate
{
    /// <summary>
    /// Lit les points de séquence visibles d'un PDB portable pour inventorier les lignes réellement exécutables.
    /// </summary>
    /// <param name="pdbPath">Chemin du PDB portable produit avec l'assembly à contrôler.</param>
    /// <returns>
    /// Les numéros de lignes distincts par fichier, avec chemins normalisés sur des barres obliques.
    /// </returns>
    /// <remarks>
    /// Les points masqués, sans document et les sources générées sous obj sont exclus. Le flux et le lecteur de
    /// métadonnées sont libérés après lecture.
    /// </remarks>
    /// <exception cref="BadImageFormatException">
    /// Le fichier ne contient pas les métadonnées d'un PDB portable exploitable.
    /// </exception>
    /// <exception cref="IOException">Le PDB ne peut pas être ouvert ou lu.</exception>
    public static Dictionary<string, HashSet<string>> ReadSequencePoints(string pdbPath)
    {
        using FileStream stream = File.OpenRead(pdbPath);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        MetadataReader reader = provider.GetMetadataReader();
        Dictionary<string, HashSet<string>> lines = [with(StringComparer.Ordinal)];
        foreach (MethodDebugInformationHandle method in reader.MethodDebugInformation)
        {
            foreach (SequencePoint point in reader.GetMethodDebugInformation(method).GetSequencePoints())
            {
                if (point.IsHidden || point.Document.IsNil)
                {
                    continue;
                }

                string file = reader.GetString(reader.GetDocument(point.Document).Name).Replace('\\', '/');
                if (file.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!lines.TryGetValue(file, out HashSet<string>? fileLines))
                {
                    fileLines = [];
                    lines.Add(file, fileLines);
                }

                fileLines.Add(point.StartLine.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return lines;
    }

    /// <summary>
    /// Fusionne les rapports de couverture d'une assembly et les confronte à l'inventaire indépendant du PDB.
    /// </summary>
    /// <param name="assembly">Nom de l'assembly, sans extension, comparé de manière ordinale.</param>
    /// <param name="expected">Lignes exécutables attendues par fichier d'après le PDB.</param>
    /// <param name="reports">
    /// Rapports Coverlet JSON de toutes les suites ; ils sont lus sans être modifiés.
    /// </param>
    /// <returns>
    /// Un résultat not-applicable pour un inventaire vide, passed si toutes les lignes et branches sont couvertes,
    /// sinon failed avec les points manquants.
    /// </returns>
    /// <remarks>
    /// Un point est couvert si au moins une suite l'a exécuté. Une assembly ou un document absent du collecteur
    /// échoue dès qu'il contient des lignes attendues. Les documents et branches des rapports participent aussi au
    /// contrôle.
    /// </remarks>
    public static CoverageResult Evaluate(string assembly, Dictionary<string, HashSet<string>> expected, IEnumerable<JsonElement> reports)
    {
        if (expected.Count == 0)
        {
            return new(assembly, "not-applicable", [], []);
        }

        Dictionary<string, bool> lines = [with(StringComparer.Ordinal)];
        Dictionary<string, bool> branches = [with(StringComparer.Ordinal)];
        bool found = false;
        foreach (JsonElement report in reports)
        {
            foreach (JsonProperty module in report.EnumerateObject())
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(module.Name.Replace('\\', '/')), assembly, StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;
                foreach (JsonProperty document in module.Value.EnumerateObject())
                {
                    string file = document.Name.Replace('\\', '/');
                    foreach (JsonProperty type in document.Value.EnumerateObject())
                    {
                        foreach (JsonProperty method in type.Value.EnumerateObject())
                        {
                            foreach (JsonProperty line in method.Value.GetProperty("Lines").EnumerateObject())
                            {
                                string key = file + ":" + line.Name;
                                lines[key] = lines.GetValueOrDefault(key) || line.Value.GetInt32() > 0;
                            }

                            foreach (JsonElement branch in method.Value.GetProperty("Branches").EnumerateArray())
                            {
                                string key = $"{file}:{branch.GetProperty("Line")}: {method.Name} offset {branch.GetProperty("Offset")} path {branch.GetProperty("Path")}";
                                branches[key] = branches.GetValueOrDefault(key) || branch.GetProperty("Hits").GetInt32() > 0;
                            }
                        }
                    }
                }
            }
        }

        // Check the PDB independently: an assembly or document omitted by the collector must fail.
        foreach ((string file, HashSet<string> fileLines) in expected)
        {
            foreach (string line in fileLines)
            {
                lines.TryAdd(file + ":" + line, false);
            }
        }

        string[] uncoveredLines = [.. lines.Where(pair => !pair.Value).Select(pair => pair.Key).Order()];
        string[] uncoveredBranches = [.. branches.Where(pair => !pair.Value).Select(pair => pair.Key).Order()];
        string status = found && uncoveredLines.Length == 0 && uncoveredBranches.Length == 0 ? "passed" : "failed";
        return new(assembly, status, uncoveredLines, uncoveredBranches);
    }
}
