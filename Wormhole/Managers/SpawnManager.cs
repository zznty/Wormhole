using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using ParallelTasks;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch.API;
using Torch.Managers;
using Torch.Utils;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ObjectBuilders.Components;

namespace Wormhole.Managers
{
    public class SpawnManager : Manager
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();

        public SpawnManager(ITorchBase torchInstance) : base(torchInstance)
        {
        }

        public void SpawnGridsParallel(IEnumerable<MyObjectBuilder_CubeGrid> grids,
            Action<ICollection<MyCubeGrid>> onCompletedCallback = null)
        {
            Parallel.Start(() =>
            {
                var spawnedGrids = SpawnInternal(grids);
                onCompletedCallback?.Invoke(spawnedGrids);
            }).WaitOrExecute();
        }

        private static ICollection<MyCubeGrid> SpawnInternal(IEnumerable<MyObjectBuilder_CubeGrid> grids)
        {
            return grids.Select(gridBuilder =>
            {
                Log.Debug($"Spawning grid {gridBuilder.DisplayName}");
                //MyEntities.RemapObjectBuilder(gridBuilder);  // subgrids fail

                var entity = MyEntities.CreateFromObjectBuilderNoinit(gridBuilder);
                MyEntities.InitEntity(gridBuilder, ref entity);
                MyEntities.Add(entity, true);
                DoneHandler(entity);

                //MyEntities.InitAsync(entity, gridBuilder, true, DoneHandler); // subgrids fail
                return (MyCubeGrid)entity;
            }).ToArray();
        }

        private static void DoneHandler(MyEntity obj)
        {
            var grid = (MyCubeGrid)obj;

            if (grid is null)
                return;

            foreach (var cockpit in grid.GetFatBlocks<MyCockpit>().Where(b => b.Pilot is { }))
            {
                if (cockpit.Pilot.GetIdentity() is { } identity && Sync.Players.TryGetPlayerId(identity.IdentityId, out var playerId))
                {
                    identity.ChangeCharacter(cockpit.Pilot);
                    if (Sync.Players.GetPlayerById(playerId) is not { } player)
                        continue;

                    MyMultiplayer.RaiseStaticEvent(_ => MySession.SetSpectatorPositionFromServer,
                        cockpit.PositionComp.GetPosition(), new(playerId.SteamId));
                    MySession.SendVicinityInformation(cockpit.CubeGrid.EntityId, new(playerId.SteamId));
                    Sync.Players.SetControlledEntity(playerId.SteamId, cockpit);
                    cockpit.Pilot.SetPlayer(player);
                    Sync.Players.RevivePlayer(player);
                }
                else
                    Log.Warn($"Detected character without identity. Clang magic may occur\ngrid: {cockpit.CubeGrid.DisplayName} cockpit: {cockpit.CustomName} identity id: {cockpit.Pilot.GetPlayerIdentityId()}");
            }
        }

        public void RemapOwnership(TransferFile file, ulong requester)
        {
            var identitiesToChange = new Dictionary<long, long>();
            foreach (var (identityId, clientId) in file.PlayerIdsMap.Where(static b =>
                Sync.Players.TryGetPlayerIdentity(new(b.Value)) is null))
            {
                var ob = file.IdentitiesMap[identityId];
                ob.IdentityId = MyEntityIdentifier.AllocateId(MyEntityIdentifier.ID_OBJECT_TYPE.IDENTITY);
                Sync.Players.CreateNewIdentity(ob).PerformFirstSpawn();

                var id = new MyPlayer.PlayerId(clientId);

                Sync.Players.GetPrivateField<ConcurrentDictionary<MyPlayer.PlayerId, long>>("m_playerIdentityIds")[id] =
                    ob.IdentityId;
                Sync.Players.GetPrivateField<Dictionary<long, MyPlayer.PlayerId>>("m_identityPlayerIds")
                    [ob.IdentityId] = id;

                identitiesToChange[identityId] = ob.IdentityId;
            }

            foreach (var (oldIdentityId, clientId) in file.PlayerIdsMap)
            {
                var identity = Sync.Players.TryGetPlayerIdentity(new(clientId));

                if (identity is { })
                    identitiesToChange[oldIdentityId] = identity.IdentityId;
                else if (!identitiesToChange.ContainsKey(oldIdentityId) && Plugin.Instance.Config.KeepOwnership)
                    Log.Warn($"New Identity id for {clientId} ({oldIdentityId}) not found! This will cause player to loose ownership");

                if (identity?.Character is { })
                    Utilities.KillCharacter(identity.Character);
            }

            MyIdentity requesterIdentity = null!;
            if (!Plugin.Instance.Config.KeepOwnership)
                requesterIdentity = Sync.Players.TryGetPlayerIdentity(new(requester));

            foreach (var cubeBlock in file.Grids.SelectMany(static b => b.CubeBlocks))
            {
                if (cubeBlock is MyObjectBuilder_Cockpit builderCockpit)
                    RemapCockpit(builderCockpit, identitiesToChange);

                if (!Plugin.Instance.Config.KeepOwnership && requesterIdentity is { })
                {
                    cubeBlock.Owner = requesterIdentity.IdentityId;
                    cubeBlock.BuiltBy = requesterIdentity.IdentityId;
                    continue;
                }

                if (identitiesToChange.TryGetValue(cubeBlock.BuiltBy, out var builtBy))
                    cubeBlock.BuiltBy = builtBy;

                if (identitiesToChange.TryGetValue(cubeBlock.Owner, out var owner))
                    cubeBlock.Owner = owner;
            }
        }

        private static void RemapCockpit(MyObjectBuilder_Cockpit cockpit, IDictionary<long, long> identitiesToChange)
        {
            if (cockpit.Pilot is null)
                return;

            if (!Plugin.Instance.Config.PlayerRespawn)
            {
                Utilities.RemovePilot(cockpit);
                return;
            }

            cockpit.Pilot.OwningPlayerIdentityId = identitiesToChange[cockpit.Pilot.OwningPlayerIdentityId!.Value];
            var component = cockpit.ComponentContainer?.Components?.FirstOrDefault(static b =>
                b.Component is MyObjectBuilder_HierarchyComponentBase);

            if (component?.Component is MyObjectBuilder_HierarchyComponentBase hierarchyComponent)
                hierarchyComponent.Children[0] = cockpit.Pilot;

            if (Sync.Players.TryGetIdentity(cockpit.Pilot.OwningPlayerIdentityId!.Value) is { } identity)
                // to prevent from hungry trash collector
                identity.LastLogoutTime = DateTime.Now;
            else
                Log.Warn($"Failed to remap cockpit {cockpit.CustomName} identity id: {cockpit.Pilot.OwningPlayerIdentityId}");
        }
    }
}