using System.Globalization;
using System.Text.Json;
using HookRunner.Application;
using HookRunner.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HookRunner.Cli;

public static class HookRunnerHost
{
    private static readonly JsonSerializerOptions ChildJsonOptions = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static async Task<int> RunAsync(
        string[] args,
        Action<IServiceCollection> configureServices,
        ChildProcessCommand? childProcess = null,
        CancellationToken cancellationToken = default)
    {
        int exitCode;
        try
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(configureServices);
            ServiceCollection services = new();
            services.AddSingleton<IIsolatedHookExecutor>(new ProcessHookExecutor(childProcess ?? ChildProcessCommand.ForCurrentProcess()));
            services.AddTransient(provider => new Context(provider.GetRequiredService<IIsolatedHookExecutor>(), Console.Error));
            services.AddTransient<RunHookUseCase>();
            services.AddSingleton<IHookOptionsPreparer>(_ => LoadOptionsPreparer());
            configureServices(services);
            await using ServiceProvider provider = services.BuildServiceProvider();
            if (args.Length > 0 && args[0] == ChildHookProtocol.Command)
            {
                exitCode = await ExecuteChildAsync(args, provider, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                RunHookArgs parsed = ParseArguments(args);
                string json = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                InvocationData invocation = CodexEventAdapter.Parse(json);
                RunHookRequest request = new(parsed, invocation);
                RunHookResponse response = await provider.GetRequiredService<RunHookUseCase>()
                    .ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
                exitCode = response.ExitCode;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HookRunner: {exception.Message}");
            return 2;
        }

        return exitCode;
    }

    private static HookOptionsPreparer LoadOptionsPreparer()
    {
        using ConfigurationRoot configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();
        string? timeoutValue = configuration["HookRunner:TimeoutSeconds"];
        int timeout = timeoutValue is null ? 120 : ParseTimeout(timeoutValue);
        string state = configuration["HookRunner:StateFilePath"] ?? string.Empty;
        return new HookOptionsPreparer(timeout, state);
    }

    private static RunHookArgs ParseArguments(string[] args)
    {
        if (args.Length < 2 || args[0] != "execute" || string.IsNullOrWhiteSpace(args[1]) || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Usage: execute <hook-name> --agent codex [--timeout <seconds>].");
        }

        Dictionary<string, string> options = [with(StringComparer.Ordinal)];
        for (int index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !options.TryAdd(args[index], args[index + 1]))
            {
                throw new ArgumentException($"Missing or repeated option: '{args[index]}'.");
            }

            if (args[index] is not ("--agent" or "--timeout" or "--projects"))
            {
                throw new ArgumentException($"Unknown option: '{args[index]}'.");
            }
        }

        if (!options.TryGetValue("--agent", out string? agent) || agent != "codex")
        {
            throw new ArgumentException("An explicit --agent codex is required.");
        }

        int? timeout = options.TryGetValue("--timeout", out string? value) ? ParseTimeout(value) : null;
        string[] projects = options.TryGetValue("--projects", out string? selection)
            ? selection.Split(',', StringSplitOptions.TrimEntries)
            : [];
        return new RunHookArgs(args[1], timeout, projects);
    }

    private static int ParseTimeout(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int timeout) || timeout <= 0)
        {
            throw new ArgumentException("TimeoutSeconds must be a positive integer.");
        }

        return timeout;
    }

    private static async Task<int> ExecuteChildAsync(string[] args, IServiceProvider provider, CancellationToken cancellationToken)
    {
        if (args.Length != 3)
        {
            throw new ArgumentException("Invalid hook child arguments.");
        }

        ChildHookResult result;
        try
        {
            string json = await File.ReadAllTextAsync(args[1], cancellationToken).ConfigureAwait(false);
            IsolatedHookRequest request = JsonSerializer.Deserialize<IsolatedHookRequest>(json, ChildJsonOptions)
                ?? throw new InvalidDataException("Invalid hook child request.");
            HookStrategy strategy = HookSelection.Select(provider.GetServices<HookStrategy>(), request.HookName);
            await strategy.ExecuteAsync(request.Invocation, request.Options, cancellationToken).ConfigureAwait(false);
            result = new ChildHookResult(ChildHookProtocol.Version, HookExecutionStatus.Success, null);
        }
        catch (Exception exception)
        {
            result = new ChildHookResult(ChildHookProtocol.Version, HookExecutionStatus.TechnicalFailure, exception.Message);
        }

        await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(result), cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
