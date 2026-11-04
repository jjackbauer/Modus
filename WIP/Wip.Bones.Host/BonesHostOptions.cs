using Wip.Bones.Agents.Knowledge;
using Wip.Bones.Agents.Ponder;
using Wip.Bones.Agents.Workflow;
using Wip.Bones.Engine;
using Wip.Bones.Identifiers;
using Wip.Bones.ModelProviders.DeepSeek;
using Wip.Runtime.Runtime;

namespace Wip.Bones.Host;

public enum BonesModelProviderKind
{
    DeepSeek,
    Stub,
}

public sealed class BonesHostDeepSeekConfiguration
{
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string Model { get; set; } = BonesDeepSeekModelIdentifier.Flash;

    public int TimeoutSeconds { get; set; } = 30;

    public string ApiKeySource { get; set; } = "env:DEEPSEEK_API_KEY";

    public string Thinking { get; set; } = "disabled";
}

public sealed class BonesHostModelProviderDiagnostics
{
    public required string Provider { get; init; }

    public string? Model { get; init; }

    public string? BaseUrl { get; init; }

    public int? TimeoutSeconds { get; init; }

    public string? ApiKeySource { get; init; }

    public string? Thinking { get; init; }
}

public sealed class BonesHostOptions
{
    public const string SectionName = "BonesHost";

    public string DataDirectory { get; set; } = "bones-data";

    public Uri ListenUrl { get; set; } = new("http://127.0.0.1:8080");

    public int IterationDelayMs { get; set; } = 100;

    public int MaxGamesPerRun { get; set; }

    public bool AllowUnbounded { get; set; }

    public int MaxConsecutiveIterationFailures { get; set; } = 5;

    public int FailureBackoffBaseMs { get; set; } = 1000;

    public int FailureBackoffMaxMs { get; set; } = 60000;

    public bool ResumeFromBest { get; set; } = true;

    public bool AllSeatsLearning { get; set; } = true;

    public BonesModelProviderKind ModelProvider { get; set; } = BonesModelProviderKind.DeepSeek;

    public BonesHostDeepSeekConfiguration DeepSeek { get; set; } = new();

    public BonesLearningWorkflowParameters DefaultParameters { get; set; } = new(
        GameCount: 8,
        Seed: 4242,
        TargetScore: 120,
        LearningPlayerId: new BonesPlayerId(1),
        EvaluationTargetScore: 12);

    public int ParallelSessionCount { get; set; } = 8;

    public bool RequireLibraryBootstrap { get; set; } = false;

    public BonesCompileRetryOptions Ponder { get; set; } = new();

    public string? PonderModelId { get; set; }

    public string? PlayModelId { get; set; }

    public string? EnhanceModelId { get; set; }

    public int? EvaluationTargetScore { get; set; } = 12;

    public BonesStrategyPromotionOptions Promotion { get; set; } = new();

    public BonesStrategyScriptHostOptions ScriptHost { get; set; } = new();

    public BonesAggressiveIterationOptions AggressiveIteration { get; set; } = new();

    public PopulationPoolOptions Population { get; set; } = PopulationPoolOptions.Default;

    public BonesDeepSeekProviderOptions? DeepSeekProviderOptions { get; private set; }

    public BonesDeepSeekResilienceOptions Resilience { get; set; } = new();

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
        options.AllowUnbounded = ResolveAllowUnbounded(configuration, options.AllowUnbounded);
        options.MaxConsecutiveIterationFailures = ResolvePositiveInt(
            configuration,
            $"{SectionName}:MaxConsecutiveIterationFailures",
            "BonesHost__MaxConsecutiveIterationFailures",
            options.MaxConsecutiveIterationFailures);
        options.FailureBackoffBaseMs = ResolvePositiveInt(
            configuration,
            $"{SectionName}:FailureBackoffBaseMs",
            "BonesHost__FailureBackoffBaseMs",
            options.FailureBackoffBaseMs);
        options.FailureBackoffMaxMs = ResolvePositiveInt(
            configuration,
            $"{SectionName}:FailureBackoffMaxMs",
            "BonesHost__FailureBackoffMaxMs",
            options.FailureBackoffMaxMs);
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
        options.EvaluationTargetScore = ResolveEvaluationTargetScore(configuration, options.EvaluationTargetScore);
        ResolveResilienceOptions(configuration, options.Resilience);
        options.Ponder = ResolveCompileRetryOptions(configuration, options.Ponder);
        options.Promotion = ResolvePromotionOptions(configuration, options.Promotion);
        options.ScriptHost = ResolveScriptHostOptions(configuration, options.ScriptHost);
        options.AggressiveIteration = ResolveAggressiveIterationOptions(configuration, options.AggressiveIteration);
        options.Population = ResolvePopulationOptions(configuration, options.Population);
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
            EvaluationTargetScore = options.EvaluationTargetScore,
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
            $"allowUnbounded={AllowUnbounded.ToString().ToLowerInvariant()}",
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
            diagnostics.Add($"modelProvider.deepseek.thinking={deepSeek.Thinking.ToString().ToLowerInvariant()}");
        }

        if (EvaluationTargetScore is > 0)
            diagnostics.Add($"evaluationTargetScore={EvaluationTargetScore.Value}");

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
            Thinking = deepSeek.Thinking.ToString().ToLowerInvariant(),
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

        var temperature = ResolveDeepSeekTemperature(configuration);
        var thinking = ParseDeepSeekThinkingMode(DeepSeek.Thinking);

        DeepSeekProviderOptions = BonesDeepSeekProviderOptions.Create(
            DeepSeek.BaseUrl,
            DeepSeek.Model,
            TimeSpan.FromSeconds(DeepSeek.TimeoutSeconds),
            DeepSeek.ApiKeySource,
            temperature,
            thinking);

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

        var thinking = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Thinking")
            ?? deepSeekSection["Thinking"]
            ?? DeepSeek.Thinking;

        DeepSeek = new BonesHostDeepSeekConfiguration
        {
            BaseUrl = baseUrl,
            Model = model,
            TimeoutSeconds = timeoutSeconds,
            ApiKeySource = apiKeySource,
            Thinking = thinking,
        };
    }

    private static DeepSeekThinkingMode ParseDeepSeekThinkingMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DeepSeekThinkingMode.Disabled;

        if (Enum.TryParse<DeepSeekThinkingMode>(value.Trim(), ignoreCase: true, out var thinking)
            && Enum.IsDefined(thinking))
        {
            return thinking;
        }

        throw new InvalidOperationException(
            $"Bones host configuration is invalid: DeepSeek Thinking '{value}' must be 'default', 'enabled', or 'disabled'.");
    }

    private static double? ResolveDeepSeekTemperature(IConfiguration configuration)
    {
        var deepSeekSection = configuration.GetSection($"{SectionName}:DeepSeek");
        var temperatureRaw = Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Temperature")
            ?? deepSeekSection["Temperature"];

        if (double.TryParse(temperatureRaw, out var temperature))
            return temperature;

        return null;
    }

    private bool HasDeepSeekEnvironmentOverrides()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__BaseUrl"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Model"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__TimeoutSeconds"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__ApiKeySource"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Temperature"))
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BonesHost__DeepSeek__Thinking"));

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

    private static int ResolvePositiveInt(
        IConfiguration configuration,
        string configurationKey,
        string environmentKey,
        int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(environmentKey)
            ?? configuration[configurationKey];

        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!int.TryParse(raw, out var value) || value < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: '{configurationKey}' must be a positive integer.");
        }

        return value;
    }

    private static int? ResolveEvaluationTargetScore(IConfiguration configuration, int? fallback)
    {
        var raw = Environment.GetEnvironmentVariable("BONES_EVALUATION_TARGET_SCORE")
            ?? Environment.GetEnvironmentVariable("BonesHost__EvaluationTargetScore")
            ?? configuration[$"{SectionName}:EvaluationTargetScore"]
            ?? configuration[$"{SectionName}:DefaultParameters:EvaluationTargetScore"];

        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        if (!int.TryParse(raw, out var value) || value < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: EvaluationTargetScore '{raw}' must be a positive integer.");
        }

        return value;
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

    private static bool ResolveAllowUnbounded(IConfiguration configuration, bool fallback)
    {
        var dedicatedEnv = Environment.GetEnvironmentVariable("BONES_ALLOW_UNBOUNDED");
        if (!string.IsNullOrWhiteSpace(dedicatedEnv)
            && bool.TryParse(dedicatedEnv, out var dedicatedValue))
        {
            return dedicatedValue;
        }

        var doubleUnderscoreEnv = Environment.GetEnvironmentVariable("BonesHost__AllowUnbounded");
        if (!string.IsNullOrWhiteSpace(doubleUnderscoreEnv)
            && bool.TryParse(doubleUnderscoreEnv, out var envValue))
        {
            return envValue;
        }

        return configuration.GetValue<bool?>($"{SectionName}:AllowUnbounded") ?? fallback;
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

    private static BonesCompileRetryOptions ResolveCompileRetryOptions(
        IConfiguration configuration,
        BonesCompileRetryOptions fallback)
    {
        var ponderSection = configuration.GetSection($"{SectionName}:Ponder");
        var maxCompileRetries = fallback.MaxCompileRetries;

        if (int.TryParse(
                Environment.GetEnvironmentVariable("BonesHost__Ponder__MaxCompileRetries"),
                out var envRetries)
            || int.TryParse(ponderSection[BonesCompileRetryOptions.MaxCompileRetriesConfigurationKey], out envRetries))
        {
            maxCompileRetries = envRetries;
        }

        if (maxCompileRetries < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: Compile MaxCompileRetries '{maxCompileRetries}' must be at least 1.");
        }

        return new BonesCompileRetryOptions
        {
            MaxCompileRetries = maxCompileRetries,
        };
    }

    private static BonesStrategyPromotionOptions ResolvePromotionOptions(
        IConfiguration configuration,
        BonesStrategyPromotionOptions fallback)
    {
        var section = configuration.GetSection($"{SectionName}:Promotion");
        var result = new BonesStrategyPromotionOptions
        {
            EvaluationMatchCount = section.GetValue<int?>("EvaluationMatchCount")
                ?? fallback.EvaluationMatchCount,
            MaxConcurrentMatches = section.GetValue<int?>("MaxConcurrentMatches")
                ?? fallback.MaxConcurrentMatches,
            MinimumWinRateImprovement = section.GetValue<double?>("MinimumWinRateImprovement")
                ?? fallback.MinimumWinRateImprovement,
            MinimumScoreDifferentialImprovement = section.GetValue<int?>("MinimumScoreDifferentialImprovement")
                ?? fallback.MinimumScoreDifferentialImprovement,
            MinimumCrossSeatCount = section.GetValue<int?>("MinimumCrossSeatCount")
                ?? fallback.MinimumCrossSeatCount,
            UsePairedEvaluationSeeds = section.GetValue<bool?>("UsePairedEvaluationSeeds")
                ?? fallback.UsePairedEvaluationSeeds,
            ConfidenceZThreshold = section.GetValue<double?>("ConfidenceZThreshold")
                ?? fallback.ConfidenceZThreshold,
            WinRateHardFloor = section.GetValue<double?>("WinRateHardFloor")
                ?? fallback.WinRateHardFloor,
            ScoreDifferentialHardFloor = section.GetValue<double?>("ScoreDifferentialHardFloor")
                ?? fallback.ScoreDifferentialHardFloor,
        };

        if (Enum.TryParse<BonesPromotionGateMode>(section["GateMode"], ignoreCase: true, out var gateMode))
            result.GateMode = gateMode;

        if (result.EvaluationMatchCount < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: Promotion EvaluationMatchCount '{result.EvaluationMatchCount}' must be at least 1.");
        }

        if (result.MaxConcurrentMatches < 1)
        {
            throw new InvalidOperationException(
                $"Bones host configuration is invalid: Promotion MaxConcurrentMatches '{result.MaxConcurrentMatches}' must be at least 1.");
        }

        return result;
    }

    private static BonesStrategyScriptHostOptions ResolveScriptHostOptions(
        IConfiguration configuration,
        BonesStrategyScriptHostOptions fallback)
    {
        var section = configuration.GetSection($"{SectionName}:ScriptHost");

        // ScriptHost.Mode is validated by the configuration binder in Bind(): an unknown value
        // fails startup deterministically instead of silently falling back to InProcess.
        var result = new BonesStrategyScriptHostOptions
        {
            CompilationTimeout = TimeSpan.TryParse(section["CompilationTimeout"], out var compileTimeout)
                ? compileTimeout
                : fallback.CompilationTimeout,
            ExecutionTimeout = TimeSpan.TryParse(section["ExecutionTimeout"], out var execTimeout)
                ? execTimeout
                : fallback.ExecutionTimeout,
            MaxCompiledScripts = section.GetValue<int?>("MaxCompiledScripts")
                ?? fallback.MaxCompiledScripts,
            EnableDirectExecutionTimeout = section.GetValue<bool?>("EnableDirectExecutionTimeout")
                ?? fallback.EnableDirectExecutionTimeout,
            Mode = fallback.Mode,
            TrustedOnly = section.GetValue<bool?>("TrustedOnly")
                ?? fallback.TrustedOnly,
            WorkerPath = string.IsNullOrWhiteSpace(section["WorkerPath"])
                ? fallback.WorkerPath
                : section["WorkerPath"],
        };

        return result;
    }

    private static BonesAggressiveIterationOptions ResolveAggressiveIterationOptions(
        IConfiguration configuration,
        BonesAggressiveIterationOptions fallback)
    {
        var section = configuration.GetSection($"{SectionName}:AggressiveIteration");
        var result = new BonesAggressiveIterationOptions
        {
            GameCountMultiplier = section.GetValue<double?>("GameCountMultiplier")
                ?? fallback.GameCountMultiplier,
            TargetScoreMultiplier = section.GetValue<double?>("TargetScoreMultiplier")
                ?? fallback.TargetScoreMultiplier,
            ExplorationTemperature = section.GetValue<double?>("ExplorationTemperature")
                ?? fallback.ExplorationTemperature,
            EnableCrossSeatTesting = section.GetValue<bool?>("EnableCrossSeatTesting")
                ?? fallback.EnableCrossSeatTesting,
            EnableSeatRotation = section.GetValue<bool?>("EnableSeatRotation")
                ?? fallback.EnableSeatRotation,
            ConcurrentGamesPerSession = section.GetValue<int?>("ConcurrentGamesPerSession")
                ?? fallback.ConcurrentGamesPerSession,
            EnhanceIterationsPerMatch = section.GetValue<int?>("EnhanceIterationsPerMatch")
                ?? fallback.EnhanceIterationsPerMatch,
            MinimumDistinctStrategiesPerSeat = section.GetValue<int?>("MinimumDistinctStrategiesPerSeat")
                ?? fallback.MinimumDistinctStrategiesPerSeat,
            CrossSeatRotation = section.GetValue<bool?>("CrossSeatRotation")
                ?? fallback.CrossSeatRotation,
            OpponentSourceExcerptMaxLength = section.GetValue<int?>("OpponentSourceExcerptMaxLength")
                ?? fallback.OpponentSourceExcerptMaxLength,
            PerSessionBudget = section.GetValue<bool?>("PerSessionBudget")
                ?? fallback.PerSessionBudget,
        };

        return result;
    }

    private static PopulationPoolOptions ResolvePopulationOptions(
        IConfiguration configuration,
        PopulationPoolOptions fallback)
    {
        var section = configuration.GetSection($"{SectionName}:Population");
        var poolSize = section.GetValue<int?>("PoolSize")
            ?? fallback.PoolSize;
        var maxPopulation = section.GetValue<int?>("MaxPopulation")
            ?? fallback.MaxPopulation;

        // Validate via constructor (it validates poolSize >= 4 and maxPopulation >= poolSize)
        if (poolSize >= 4 && maxPopulation >= poolSize)
            return new PopulationPoolOptions(poolSize, maxPopulation);

        return fallback;
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

    private static void ResolveResilienceOptions(IConfiguration configuration, BonesDeepSeekResilienceOptions options)
    {
        var section = configuration.GetSection($"{SectionName}:Resilience");
        if (!section.Exists())
            return;

        options.MaxRetries = section.GetValue<int?>("MaxRetries") ?? options.MaxRetries;
        options.ExponentialBackoffBaseMs = section.GetValue<int?>("ExponentialBackoffBaseMs") ?? options.ExponentialBackoffBaseMs;
        options.CircuitBreakThreshold = section.GetValue<int?>("CircuitBreakThreshold") ?? options.CircuitBreakThreshold;
        options.CircuitBreakDurationSeconds = section.GetValue<int?>("CircuitBreakDurationSeconds") ?? options.CircuitBreakDurationSeconds;
        options.MaxConcurrentCalls = section.GetValue<int?>("MaxConcurrentCalls") ?? options.MaxConcurrentCalls;
        options.MaxQueueDepth = section.GetValue<int?>("MaxQueueDepth") ?? options.MaxQueueDepth;
        options.PreemptiveThrottleThreshold = section.GetValue<int?>("PreemptiveThrottleThreshold") ?? options.PreemptiveThrottleThreshold;
    }
}