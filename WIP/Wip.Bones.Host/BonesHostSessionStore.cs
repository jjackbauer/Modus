using Wip.Bones.Agents.Workflow;

namespace Wip.Bones.Host;

public sealed record BonesLoopIterationContext(
    long IterationNumber,
    int DerivedSeed,
    string TaskDescription,
    string RepositoryPath,
    string WorktreePath);

public sealed class BonesHostSessionStore
{
    public string HostSessionId { get; } = Guid.NewGuid().ToString("N");

    public BonesLoopIterationContext BeginIteration(
        long iterationNumber,
        BonesLearningWorkflowParameters parameters,
        string repositoryPath)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var derivedSeed = HashCode.Combine(parameters.Seed, (int)iterationNumber);
        var taskDescription = BonesLearningWorkflowParameters.FormatTaskDescription(
            parameters.GameCount,
            derivedSeed,
            parameters.TargetScore,
            parameters.LearningPlayerId,
            parameters.ModelId,
            allSeatsLearning: parameters.AllSeatsLearning,
            ponderModelId: parameters.PonderModelId,
            playModelId: parameters.PlayModelId,
            enhanceModelId: parameters.EnhanceModelId);

        var worktreePath = Path.Combine(
            repositoryPath,
            ".wip",
            "worktrees",
            $"bones-host-iteration-{iterationNumber}");

        return new BonesLoopIterationContext(
            iterationNumber,
            derivedSeed,
            taskDescription,
            Path.GetFullPath(repositoryPath),
            worktreePath);
    }
}