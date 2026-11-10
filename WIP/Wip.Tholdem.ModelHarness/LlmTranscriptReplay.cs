namespace Wip.Tholdem.ModelHarness;

/// <summary>
/// Replays a recorded transcript response offline. The only input is the <see cref="LlmTranscript"/>;
/// the type holds no model, makes no network call and reads no clock, so replay is possible from the
/// record alone and cannot construct or call an <c>IChatModel</c>.
/// </summary>
public sealed class LlmTranscriptReplay
{
    /// <summary>Parses the recorded response and returns the proposal the recorder produced.</summary>
    public LlmProposal Propose(LlmTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return LlmProposalParser.Parse(transcript.Response);
    }
}
