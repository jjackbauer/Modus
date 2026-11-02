using Wip.Bones.Domain;
using Wip.Bones.Engine;

namespace Wip.Bones.Benchmark;

public abstract record FieldEntry(string Name)
{
    public abstract IBonesPlayerSlot Create(int seed);
}

public sealed record ScriptFieldEntry(string Name, Type SlotType) : FieldEntry(Name)
{
    public override IBonesPlayerSlot Create(int seed)
        => (IBonesPlayerSlot)(Activator.CreateInstance(SlotType)
            ?? throw new InvalidOperationException($"Unable to instantiate strategy slot '{SlotType.FullName}'."));
}

public sealed record FactoryFieldEntry(string Name, Func<int, IBonesPlayerSlot> Factory) : FieldEntry(Name)
{
    public override IBonesPlayerSlot Create(int seed) => Factory(seed);
}

public static class DeterministicOpponents
{
    public static IReadOnlyList<FieldEntry> CreateArchetypes() =>
    [
        new FactoryFieldEntry("first-legal", _ => new FirstLegalMoveSlot()),
        new FactoryFieldEntry("highest-pip", _ => new HighestPipSlot()),
        new FactoryFieldEntry("lowest-pip", _ => new LowestPipSlot()),
        new FactoryFieldEntry("double-keeper", _ => new DoubleKeeperSlot()),
        new FactoryFieldEntry("random-seeded", seed => new RandomSeededSlot(seed)),
    ];

    public sealed class FirstLegalMoveSlot : IBonesPlayerSlot
    {
        public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
            => legalMoves[0];
    }

    public sealed class HighestPipSlot : IBonesPlayerSlot
    {
        public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
        {
            BonesMove? best = null;
            var bestPips = int.MinValue;
            foreach (var move in legalMoves)
            {
                if (move.IsPass)
                    continue;

                var pips = move.Tile!.Value.TotalPips;
                if (pips > bestPips)
                {
                    best = move;
                    bestPips = pips;
                }
            }

            return best ?? legalMoves[0];
        }
    }

    public sealed class LowestPipSlot : IBonesPlayerSlot
    {
        public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
        {
            BonesMove? best = null;
            var bestPips = int.MaxValue;
            foreach (var move in legalMoves)
            {
                if (move.IsPass)
                    continue;

                var pips = move.Tile!.Value.TotalPips;
                if (pips < bestPips)
                {
                    best = move;
                    bestPips = pips;
                }
            }

            return best ?? legalMoves[0];
        }
    }

    public sealed class DoubleKeeperSlot : IBonesPlayerSlot
    {
        public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
        {
            BonesMove? bestNonDouble = null;
            var bestPips = int.MinValue;
            foreach (var move in legalMoves)
            {
                if (move.IsPass)
                    continue;

                var tile = move.Tile!.Value;
                if (tile.IsDouble)
                    continue;

                if (tile.TotalPips > bestPips)
                {
                    bestNonDouble = move;
                    bestPips = tile.TotalPips;
                }
            }

            return bestNonDouble ?? legalMoves[0];
        }
    }

    public sealed class RandomSeededSlot : IBonesPlayerSlot
    {
        private readonly Random _random;

        public RandomSeededSlot(int seed)
        {
            _random = new Random(seed);
        }

        public BonesMove ChooseMove(BonesRoundState state, IReadOnlyList<BonesMove> legalMoves)
        {
            var nonPass = new List<BonesMove>(legalMoves.Count);
            foreach (var move in legalMoves)
            {
                if (!move.IsPass)
                    nonPass.Add(move);
            }

            if (nonPass.Count == 0)
                return legalMoves[0];

            return nonPass[_random.Next(nonPass.Count)];
        }
    }
}
