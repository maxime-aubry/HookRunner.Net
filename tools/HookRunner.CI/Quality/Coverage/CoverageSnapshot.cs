namespace HookRunner.CI;

internal sealed class CoverageSnapshot
{
    public bool Found { get; set; }
    public Dictionary<SourceLine, bool> Lines { get; } = [];
    public Dictionary<SourceBranch, bool> Branches { get; } = [];
}
