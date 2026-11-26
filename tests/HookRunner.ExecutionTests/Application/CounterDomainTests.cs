using HookRunner.Domain;

namespace HookRunner.ExecutionTests;

/// <summary>Règles pures du compteur et de son identité, indépendantes du système hôte.</summary>
public sealed class CounterDomainTests
{
    /// <summary>L'identité normalise seulement le nom du hook et préserve les racines physiques distinctes.</summary>
    [Fact]
    public void IdentityNormalizationPreservesPhysicalRootsAndIgnoresHookNameCase()
    {
        HookIdentity lower = new("/work/tree", "codex", "session", "Hook");
        HookIdentity upper = new("/WORK/TREE", "codex", "session", "HOOK");

        Assert.Equal(lower.Normalize(), (lower with { HookName = "HOOK" }).Normalize());
        Assert.NotEqual(lower.Normalize(), upper.Normalize());
        Assert.Equal("HOOK", lower.Normalize().HookName);
    }

    /// <summary>Une demande au plafond maximal ne déborde pas ; les valeurs négatives sont invalides.</summary>
    [Fact]
    public void CounterRejectsNegativeValuesAndNeverOverflows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestCounter(-1));
        RequestCounter counter = new(int.MaxValue - 1);
        Assert.True(counter.RequestCorrection(int.MaxValue));
        Assert.False(counter.RequestCorrection(int.MaxValue));
        Assert.Equal(int.MaxValue, counter.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => counter.RequestCorrection(-1));
    }

    /// <summary>Les demandes de correction conservent leurs consignes et leur cause technique éventuelle.</summary>
    [Fact]
    public void ValidationExceptionsPreserveInstructionsAndTheirCause()
    {
        HookValidationException unspecified = new();
        InvalidOperationException cause = new("Tool failed");
        HookValidationException explained = new("Inspect the output", cause);

        Assert.Contains("corrections", unspecified.Message, StringComparison.Ordinal);
        Assert.Equal("Inspect the output", explained.Message);
        Assert.Same(cause, explained.InnerException);
    }
}
