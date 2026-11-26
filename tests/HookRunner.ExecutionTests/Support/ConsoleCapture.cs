namespace HookRunner.ExecutionTests;

/// <summary>Capture jetable des flux globaux d'entrée et de diagnostic pour les tests sérialisés.</summary>
internal sealed class ConsoleCapture : IDisposable
{
    private readonly TextReader previousInput = Console.In;
    private readonly TextWriter previousError = Console.Error;
    private readonly StringReader input;
    /// <summary>Tampon contenant les diagnostics écrits sur stderr pendant le test.</summary>
    public StringWriter Error { get; } = new();

    /// <summary>
    /// Remplace stdin par une chaîne et stderr par un tampon en mémorisant les flux précédents.
    /// </summary>
    /// <param name="json">Texte présenté au code testé comme entrée standard.</param>
    /// <remarks>
    /// Les flux globaux doivent être restaurés par Dispose ; les tests concernés s'exécutent sans
    /// parallélisme.
    /// </remarks>
    public ConsoleCapture(string json)
    {
        input = new StringReader(json);
        Console.SetIn(input);
        Console.SetError(Error);
    }

    /// <summary>
    /// Restaure les flux standard précédents puis libère le lecteur et le tampon de diagnostic temporaires.
    /// </summary>
    public void Dispose()
    {
        Console.SetIn(previousInput);
        Console.SetError(previousError);
        input.Dispose();
        Error.Dispose();
    }
}
