using System.Reflection.Metadata;
using System.Text.Json;

namespace HookRunner.CI;

public sealed record CoverageResult(string Assembly, string Status, string[] UncoveredLines, string[] UncoveredBranches);

public static class CoverageGate
{
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
