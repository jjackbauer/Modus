using Wip.Factory.Host.Demo;
using Wip.Factory.Host.Factory;

namespace Wip.Factory.Host;

/// <summary>
/// Composition root for the factory alpha. A verb that is not implemented exits non-zero rather than
/// reporting success it did not observe.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            await Console.Error.WriteLineAsync("usage: Wip.Factory.Host factory <verb> [args]");
            return 64;
        }

        if (!string.Equals(args[0], "factory", StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync($"unknown command group '{args[0]}'");
            return 64;
        }

        var verb = args.Length > 1 ? args[1] : string.Empty;

        // Operator verbs (v0.3 §8). These are the production path; demos are secondary regression.
        if (verb is "requirement" or "contract" or "run" or "status" or "approve" or "serve" or "template" or "acceptance")
        {
            return await FactoryCli.ExecuteAsync(args, Console.Out, Console.Error);
        }

        if (string.Equals(verb, "demo", StringComparison.Ordinal))
        {
            var demo = args.Length > 2 ? args[2] : string.Empty;

            var output = args.Length > 3
                ? args[3]
                : Path.Combine(Path.GetTempPath(), "wip-factory-demo-" + Guid.NewGuid().ToString("N"));

            if (string.Equals(demo, "requirement-to-merge", StringComparison.Ordinal))
            {
                return await new RequirementToMergeDemo().RunAsync(output);
            }

            if (string.Equals(demo, "nine-role-dispatch", StringComparison.Ordinal))
            {
                return await new NineRoleDispatchDemo().RunAsync(output);
            }

            if (string.Equals(demo, "real-model-role", StringComparison.Ordinal))
            {
                return await new RealModelRoleDemo().RunAsync(output);
            }

            if (string.Equals(demo, "improvement-cycle", StringComparison.Ordinal))
            {
                return await new ImprovementCycleDemo().RunAsync(output);
            }

            if (string.Equals(demo, "analysis-comparison", StringComparison.Ordinal))
            {
                return await new AnalysisComparisonDemo().RunAsync(output);
            }

            if (string.Equals(demo, "admission-lifecycle", StringComparison.Ordinal))
            {
                return await new AdmissionLifecycleDemo().RunAsync(output);
            }

            if (string.Equals(demo, "todo-app", StringComparison.Ordinal))
            {
                return await new TodoAppDemo().RunAsync(output);
            }

            await Console.Error.WriteLineAsync($"unknown demo '{demo}'");
            return 64;
        }

        await Console.Error.WriteLineAsync($"factory verb '{verb}' is not implemented in this increment");
        return 70;
    }
}
