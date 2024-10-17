using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Commands.Permissions;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Wormhole.Managers;

namespace Wormhole
{
    [Category("wormhole")]
    public class Commands : CommandModule
    {
        public Plugin Plugin => (Plugin) Context.Plugin;

        [Command("show", "shows wormholes")]
        [Permission(MyPromoteLevel.None)]
        public void ShowWormholes()
        {
            foreach (var wormholeGate in Plugin.Config.WormholeGates)
            {
                var gps = wormholeGate.ToGps();
                MySession.Static.Gpss.SendAddGpsRequest(Context.Player.IdentityId, ref gps);
            }

            Context.Respond("GPSs added to your list if it didn't already exist");
        }

        [Command("showeveryone", "shows wormholes to everyone (offline players too)")]
        [Permission(MyPromoteLevel.Admin)]
        public void ShowEveryoneWormholes()
        {
            foreach (var wormholeGate in Plugin.Config.WormholeGates)
            {
                var gps = wormholeGate.ToGps();
                foreach (var (_, identityId) in Sync.Players.m_playerIdentityIds)
                {
                    MySession.Static.Gpss.AddPlayerGps(identityId, ref gps);
                }
            }

            Context.Respond("GPSs added to everyone's list if it didn't already exist");
        }

        [Command("safezone", "adds safe zones")]
        [Permission(MyPromoteLevel.Admin)]
        public void SafeZone(float radius = -1)
        {
            if (radius < 1) radius = (float) Plugin.Config.GateRadius;
            var entities = MyEntities.GetEntities();
            if (entities != null)
                foreach (var entity in entities.Where(static entity =>
                    entity is MySafeZone && entity.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-SafeZone]")))
                {
                    entity.Close();
                }

            foreach (var server in Plugin.Config.WormholeGates)
            {
                var ob = new MyObjectBuilder_SafeZone
                {
                    PositionAndOrientation =
                        new MyPositionAndOrientation(new (server.X, server.Y, server.Z), Vector3.Forward, Vector3.Up),
                    PersistentFlags = MyPersistentEntityFlags2.InScene,
                    Shape = MySafeZoneShape.Sphere,
                    Radius = radius,
                    Enabled = true,
                    DisplayName = "[NPC-IGNORE]_[Wormhole-SafeZone]_" + server.Name,
                    AccessTypeGrids = MySafeZoneAccess.Blacklist,
                    AccessTypeFloatingObjects = MySafeZoneAccess.Blacklist,
                    AccessTypeFactions = MySafeZoneAccess.Blacklist,
                    AccessTypePlayers = MySafeZoneAccess.Blacklist
                };
                MyEntities.CreateFromObjectBuilderAndAdd(ob, true);
            }

            Context.Respond(
                "Deleted all entities with '[NPC-IGNORE]_[Wormhole-SafeZone]' in them and readded Safezones to each Wormhole");
        }

        [Command("addgates",
            "Adds gates to each wormhole (type: 1 = regular, 2 = rotating, 3 = advanced rotating, 4 = regular 53blocks, 5 = rotating 53blocks, 6 = advanced rotating 53blocks) (selfowned: true = owned by you) (ownerid = id of who you want it owned by)")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddGates(int type = 1, bool selfowned = false, long ownerid = 0L)
        {
            try
            {
                var entities = MyEntities.GetEntities();
                if (entities is { })
                    foreach (var grid in entities
                        .OfType<MyCubeGrid>()
                        .Where(static b => b.DisplayName.Contains("[NPC-IGNORE]_[Wormhole-Gate]")))
                    {
                        grid.Close();
                    }

                foreach (var gateViewModel in Plugin.Config.WormholeGates)
                {
                    var prefab = type switch
                    {
                        2 => "WORMHOLE_ROTATING",
                        3 => "WORMHOLE_ROTATING_ADVANCED",
                        4 => "WORMHOLE_53_BLOCKS",
                        5 => "WORMHOLE_ROTATING_53_BLOCKS",
                        6 => "WORMHOLE_ROTATING_ADVANCED_53_BLOCKS",
                        _ => "WORMHOLE"
                    };

                    var grids = MyPrefabManager.Static.GetGridPrefab(prefab).ToList();

                    foreach (var cubeBlock in grids.SelectMany(static grid => grid.CubeBlocks))
                    {
                        if (selfowned)
                        {
                            cubeBlock.Owner = Context.Player.IdentityId;
                            cubeBlock.BuiltBy = Context.Player.IdentityId;
                        }
                        else
                        {
                            cubeBlock.Owner = ownerid;
                            cubeBlock.BuiltBy = ownerid;
                        }
                    }
                    
                    MyEntities.RemapObjectBuilderCollection(grids);
                    Utilities.UpdateGridsPositionAndStop(grids, gateViewModel.Position);
                    
                    foreach (var grid in grids)
                    {
                        grid.IsStatic = !prefab.Contains("rotating", StringComparison.OrdinalIgnoreCase);
                        grid.Immune = true;
                        grid.Editable = false;
                        
                        // Queue work for creation thread, so no lags on main
                        MyEntities.CreateAsync(grid, true);
                    }
                }

                Context.Respond(
                    "Deleted all entities with '[NPC-IGNORE]_[Wormhole-Gate]' in them and readded Gates to each Wormhole");
            }
            catch
            {
                Context.Respond("Error: make sure you add the mod for the gates you are using");
            }
        }

        [Command("thorshammer", "Summon the power of Mjölnir")]
        [Permission(MyPromoteLevel.Admin)]
        public void ThorsHammer()
        {
            MyVisualScriptLogicProvider.CreateLightning(Context.Player.GetPosition());
        }

        [Command("clearsync", "try to remove all files in sync folder")]
        [Permission(MyPromoteLevel.Admin)]
        public void ClearSync()
        {
            var gridDir = new DirectoryInfo(Plugin.Config.Folder + "/" + Plugin.AdminGatesFolder);

            if (!gridDir.Exists) return;

            foreach (var file in gridDir.GetFiles())
            {
                if (file == null) continue;

                try
                {
                    file.Delete();
                }
                catch
                {
                }
            }
        }

        [Command("clear characters", "try to remove all your character (if it duplicated or bugged)")]
        [Permission(MyPromoteLevel.None)]
        public void RemoveCharacters()
        {
            if (Context.Player == null)
                return;

            var identity = (MyIdentity) Context.Player.Identity;
            Utilities.KillCharacters(identity.SavedCharacters);

            Sync.Players.KillPlayer((MyPlayer) Context.Player);
        }

        [Command("show discovery", "show the current discovered gates")]
        public void ShowDiscovery()
        {
            var sb = new StringBuilder();

            if (Context.Player is null)
                sb.AppendLine("Discovered Gates:");
                    
            foreach (var (ip, discovery) in Context.Torch.Managers.GetManager<WormholeDiscoveryManager>().Discoveries)
            {
                sb.AppendLine($"= {ip} - {discovery.Gates.Count} gates");
                foreach (var gate in discovery.Gates)
                {
                    sb.AppendLine($"{" ",-3}+ {gate.Name}:");
                    foreach (var destination in gate.Destinations)
                    {
                        sb.AppendLine($"{" ",-6}-> {destination.DisplayName}");
                    }
                }
            }
            
            if (Context.Player is null)
                Context.Respond(sb.ToString());
            else
                ModCommunication.SendMessageTo(new DialogMessage("Wormhole", "Discovered Gates", sb.ToString()), Context.Player.SteamUserId);
        }
    }
}