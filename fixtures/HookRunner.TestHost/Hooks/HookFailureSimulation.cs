using System.Globalization;
using HookRunner.Domain;

namespace HookRunner.TestHost;

/// <summary>Simule les modes de panne du consommateur indépendamment de l'observation du hook.</summary>
internal static class HookFailureSimulation
{
    /// <summary>Applique l'attente, la sortie brutale ou l'exception demandée par le scénario de test.</summary>
    /// <param name="mode">Mode de simulation ; une valeur inconnue ou null laisse le hook continuer.</param>
    /// <param name="output">Fichier dans lequel publier le PID avant une attente simulée.</param>
    /// <param name="cancellationToken">Annulation de la publication du PID et de l'attente coopérative.</param>
    /// <returns>Une tâche terminée pour un mode sans incident, annulée ou en échec selon la simulation.</returns>
    /// <exception cref="InvalidOperationException">Le mode technical-failure demande une panne technique.</exception>
    /// <remarks>Le mode unexpected-exit termine immédiatement le processus avec le code 23.</remarks>
    public static async Task ApplyAsync(string? mode, string output, CancellationToken cancellationToken)
    {
        switch (mode)
        {
            case "wait":
            case "uncooperative-wait":
                await WaitAsync(mode, output, cancellationToken);
                break;
            case "unexpected-exit":
                Environment.Exit(23);
                break;
            case "technical-failure":
                throw new InvalidOperationException("Consumer technical failure.");
            case "validation-failure":
                throw new HookValidationException("Consumer correction required.");
            case "validation-failure-message":
                throw new HookValidationException(Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_RESULT") ?? string.Empty);
        }
    }

    /// <summary>Publie le PID par renommage d'un fichier prêt, puis attend indéfiniment.</summary>
    /// <param name="mode">uncooperative-wait bloque le thread sans observer l'annulation ; wait reste annulable.</param>
    /// <param name="output">Chemin final du fichier contenant le PID.</param>
    /// <param name="cancellationToken">Annulation de l'écriture préparatoire et de l'attente coopérative.</param>
    /// <returns>Une tâche qui ne termine que par annulation ou erreur ; le mode non coopératif exige l'arrêt du processus.</returns>
    private static async Task WaitAsync(string mode, string output, CancellationToken cancellationToken)
    {
        string ready = output + ".ready";
        await File.WriteAllTextAsync(ready, Environment.ProcessId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        File.Move(ready, output);
        if (mode == "uncooperative-wait")
        {
            Thread.Sleep(Timeout.Infinite);
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
