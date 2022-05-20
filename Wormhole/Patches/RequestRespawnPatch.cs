using System;
using System.Reflection;
using SpaceEngineers.Game.World;
using Torch.Managers.PatchManager;
using Torch.Utils;
using VRage.Network;

namespace Wormhole.Patches;

[PatchShim]
public static class RequestRespawnPatch
{
    [ReflectedMethodInfo(typeof(MySpaceRespawnComponent), "RespawnRequest_Implementation")]
    private static MethodInfo _requestRespawnMethod = null!;

    public static Func<ulong, bool> RespawnScreenRequest;

    public static void Patch(PatchContext context)
    {
        context.GetPattern(_requestRespawnMethod).Prefixes.Add(new Func<bool>(Prefix).Method);
    }

    private static bool Prefix()
    {
        return !RespawnScreenRequest(MyEventContext.Current.Sender.Value);
    }
}