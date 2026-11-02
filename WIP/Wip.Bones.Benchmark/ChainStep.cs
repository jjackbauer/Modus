namespace Wip.Bones.Benchmark;

public sealed record ChainStep(int Seat, string IncumbentId, string CandidateId, int CandidateVersion);

public sealed record EvalSample(
    bool CandidateWon,
    int CandidateScore,
    bool IncumbentPairedWon,
    int IncumbentPairedScore,
    bool IncumbentIndependentWon,
    int IncumbentIndependentScore);
