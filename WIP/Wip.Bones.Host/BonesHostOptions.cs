using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;

namespace Wip.Bones.Host;

public enum BonesModelProviderKind
{
    DeepSeek,
    Stub,
}

public sealed class BonesHostDeepSeekConfiguration
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string Model { get; set; } = "deepseek-chat";

    public int TimeoutSeconds { get; set; } = 30;

    public string ApiKeySource { get; set; } = "env:DEEPSEEK_API_KEY";
}

public sealed class BonesHostModelProviderDiagnostics
{
    public required string Provider { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int? TimeoutSeconds { get; init; }

    public string? ApiKeySource { get; init; }
}

public sealed class BonesHostOptions
{
    public const string SectionName = "BonesHost";

    public string DataDirectory { get; set; } = "bones-data";

    public Uri ListenUrl { get; set; } = new("http://127.0.0.1:8080");

    public int IterationDelayMs { get; set; } = 1000;

    public int MaxGamesPerRun { get; set; }

    public bool ResumeFromBest { get; set; } = true;

    public bool AllSeatsLearning { get; set; } = true;

    public BonesModelProviderKind ModelProvider { get; set; } = BonesModelProviderKind.DeepSeek;

    public BonesHostDeepSeekConfiguration DeepSeek { get; set; } = new();

    public BonesLearningWorkflowParameters DefaultParameters { get; set; } = new(
        GameCount: 1,
        Seed: 4242,
        TargetScore: 10,
        LearningPlayerId: new BonesPlayerId(1));

    public int ParallelSessionCount { get; set; } = 4;

    public bool RequireLibraryBootstrap { get; set; } = false;

    public BonesPonderCompileRetryOptions Ponder { get; set; } = new();

    public string? PonderModelId { get; set; }

    public string? PlayModelId { get; set; }

    public string? EnhanceModelId { get; set; }

    public BonesDeepSeekProviderOptions? DeepSeekProviderOptions { get; private set; }

    public static BonesHostOptions Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new BonesHostOptions();
        configuration.GetSection(SectionName).Bind(options);

        var listenUrl = configuration[$"{SectionName}:ListenUrl"]
            ?? configuration["ASPNETCORE_URLS"]
            ?? Environment.GetEnvironmentVariable("BonesHost__ListenUrl")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (!string.IsNullOrWhiteSpace(listenUrl))
            options.ListenUrl = new Uri(listenUrl, UriKind.Absolute);

        var dataDirectory = configuration[$"{SectionName}:DataDirectory"]
            ?? Environment.GetEnvironmentVariable("BonesHost__DataDirectory");
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            options.DataDirectory = dataDirectory;

        if (int.TryParse(configuration[$"{SectionName}:IterationDelayMs"], out var iterationDelayMs)
            || int.TryParse(configuration["BonesHost__IterationDelayMs"], out iterationDelayMs))
        {
            options.IterationDelayMs = iterationDelayMs;
        }

        options.MaxGamesPerRun = ResolveMaxGamesPerRun(configuration, options.MaxGamesPerRun);
        options.ResumeFromBest = ResolveResumeFromBest(configuration, options.ResumeFromBest);
        options.AllSeatsLearning = ResolveAllSeatsLearning(configuration, options.AllSeatsLearning);

        options.ModelProvider = ParseModelProvider(
            configuration[$"{SectionName}:ModelProvider"]
            ?? Environment.GetEnvironmentVariable("BonesHost__ModelProvider"),
            options.ModelProvider);

        var parametersSection = configuration.GetSection($"{SectionName}:DefaultParameters");
        if (parametersSection.Exists())
        {
            var gameCount = parametersSection.GetValue<int?>("GameCount") ?? options.DefaultParameters.GameCount;
            var seed = parametersSection.GetValue<int?>("Seed") ?? options.DefaultParameters.Seed;
            var targetScore = parametersSection.GetValue<int?>("TargetScore") ?? options.DefaultParameters.TargetScore;
            var learningPlayerId = parametersSection.GetValue<int?>("LearningPlayerId")
                ?? options.DefaultParameters.LearningPlayerId.Seat;

            options.DefaultParameters = new BonesLearningWorkflowParameters(
                gameCount,
                seed,
                targetScore,
                new BonesPlayerId(learningPlayerId));
        }

        options.ApplyModelProviderConfiguration(configuration);
        options.Ponder = ResolvePonderCompileRetryOptions(configuration, options.Ponder);
        options.ParallelSessionCount = ResolveParallelSessionCount(configuration, options.ParallelSessionCount);
        options.RequireLibraryBootstrap = ResolveRequireLibraryBootstrap(configuration, options.RequireLibraryBootstrap);

        // Resolve per-stage model IDs from env vars and config
        options.PonderModelId =
            Environment.GetEnvironmentVariable("BONES_PONDER_MODEL")
            ?? configuration[$"{SectionName}:PonderModelId"];
        options.PlayModelId =
            Environment.GetEnvironmentVariable("BONES_PLAY_MODEL")
            ?? configuration[$"{SectionName}:PlayModelId"];
        options.EnhanceModelId =
            Environment.GetEnvironmentVariable("BONES_ENHANCE_MODEL")
            ?? configuration[$"{SectionName}:EnhanceModelId"];

        // Wire AllSeatsLearning and per-stage model IDs into DefaultParameters
        options.DefaultParameters = options.DefaultParameters with
        {
            AllSeatsLearning = options.AllSeatsLearning,
            PonderModelId = string.IsNullOrWhiteSpace(options.PonderModelId) ? null : options.PonderModelId,
            PlayModelId = string.IsNullOrWhiteSpace(options.PlayModelId) ? null : options.PlayModelId,
            EnhanceModelId = string.IsNullOrWhiteSpace(options.EnhanceModelId) ? null : options.EnhanceModelId,
        };

        return options;
    }

    public IReadOnlyList<string> GetStartupDiagnostics()
    {
        var diagnostics = new List<string>
        {
            $"dataDirectory={DataDirectory}",
            $"listenUrl={ListenUrl}",
            $"iterationDelayMs={IterationDelayMs}",
            $"maxGamesPerRun={MaxGamesPerRun}",
            $"resumeFromBest={ResumeFromBest.ToString().ToLowerInvariant()}",
            $"allSeatsLearning={AllSeatsLearning.ToString().ToLowerInvariant()}",
            $"parallelSessionCount={ParallelSessionCount}",
            $"modelProvider={ModelProvider.ToString().ToLowerInvariant()}",
        };

        if (ModelProvider == BonesModelProviderKind.DeepSeek && DeepSeekProviderOptions is { } deepSeek)
        {
            diagnostics.Add($"modelProvider.deepseek.baseUrl={deepSeek.BaseUrl}");
            diagnostics.Add($"modelProvider.deepseek.model={deepSeek.Model.Value}");
            diagnostics.Add($"modelProvider.deepseek.timeoutSeconds={(int)deepSeek.Timeout.TotalSeconds}");
            diagnostics.Add($"modelProvider.deepseek.apiKeySource=environment:{deepSeek.ApiKeySourceReference}");
        }

        if (!string.IsNullOrWhiteSpace(PonderModelId))
            diagnostics.Add($"ponderModel={PonderModelId}");

        if (!string.IsNullOrWhiteSpace(PlayModelId))
            diagnostics.Add($"playModel={PlayModelId}");

        if (!string.IsNullOrWhiteSpace(EnhanceModelId))
            diagnostics.Add($"enhanceModel={EnhanceModelId}");

        return diagnostics;
    }

    public BonesHostModelProviderDiagnostics GetModelProviderDiagnostics()
    {
        if (ModelProvider == BonesModelProviderKind.Stub)
        {
            return new BonesHostModelProviderDiagnostics
            {
                Provider = "stub",
            };
        }

        var deepSeek = DeepSeekProviderOptions
            ?? throw new InvalidOperationException(
                "Bones host configuration is invalid: DeepSeek provider options are unavailable.");

        return new BonesHostModelProviderDiagnostics
        {
            Provider = "deepseek",
            Model = deepSeek.Model.Value,
            BaseUrl = deepSeek.BaseUrl.ToString(),
            TimeoutSeconds = (int)deepSeek.Timeout.TotalSeconds,
            ApiKeySource = $"environment:{deepSeek.ApiKeySourceReference}",
        };
    }

    private void ApplyModelProviderConfiguration(IConfiguration configuration)
    {
        if (ModelProvider == BonesModelProviderKind.Stub)
            return;

        ResolveDeepSeekConfiguration(configuration);

        var deepSeekSection = configuration.GetSection($"{SectionName}:DeepSeek");
        if (!deepSeekSection.Exists() && !HasDeepSeekEnvironmentOverrides())
        {
            throw new InvalidOperationException(
                "Bones host configuration is invalid: DeepSeek settings are required when ModelProvider is set to 'deepseek'.");
        }

        if (DeepSeek.TimeoutSeconds is < 1 or > 600)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: DeepSeek TimeoutSeconds '{DeepSeek.TimeoutSeconds}' must be between 1 and 600.");
        }

        DeepSeekProviderOptions = BonesDeepSeekProviderOptions.Create(
            DeepSeek.BaseUrl,
            DeepSeek.Model,
            TimeSpan.FromSeconds(DeepSeek.TimeoutSeconds),
            DeepSeek.ApiKeySource);

        DefaultParameters = DefaultParameters with
        {
            ModelId = DeepSeekProviderOptions.Model.Value,
        };
    }

    private void ResolveDeepSeekConfiguration(IConfiguration configuration)
    {
        var deepSeekSection = configuration.GetSection($"{SectionName}:DeepSeek");

        var baseUrl = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl")
            ?? deepSeekSection["BaseUrl"]
            ?? DeepSeek.BaseUrl;

        var model = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Model")
            ?? deepSeekSection["Model"]
            ?? DeepSeek.Model;

        var timeoutRaw = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds")
            ?? deepSeekSection["TimeoutSeconds"];
        var timeoutSeconds = int.TryParse(timeoutRaw, out var parsedTimeout)
            ? parsedTimeout
            : DeepSeek.TimeoutSeconds;

        var apiKeySource = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource")
            ?? deepSeekSection["ApiKeySource"]
            ?? DeepSeek.ApiKeySource;

        DeepSeek = new BonesHostDeepSeekConfiguration
        {
            BaseUrl = baseUrl,
            Model = model,
            TimeoutSeconds = timeoutSeconds,
            ApiKeySource = apiKeySource,
        };
    }

    private bool HasDeepSeekEnvironmentOverrides()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Model"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource"));

    private static BonesModelProviderKind ParseModelProvider(string? value, BonesModelProviderKind fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim().ToLowerInvariant() switch
        {
            "deepseek" => BonesModelProviderKind.DeepSeek,
            "stub" => BonesModelProviderKind.Stub,
            _ => throw new InvalidOperationException(
                "Bones host configuration is invalid: ModelProvider must be 'deepseek' or 'stub'."),
        };
    }

    private static int ResolveMaxGamesPerRun(IConfiguration configuration, int fallback)
    {
        var dedicatedEnv = Environment.GetEnvironmentVariable("BONES_MAX_GAMES_PER_RUN");
        if (int.TryParse(dedicatedEnv, out var dedicatedValue))
            return ValidateMaxGamesPerRun(dedicatedValue);

        var doubleUnderscoreEnv = Environment.GetEnvironmentVariable("BonesHost__MaxGamesPerRun");
        if (int.TryParse(doubleUnderscoreEnv, out var envValue))
            return ValidateMaxGamesPerRun(envValue);

        var configValue = configuration.GetValue<int?>($"{SectionName}:MaxGamesPerRun");
        if (configValue.HasValue)
            return ValidateMaxGamesPerRun(configValue.Value);

        return ValidateMaxGamesPerRun(fallback);
    }

    private static bool ResolveResumeFromBest(IConfiguration configuration, bool fallback)
    {
        var dedicatedEnv = Environment.GetEnvironmentVariable("BONES_RESUME_FROM_BEST");
        if (!string.IsNullOrWhiteSpace(dedicatedEnv)
            && bool.TryParse(dedicatedEnv, out var dedicatedValue))
        {
            return dedicatedValue;
        }

        var doubleUnderscoreEnv = Environment.GetEnvironmentVariable("BonesHost__ResumeFromBest");
        if (!string.IsNullOrWhiteSpace(doubleUnderscoreEnv)
            && bool.TryParse(doubleUnderscoreEnv, out var envValue))
        {
            return envValue;
        }

        return configuration.GetValue<bool?>($"{SectionName}:ResumeFromBest") ?? fallback;
    }

    private static bool ResolveAllSeatsLearning(IConfiguration configuration, bool fallback)
    {
        var dedicatedEnv = Environment.GetEnvironmentVariable("BONES_ALL_SEATS_LEARNING");
        if (!string.IsNullOrWhiteSpace(dedicatedEnv)
            && bool.TryParse(dedicatedEnv, out var dedicatedValue))
        {
            return dedicatedValue;
        }

        var doubleUnderscoreEnv = Environment.GetEnvironmentVariable("BonesHost__AllSeatsLearning");
        if (!string.IsNullOrWhiteSpace(doubleUnderscoreEnv)
            && bool.TryParse(doubleUnderscoreEnv, out var envValue))
        {
            return envValue;
        }

        return configuration.GetValue<bool?>($"{SectionName}:AllSeatsLearning") ?? fallback;
    }

    private static int ValidateMaxGamesPerRun(int value)
    {
        if (value < 0)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: MaxGamesPerRun '{value}' cannot be negative.");
        }

        return value;
    }

    private static BonesPonderCompileRetryOptions ResolvePonderCompileRetryOptions(
        IConfiguration configuration,
        BonesPonderCompileRetryOptions fallback)
    {
        var ponderSection = configuration.GetSection($"{SectionName}:Ponder");
        var maxCompileRetries = fallback.MaxCompileRetries;

        if (int.TryParse(
                Environment.GetEnvironmentVariable("BonesHost__Ponder__MaxCompileRetries"),
                out var envRetries)
            || int.TryParse(ponderSection[BonesPonderCompileRetryOptions.MaxCompileRetriesConfigurationKey], out envRetries))
        {
            maxCompileRetries = envRetries;
        }

        if (maxCompileRetries < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: Ponder MaxCompileRetries '{maxCompileRetries}' must be at least 1.");
        }

        return new BonesPonderCompileRetryOptions
        {
            MaxCompileRetries = maxCompileRetries,
        };
    }

    private static int ResolveParallelSessionCount(IConfiguration configuration, int fallback)
    {
        var dedicatedEnv = Environment.GetEnvironmentVariable("BONES_PARALLEL_SESSIONS");
        if (int.TryParse(dedicatedEnv, out var dedicatedValue))
            return ValidateParallelSessionCount(dedicatedValue);

        var configValue = configuration.GetValue<int?>($"{SectionName}:ParallelSessionCount");
        if (configValue.HasValue)
            return ValidateParallelSessionCount(configValue.Value);

        return ValidateParallelSessionCount(fallback);
    }

    private static int ValidateParallelSessionCount(int value)
    {
        if (value < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: ParallelSessionCount '{value}' must be at least 1.");
        }

        return value;
    }

    private static bool ResolveRequireLibraryBootstrap(IConfiguration configuration, bool fallback)
    {
        var env = Environment.GetEnvironmentVariable("BONES_REQUIRE_LIBRARY_BOOTSTRAP");
        if (!string.IsNullOrWhiteSpace(env) && bool.TryParse(env, out var envValue))
            return envValue;

        return configuration.GetValue<bool?>($"{SectionName}:RequireLibraryBootstrap") ?? fallback;
    }
}