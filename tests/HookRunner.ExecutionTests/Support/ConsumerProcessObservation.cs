using System.Globalization;

namespace HookRunner.ExecutionTests;

internal static class ConsumerProcessObservation
{
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
