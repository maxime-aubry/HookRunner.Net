namespace HookRunner.ExecutionTests;

/// <summary>Évite les écritures simultanées de rapports Coverlet à la sortie des consommateurs instrumentés.</summary>
[CollectionDefinition("Processes", DisableParallelization = true)]
public sealed class ProcessTestsGroup;
