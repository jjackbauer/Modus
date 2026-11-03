using System.Text.Json;
using Wip.Sandbox.Protocol;

namespace Wip.Bones.Engine;

public static class BonesWorkerAdapter
{
    public static string Choose(object slot, string stateJson, string legalJson)
    {
        var stateDto = JsonSerializer.Deserialize<BonesWorkerState>(stateJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("State could not be read.");
        var legalDtos = JsonSerializer.Deserialize<BonesWorkerMove[]>(legalJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Legal moves could not be read.");
        var state = BonesWorkerBridge.ToState(stateDto);
        var legal = legalDtos.Select(BonesWorkerBridge.ToMove).ToArray();
        var move = ((IBonesPlayerSlot)slot).ChooseMove(state, legal);
        return JsonSerializer.Serialize(BonesWorkerBridge.ToMoveDto(move));
    }
}
