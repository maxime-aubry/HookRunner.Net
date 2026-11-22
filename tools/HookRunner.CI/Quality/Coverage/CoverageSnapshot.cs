namespace HookRunner.CI;

internal sealed class CoverageSnapshot
{
    public bool Found { get; set; }
    public Dictionary<SourceLine, bool> Lines { get; } = [];
    public Dictionary<SourceBranch, bool> Branches { get; } = [];
}

internal readonly record struct SourceLine(string File, string Line)
{
    public override string ToString() => $"{File}:{Line}";
}

internal readonly record struct SourceBranch(string File, string Line, string Method, string Offset, string Path)
{
    public override string ToString() => $"{File}:{Line}: {Method} offset {Offset} path {Path}";
}
