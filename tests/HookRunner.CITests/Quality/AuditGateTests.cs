using System.Text.Json;
using HookRunner.CI;

namespace HookRunner.CITests;

/// <summary>Scénarios déterministes du format et de la politique de sévérité de l'audit NuGet.</summary>
public sealed class AuditGateTests
{
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
}
