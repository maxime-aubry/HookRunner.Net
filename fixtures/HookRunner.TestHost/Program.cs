using HookRunner.Application;
using HookRunner.Cli;
using HookRunner.Infrastructure;
using HookRunner.TestHost;
using Microsoft.Extensions.DependencyInjection;

string? mode = Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_MODE");
if (args.Length >= 3 && args[0] == "--test-mode")
{
    mode = args[1];
    Environment.SetEnvironmentVariable("HOOKRUNNER_TEST_MODE", mode);
    Environment.SetEnvironmentVariable("HOOKRUNNER_TEST_RESULT", args[2]);
    args = args[3..];
}

if (args.Length == 3 && args[0] == ChildHookProtocol.Command && mode == "invalid-result")
{
    await File.WriteAllTextAsync(args[2], Environment.GetEnvironmentVariable("HOOKRUNNER_TEST_RESULT"));
    return 0;
}

return await HookRunnerHost.RunAsync(args, services =>
{
    services.AddSingleton<ConsumerDependency>();
    services.Scan(scan => scan
        .FromAssemblies(typeof(InspectHook).Assembly)
        .AddClasses(classes => classes.AssignableTo<HookStrategy>())
        .As<HookStrategy>()
        .WithTransientLifetime());
    if (mode == "duplicate")
    {
        services.AddTransient<HookStrategy, InspectHook>();
    }
});
