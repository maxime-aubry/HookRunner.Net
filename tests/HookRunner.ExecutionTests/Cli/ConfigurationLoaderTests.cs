using System.Text.Json;
using HookRunner.Cli;

namespace HookRunner.ExecutionTests;

/// <summary>Règles de chargement vérifiées sur de vrais fichiers dans des dossiers propres à chaque scénario.</summary>
public sealed class ConfigurationLoaderTests
{
    /// <summary>Une section absente ou vide conserve les défauts et laisse le chemin à la préparation.</summary>
    /// <param name="settings">Configuration sans valeurs HookRunner explicites.</param>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"HookRunner\":{}}")]
    public void MissingSettingsUseDefaults(string settings)
    {
        using SolutionFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), settings);

        HookRunnerConfiguration configuration = HookRunnerConfigurationLoader.Load(fixture.Root);

        Assert.Equal(120, configuration.TimeoutSeconds);
        Assert.Equal(3, configuration.MaxBlockingRequests);
        Assert.Equal(string.Empty, configuration.StateFilePath);
        Assert.Empty(configuration.HookLimits);
    }

    /// <summary>Les valeurs globales sont lues dans le dossier fourni sans résoudre ni créer le fichier d'état.</summary>
    /// <param name="timeout">Budget positif configuré, y compris les bornes Int32.</param>
    /// <param name="limit">Plafond non négatif configuré, y compris zéro et la borne Int32.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(37, 4)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void ConfiguredValuesAreReadWithoutPreparingState(int timeout, int limit)
    {
        using SolutionFixture fixture = new();
        string settings = JsonSerializer.Serialize(new
        {
            HookRunner = new { TimeoutSeconds = timeout, StateFilePath = "Hooks/state.json", MaxBlockingRequests = limit },
        });
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), settings);

        HookRunnerConfiguration configuration = HookRunnerConfigurationLoader.Load(fixture.Root);

        Assert.Equal(timeout, configuration.TimeoutSeconds);
        Assert.Equal(limit, configuration.MaxBlockingRequests);
        Assert.Equal("Hooks/state.json", configuration.StateFilePath);
        Assert.Empty(configuration.HookLimits);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Hooks")));
    }

    /// <summary>Seuls les plafonds explicites sont collectés, avec des noms insensibles à la casse.</summary>
    [Fact]
    public void HookLimitsKeepCaseInsensitiveNamesAndOnlyExplicitOverrides()
    {
        using SolutionFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), """
            {
              "HookRunner": {
                "MaxBlockingRequests": 4,
                "Hooks": {
                  "INSPECT": { "MaxBlockingRequests": 0 },
                  "Other": { "MaxBlockingRequests": "2147483647" },
                  "Inherited": {}
                }
              }
            }
            """);

        HookRunnerConfiguration configuration = HookRunnerConfigurationLoader.Load(fixture.Root);

        Assert.Equal(4, configuration.MaxBlockingRequests);
        Assert.Equal(2, configuration.HookLimits.Count);
        Assert.Equal(0, configuration.HookLimits["inspect"]);
        Assert.Equal(int.MaxValue, configuration.HookLimits["OTHER"]);
        Assert.False(configuration.HookLimits.ContainsKey("Inherited"));
    }

    /// <summary>Un timeout explicite doit être un entier positif sans signe ni séparateur.</summary>
    /// <param name="value">Valeur configurée invalide.</param>
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.5")]
    [InlineData("1e2")]
    [InlineData("invalid")]
    [InlineData("2147483648")]
    public void InvalidTimeoutIsRejected(string value)
    {
        using SolutionFixture fixture = new();
        string settings = JsonSerializer.Serialize(new { HookRunner = new { TimeoutSeconds = value } });
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), settings);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => HookRunnerConfigurationLoader.Load(fixture.Root));

        Assert.Equal("TimeoutSeconds must be a positive integer.", exception.Message);
    }

    /// <summary>Un plafond mal formé est refusé aussi bien au niveau global que pour un hook non sélectionné.</summary>
    /// <param name="value">Valeur configurée invalide.</param>
    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1.5")]
    [InlineData("1e2")]
    [InlineData("invalid")]
    [InlineData("2147483648")]
    public void InvalidGlobalAndHookLimitsAreRejected(string value)
    {
        using SolutionFixture fixture = new();
        string[] configurations =
        [
            JsonSerializer.Serialize(new { HookRunner = new { MaxBlockingRequests = value } }),
            JsonSerializer.Serialize(new { HookRunner = new { Hooks = new { unselected = new { MaxBlockingRequests = value } } } }),
        ];
        foreach (string settings in configurations)
        {
            File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), settings);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => HookRunnerConfigurationLoader.Load(fixture.Root));

            Assert.Equal("MaxBlockingRequests must be a non-negative Int32 integer.", exception.Message);
        }
    }

    /// <summary>Un hook ne peut pas redéfinir le chemin d'état, même avec une chaîne vide.</summary>
    /// <param name="path">Chemin d'état interdit dans une surcharge.</param>
    [Theory]
    [InlineData("")]
    [InlineData("other.json")]
    public void HookStateFilePathOverrideIsRejected(string path)
    {
        using SolutionFixture fixture = new();
        string settings = JsonSerializer.Serialize(new
        {
            HookRunner = new { Hooks = new { unselected = new { StateFilePath = path } } },
        });
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), settings);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => HookRunnerConfigurationLoader.Load(fixture.Root));

        Assert.Equal("HookRunner:StateFilePath is global and cannot be overridden per hook.", exception.Message);
    }

    /// <summary>Le fichier reste obligatoire dans le dossier fourni.</summary>
    [Fact]
    public void MissingConfigurationFileIsRejected()
    {
        using SolutionFixture fixture = new();

        Assert.Throws<FileNotFoundException>(() => HookRunnerConfigurationLoader.Load(fixture.Root));
    }

    /// <summary>Un fichier JSON invalide conserve le diagnostic et l'exception d'origine du provider.</summary>
    [Fact]
    public void InvalidJsonIsRejectedWithItsOriginalCause()
    {
        using SolutionFixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), "invalid JSON");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => HookRunnerConfigurationLoader.Load(fixture.Root));

        Assert.Contains("Failed to load configuration", exception.Message, StringComparison.Ordinal);
        Assert.IsType<FormatException>(exception.InnerException);
    }
}
