using System.Globalization;

namespace HookRunner.ExecutionTests;

internal static class ConsumerProcessObservation
{
    /// <summary>Attend la publication du PID tant que l'invocation reste active, puis lit ce PID.</summary>
    /// <param name="path">Fichier d'observation publié par le hook pour signaler qu'il est prêt.</param>
    /// <param name="execution">Tâche d'invocation dont la fin interrompt l'attente du fichier.</param>
    /// <param name="cancellationToken">Annulation de l'attente et de la lecture.</param>
    /// <returns>Une tâche donnant le PID publié, interprété en culture invariante.</returns>
    /// <exception cref="InvalidOperationException">L'invocation termine avant de publier son PID.</exception>
    public static async Task<int> WaitForProcessIdAsync(string path, Task execution, CancellationToken cancellationToken)
    {
        while (!File.Exists(path) && !execution.IsCompleted)
        {
            await Task.Delay(20, cancellationToken);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Consumer completed before publishing its process ID.");
        }

        return int.Parse(await File.ReadAllTextAsync(path, cancellationToken), CultureInfo.InvariantCulture);
    }
}
