using System;
using System.Linq;
using System.Reflection;
using Sandbox.Engine.Networking;
using Torch.Managers.PatchManager;
using Torch.Utils;
using VRage.Game;

namespace Wormhole.Patches;

[PatchShim]
public static class ModsPatch
{
    private const ulong ModId = 2758536657;

    [ReflectedMethodInfo(typeof(MyLocalCache), nameof(MyLocalCache.LoadCheckpoint))]
    private static MethodInfo _loadCheckpointMethod = null!;

    public static void Patch(PatchContext context)
    {
        context.GetPattern(_loadCheckpointMethod).Suffixes.Add(new Action<MyObjectBuilder_Checkpoint>(Suffix).Method);
    }

    private static void Suffix(MyObjectBuilder_Checkpoint __result)
    {
        if (__result.Mods.All(b => b.PublishedFileId != ModId))
            __result.Mods.Add(ModItemUtils.Create(ModId));
    }
}