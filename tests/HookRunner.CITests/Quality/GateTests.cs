using System.Text.Json;
using HookRunner.CI;

namespace HookRunner.CITests;

/// <summary>Scénarios déterministes des contrôles de couverture et d'audit NuGet.</summary>
public sealed class GateTests
{
    /// <summary>
    /// Vérifie que l'inventaire PDB retrouve les lignes exécutables du fichier source de cette suite.
    /// </summary>
    [Fact]
    public void PortablePdbInspectionFindsRealTestSource()
    {
        string pdb = Path.ChangeExtension(typeof(GateTests).Assembly.Location, ".pdb");
        Dictionary<string, HashSet<string>> points = CoverageGate.ReadSequencePoints(pdb);
        Assert.Contains(points, pair => pair.Key.EndsWith("GateTests.cs", StringComparison.Ordinal) && pair.Value.Count > 0);
    }

    /// <summary>
    /// Vérifie qu'une assembly sans ligne exécutable est not-applicable plutôt que considérée comme totalement
    /// couverte.
    /// </summary>
    [Fact]
    public void EmptyAssemblyIsNotReportedAsFullyCovered()
    {
        CoverageResult result = CoverageGate.Evaluate("Example", [], []);
        Assert.Equal("not-applicable", result.Status);
    }

    /// <summary>
    /// Vérifie qu'un rapport couvrant une autre assembly ne masque pas l'absence de l'assembly attendue.
    /// </summary>
    [Fact]
    public void MissingAssemblyFailsEvenWhenAnotherAssemblyIsCovered()
    {
        CoverageResult result = Evaluate("Other", 1, 1);
        Assert.Equal("failed", result.Status);
        Assert.Contains("/source.cs:10", result.UncoveredLines);
    }

    /// <summary>
    /// Vérifie qu'une ligne jamais exécutée fait échouer la couverture, même si les branches sont couvertes.
    /// </summary>
    [Fact]
    public void UncoveredLineFails()
    {
        CoverageResult result = Evaluate("Example", 0, 1);
        Assert.Equal("failed", result.Status);
        Assert.Single(result.UncoveredLines);
    }

    /// <summary>
    /// Vérifie qu'une branche jamais exécutée fait échouer la couverture malgré des lignes couvertes.
    /// </summary>
    [Fact]
    public void UncoveredBranchFailsIndependentlyOfLines()
    {
        CoverageResult result = Evaluate("Example", 1, 0);
        Assert.Equal("failed", result.Status);
        Assert.Empty(result.UncoveredLines);
        Assert.Single(result.UncoveredBranches);
    }

    /// <summary>Vérifie qu'un rapport couvrant toutes les lignes et branches produit le statut passed.</summary>
    [Fact]
    public void FullyCoveredAssemblyPasses()
    {
        Assert.Equal("passed", Evaluate("Example", 1, 1).Status);
    }

    /// <summary>Vérifie que l'inventaire PDB détecte un document absent du rapport de couverture.</summary>
    [Fact]
    public void DocumentOmittedFromReportFails()
    {
        using JsonDocument report = MakeReport("Example", 1, 1);
        CoverageResult result = CoverageGate.Evaluate("Example", new() { ["/missing.cs"] = ["25"] }, [report.RootElement]);
        Assert.Contains("/missing.cs:25", result.UncoveredLines);
        Assert.Equal("failed", result.Status);
    }

    /// <summary>Vérifie qu'une suite peut couvrir les lignes et branches manquées par une autre.</summary>
    [Fact]
    public void CoverageFromDifferentTestSuitesIsCombined()
    {
        using JsonDocument missed = MakeReport("Example", 0, 0);
        using JsonDocument covered = MakeReport("Example", 1, 1);
        CoverageResult result = CoverageGate.Evaluate("Example", ExpectedLines(), [missed.RootElement, covered.RootElement]);
        Assert.Equal("passed", result.Status);
    }

    /// <summary>Vérifie le filtrage des sévérités sur les dépendances NuGet directes et transitives.</summary>
    /// <param name="severity">Sévérité de la vulnérabilité simulée.</param>
    /// <param name="group">Groupe NuGet topLevelPackages ou transitivePackages.</param>
    /// <param name="failures">Nombre attendu de diagnostics bloquants.</param>
    [Theory]
    [InlineData("High", "topLevelPackages", 1)]
    [InlineData("Critical", "transitivePackages", 1)]
    [InlineData("Moderate", "transitivePackages", 0)]
    [InlineData("Low", "topLevelPackages", 0)]
    public void AuditAppliesSeverityPolicyToDirectAndTransitivePackages(string severity, string group, int failures)
    {
        using JsonDocument report = JsonDocument.Parse($$"""
            {"version":1,"projects":[{"path":"project.csproj","frameworks":[{"{{group}}":[{"id":"Example","vulnerabilities":[{"severity":"{{severity}}","advisoryurl":"https://example.invalid/advisory"}]}]}]}]}
            """);
        Assert.Equal(failures, AuditGate.BlockingVulnerabilities(report.RootElement).Length);
    }

    /// <summary>Vérifie qu'un projet sans frameworks est accepté comme ne présentant aucun signalement.</summary>
    [Fact]
    public void NuGetMayOmitFrameworksForProjectsWithoutFindings()
    {
        using JsonDocument report = JsonDocument.Parse("{\"version\":1,\"projects\":[{\"path\":\"project.csproj\"}]}");
        Assert.Empty(AuditGate.BlockingVulnerabilities(report.RootElement));
    }

    /// <summary>
    /// Vérifie qu'une erreur de collecte NuGet est refusée plutôt que traitée comme un audit vide.
    /// </summary>
    [Fact]
    public void AuditTransportProblemsCannotPassAsNoVulnerabilities()
    {
        using JsonDocument report = JsonDocument.Parse("{\"version\":1,\"projects\":[{\"path\":\"project.csproj\"}],\"problems\":[\"source unavailable\"]}");
        Assert.Throws<InvalidDataException>(() => AuditGate.BlockingVulnerabilities(report.RootElement));
    }

    /// <summary>Construit l'inventaire minimal d'une ligne exécutable pour les scénarios de couverture.</summary>
    /// <returns>Un dictionnaire neuf contenant /source.cs à la ligne 10.</returns>
    private static Dictionary<string, HashSet<string>> ExpectedLines() => new() { ["/source.cs"] = ["10"] };

    /// <summary>Évalue un rapport synthétique face à l'inventaire attendu de l'assembly Example.</summary>
    /// <param name="assembly">Nom du module présent dans le rapport simulé.</param>
    /// <param name="lineHits">Nombre d'exécutions de la ligne simulée.</param>
    /// <param name="branchHits">Nombre d'exécutions de la branche simulée.</param>
    /// <returns>Le verdict de couverture de l'assembly Example.</returns>
    private static CoverageResult Evaluate(string assembly, int lineHits, int branchHits)
    {
        using JsonDocument report = MakeReport(assembly, lineHits, branchHits);
        return CoverageGate.Evaluate("Example", ExpectedLines(), [report.RootElement]);
    }

    /// <summary>Construit un document JSON Coverlet minimal contenant une ligne et une branche.</summary>
    /// <param name="assembly">Nom du module à inscrire dans le rapport.</param>
    /// <param name="lineHits">Nombre d'exécutions de la ligne 10.</param>
    /// <param name="branchHits">Nombre d'exécutions de la branche à l'offset 0.</param>
    /// <returns>Un document JSON dont l'appelant doit assurer la libération.</returns>
    private static JsonDocument MakeReport(string assembly, int lineHits, int branchHits)
    {
        object method = new
        {
            Lines = new Dictionary<string, int> { ["10"] = lineHits },
            Branches = new[] { new { Line = 10, Offset = 0, Path = 0, Hits = branchHits } },
        };
        Dictionary<string, object> methods = new() { ["Method()"] = method };
        Dictionary<string, object> types = new() { ["Example.Type"] = methods };
        Dictionary<string, object> documents = new() { ["/source.cs"] = types };
        return JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object> { [assembly + ".dll"] = documents }));
    }
}
