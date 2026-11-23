namespace HookRunner.CITests;

/// <summary>Vérifie les erreurs de données à travers les commandes publiques du programme CI.</summary>
public sealed class CommandTests
{
    /// <summary>Vérifie qu'un inventaire de production incomplet donne le code 1 et un diagnostic pris en charge.</summary>
    /// <returns>Une tâche représentant l'exécution de la commande CI et ses assertions.</returns>
    [Fact]
    public async Task MalformedProductionInventoryReturnsOneWithDiagnostic()
    {
        using TemporaryDirectory directory = new();
        directory.Write("src/Example/Example.csproj", "<Project />");
        directory.Write("artifacts/coverage/coverage.json", "{}");
        directory.Write("artifacts/inventory/Example.txt", "incomplete");

        (int exitCode, string error) = await CiProgram.RunAsync(["coverage", directory.Root]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid production inventory:", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }

    /// <summary>Vérifie qu'un rapport d'audit incompatible, vide ou en erreur donne le code 1 sans exception non gérée.</summary>
    /// <param name="report">Contenu JSON du rapport invalide.</param>
    /// <param name="diagnostic">Fragment attendu dans le diagnostic écrit sur stderr.</param>
    /// <returns>Une tâche représentant l'exécution de la commande CI et ses assertions.</returns>
    [Theory]
    [InlineData("{\"version\":2,\"projects\":[{}]}", "Unsupported or empty NuGet audit report")]
    [InlineData("{\"version\":1,\"projects\":[]}", "Unsupported or empty NuGet audit report")]
    [InlineData("{\"version\":1,\"projects\":[{}],\"problems\":[\"unavailable\"]}", "NuGet reported audit problems")]
    public async Task InvalidAuditReportReturnsOneWithDiagnostic(string report, string diagnostic)
    {
        using TemporaryDirectory directory = new();
        string path = directory.Write("audit.json", report);

        (int exitCode, string error) = await CiProgram.RunAsync(["audit", path]);

        Assert.Equal(1, exitCode);
        Assert.Contains(diagnostic, error, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", error, StringComparison.Ordinal);
    }
}
