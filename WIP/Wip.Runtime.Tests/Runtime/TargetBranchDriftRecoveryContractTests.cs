using Wip.Abstractions.Sessions;
using Wip.Runtime.Runtime;
using Xunit;

namespace Wip.Runtime.Tests.Runtime;

public sealed class TargetBranchDriftRecoveryContractTests
{
    [Fact]
    public void Current_GivenTargetBranchDrift_ReturnsRecoveryStepsAndObservableSessionStates()
    {
        var contract = TargetBranchDriftRecovery.Current;

        Assert.Collection(
            contract.Steps,
            step =>
            {
                Assert.Equal("rebase", step.Command);
                Assert.Equal(SessionState.Editing, step.SessionStateAfterStep);
                Assert.Contains("refreshed target baseline", step.Description, StringComparison.Ordinal);
            },
            step =>
            {
                Assert.Equal("diff", step.Command);
                Assert.Equal(SessionState.Editing, step.SessionStateAfterStep);
                Assert.Contains("updated target head", step.Description, StringComparison.Ordinal);
            },
            step =>
            {
                Assert.Equal("validate", step.Command);
                Assert.Equal(SessionState.Validating, step.SessionStateAfterStep);
                Assert.Contains("Revalidate", step.Description, StringComparison.Ordinal);
            },
            step =>
            {
                Assert.Equal("review", step.Command);
                Assert.Equal(SessionState.AwaitingApproval, step.SessionStateAfterStep);
                Assert.Contains("review evidence", step.Description, StringComparison.Ordinal);
            },
            step =>
            {
                Assert.Equal("approve", step.Command);
                Assert.Equal(SessionState.Approved, step.SessionStateAfterStep);
                Assert.Contains("approval evidence", step.Description, StringComparison.Ordinal);
            },
            step =>
            {
                Assert.Equal("merge", step.Command);
                Assert.Equal(SessionState.Merged, step.SessionStateAfterStep);
                Assert.Contains("Retry merge", step.Description, StringComparison.Ordinal);
            });

        Assert.Equal(
            [
                SessionState.Editing,
                SessionState.Validating,
                SessionState.AwaitingApproval,
                SessionState.Approved
            ],
            contract.ObservableSessionStatesBeforeRetry);

        var guidance = contract.FormatGuidance();

        Assert.Contains("rebase, revalidate, and refresh", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rebase", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diff", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("validate", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("approve", guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Editing -> Validating -> AwaitingApproval -> Approved", guidance, StringComparison.Ordinal);
    }
}