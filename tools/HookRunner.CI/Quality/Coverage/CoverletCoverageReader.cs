using System.Text.Json;

namespace HookRunner.CI;

/// <summary>Lit et fusionne les observations Coverlet pour une assembly.</summary>
internal static class CoverletCoverageReader
{
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
