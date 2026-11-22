using System.Reflection.Metadata;

namespace HookRunner.CI;

internal static class PdbSequencePointReader
{
    public static Dictionary<string, HashSet<string>> Read(string pdbPath)
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
}
