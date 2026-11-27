using System.Text.Json;
using HookRunner.Application;
using HookRunner.Domain;
using HookRunner.Infrastructure;

namespace HookRunner.ExecutionTests;

/// <summary>Publication du stockage réel pendant qu'un lecteur conserve l'ancien document ouvert.</summary>
public sealed class StatePublicationTests
{
    /// <summary>Un lecteur garde son instantané complet tandis que le chemin publié reçoit le nouvel état.</summary>
    /// <returns>Une tâche vérifiant les deux documents sans verrou de lecture applicatif.</returns>
    [Fact]
    public async Task OpenReaderKeepsPreviousDocumentWhileNextCounterIsPublished()
    {
        using SolutionFixture fixture = new();
        string path = Path.Combine(fixture.Root, "state.json");
        FileHookStateStore store = new();
        HookIdentity identity = new(fixture.Root, "codex", "session", "inspect");
        await using IHookStateSession session = await store.OpenAsync(path, identity, TestContext.Current.CancellationToken);
        await session.SaveAsync(1, TestContext.Current.CancellationToken);
        await using FileStream previous = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        await session.SaveAsync(2, TestContext.Current.CancellationToken);

        using JsonDocument oldDocument = await JsonDocument.ParseAsync(previous, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, Assert.Single(oldDocument.RootElement.GetProperty("Counters").EnumerateObject()).Value.GetInt32());
        using JsonDocument published = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(2, Assert.Single(published.RootElement.GetProperty("Counters").EnumerateObject()).Value.GetInt32());
    }
}
