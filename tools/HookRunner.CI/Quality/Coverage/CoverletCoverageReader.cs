using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Lit et fusionne les observations Coverlet pour une assembly.</summary>
internal static class CoverletCoverageReader
{
    /// <summary>Fusionne les lignes et branches de tous les modules portant le nom de l'assembly demandée.</summary>
    /// <param name="assembly">Nom d'assembly sans extension, comparé de manière ordinale.</param>
    /// <param name="reports">Racines JSON des rapports Coverlet à parcourir.</param>
    /// <param name="repositoryRoot">Racine locale de résolution des chemins déterministes ; peut être null.</param>
    /// <returns>Les observations fusionnées et un indicateur de présence de l'assembly dans les rapports.</returns>
    public static CoverageSnapshot Read(string assembly, IEnumerable<JsonElement> reports, string? repositoryRoot)
    {
        CoverageSnapshot coverage = new();
        foreach (JsonElement report in reports)
        {
            foreach (JsonProperty module in report.EnumerateObject())
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(module.Name.Replace('\\', '/')), assembly, StringComparison.Ordinal))
                {
                    coverage.Found = true;
                    ReadModule(module.Value, coverage, repositoryRoot);
                }
            }
        }

        return coverage;
    }

    /// <summary>Parcourt les documents, types et méthodes d'un module en normalisant leurs chemins sources.</summary>
    /// <param name="module">Objet JSON du module sélectionné.</param>
    /// <param name="coverage">Observations à enrichir avec celles du module.</param>
    /// <param name="repositoryRoot">Racine locale de résolution des chemins déterministes ; peut être null.</param>
    private static void ReadModule(JsonElement module, CoverageSnapshot coverage, string? repositoryRoot)
    {
        foreach (JsonProperty document in module.EnumerateObject())
        {
            string file = CoverageSourcePath.Resolve(document.Name, repositoryRoot);
            foreach (JsonProperty type in document.Value.EnumerateObject())
            {
                foreach (JsonProperty method in type.Value.EnumerateObject())
                {
                    ReadMethod(file, method, coverage);
                }
            }
        }
    }

    /// <summary>Fusionne la couverture d'une méthode en conservant tout passage observé dans une autre suite.</summary>
    /// <param name="file">Chemin source normalisé du document contenant la méthode.</param>
    /// <param name="method">Propriété Coverlet contenant le nom, les lignes et les branches de la méthode.</param>
    /// <param name="coverage">Observations modifiées ; un nombre de passages positif marque le point comme couvert.</param>
    private static void ReadMethod(string file, JsonProperty method, CoverageSnapshot coverage)
    {
        foreach (JsonProperty line in method.Value.GetProperty("Lines").EnumerateObject())
        {
            SourceLine key = new(file, line.Name);
            coverage.Lines[key] = coverage.Lines.GetValueOrDefault(key) || line.Value.GetInt32() > 0;
        }

        foreach (JsonElement branch in method.Value.GetProperty("Branches").EnumerateArray())
        {
            SourceBranch key = new(file, branch.GetProperty("Line").ToString(), method.Name,
                branch.GetProperty("Offset").ToString(), branch.GetProperty("Path").ToString());
            coverage.Branches[key] = coverage.Branches.GetValueOrDefault(key) || branch.GetProperty("Hits").GetInt32() > 0;
        }
    }
}
