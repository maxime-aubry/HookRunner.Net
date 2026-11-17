using System.Text.Json;
using HookRunner.CI;

namespace HookRunner.CITests;

public sealed class GateTests
{
    [Fact]
    public void PortablePdbInspectionFindsRealTestSource()
    {
        string pdb = Path.ChangeExtension(typeof(GateTests).Assembly.Location, ".pdb");
        Dictionary<string, HashSet<string>> points = CoverageGate.ReadSequencePoints(pdb);
        Assert.Contains(points, pair => pair.Key.EndsWith("GateTests.cs", StringComparison.Ordinal) && pair.Value.Count > 0);
    }

    [Fact]
    public void EmptyAssemblyIsNotReportedAsFullyCovered()
    {
        CoverageResult result = CoverageGate.Evaluate("Example", [], []);
        Assert.Equal("not-applicable", result.Status);
    }

    [Fact]
    public void MissingAssemblyFailsEvenWhenAnotherAssemblyIsCovered()
    {
        CoverageResult result = Evaluate("Other", 1, 1);
        Assert.Equal("failed", result.Status);
        Assert.Contains("/source.cs:10", result.UncoveredLines);
    }

    [Fact]
    public void UncoveredLineFails()
    {
        CoverageResult result = Evaluate("Example", 0, 1);
        Assert.Equal("failed", result.Status);
        Assert.Single(result.UncoveredLines);
    }

    [Fact]
    public void UncoveredBranchFailsIndependentlyOfLines()
    {
        CoverageResult result = Evaluate("Example", 1, 0);
        Assert.Equal("failed", result.Status);
        Assert.Empty(result.UncoveredLines);
        Assert.Single(result.UncoveredBranches);
    }

    [Fact]
    public void FullyCoveredAssemblyPasses()
    {
        Assert.Equal("passed", Evaluate("Example", 1, 1).Status);
    }

    [Fact]
    public void DocumentOmittedFromReportFails()
    {
        using JsonDocument report = MakeReport("Example", 1, 1);
        CoverageResult result = CoverageGate.Evaluate("Example", new() { ["/missing.cs"] = ["25"] }, [report.RootElement]);
        Assert.Contains("/missing.cs:25", result.UncoveredLines);
        Assert.Equal("failed", result.Status);
    }

    [Fact]
    public void CoverageFromDifferentTestSuitesIsCombined()
    {
        using JsonDocument missed = MakeReport("Example", 0, 0);
        using JsonDocument covered = MakeReport("Example", 1, 1);
        CoverageResult result = CoverageGate.Evaluate("Example", ExpectedLines(), [missed.RootElement, covered.RootElement]);
        Assert.Equal("passed", result.Status);
    }

    [Theory]
    [InlineData("High", "topLevelPackages", 1)]
    [InlineData("Critical", "transitivePackages", 1)]
    [InlineData("Moderate", "transitivePackages", 0)]
    [InlineData("Low", "topLevelPackages", 0)]
    public void AuditAppliesSeverityPolicyToDirectAndTransitivePackages(string severity, string group, int failures)
    {
        using var report = JsonDocument.Parse($$"""
            {"version":1,"projects":[{"path":"project.csproj","frameworks":[{"{{group}}":[{"id":"Example","vulnerabilities":[{"severity":"{{severity}}","advisoryurl":"https://example.invalid/advisory"}]}]}]}]}
            """);
        Assert.Equal(failures, AuditGate.BlockingVulnerabilities(report.RootElement).Length);
    }

    [Fact]
    public void NuGetMayOmitFrameworksForProjectsWithoutFindings()
    {
        using var report = JsonDocument.Parse("{\"version\":1,\"projects\":[{\"path\":\"project.csproj\"}]}");
        Assert.Empty(AuditGate.BlockingVulnerabilities(report.RootElement));
    }

    [Fact]
    public void AuditTransportProblemsCannotPassAsNoVulnerabilities()
    {
        using var report = JsonDocument.Parse("{\"version\":1,\"projects\":[{\"path\":\"project.csproj\"}],\"problems\":[\"source unavailable\"]}");
        Assert.Throws<InvalidDataException>(() => AuditGate.BlockingVulnerabilities(report.RootElement));
    }

    private static Dictionary<string, HashSet<string>> ExpectedLines() => new() { ["/source.cs"] = ["10"] };

    private static CoverageResult Evaluate(string assembly, int lineHits, int branchHits)
    {
        using JsonDocument report = MakeReport(assembly, lineHits, branchHits);
        return CoverageGate.Evaluate("Example", ExpectedLines(), [report.RootElement]);
    }

    private static JsonDocument MakeReport(string assembly, int lineHits, int branchHits)
    {
        var method = new
        {
            Lines = new Dictionary<string, int> { ["10"] = lineHits },
            Branches = new[] { new { Line = 10, Offset = 0, Path = 0, Hits = branchHits } },
        };
        var methods = new Dictionary<string, object> { ["Method()"] = method };
        var types = new Dictionary<string, object> { ["Example.Type"] = methods };
        var documents = new Dictionary<string, object> { ["/source.cs"] = types };
        return JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object> { [assembly + ".dll"] = documents }));
    }
}
