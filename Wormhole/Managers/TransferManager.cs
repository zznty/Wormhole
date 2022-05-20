using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Torch.API;
using Torch.Managers;
using Wormhole.Managers.Events;
using Wormhole.Patches;
using Wormhole.ViewModels;

namespace Wormhole.Managers
{
    public class TransferManager : Manager
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentDictionary<ulong, (TransferFile, TransferFileInfo)> _queue = new();

        [Dependency] private readonly SpawnManager _spawnManager = null!;
        [Dependency] private readonly WormholeDiscoveryManager _discoveryManager = null!;

        public TransferManager(ITorchBase torchInstance) : base(torchInstance)
        {
        }

        public override void Attach()
        {
            base.Attach();
            RequestRespawnPatch.RespawnScreenRequest = RespawnScreenRequest;
        }

        public override void Detach()
        {
            base.Detach();
            RequestRespawnPatch.RespawnScreenRequest = null;
        }

        private bool RespawnScreenRequest(ulong clientId)
        {
            if (!_queue.TryGetValue(clientId, out var value))
                return false;

            var (file, fileInfo) = value;

            Log.Info($"Queued grid is being spawned {fileInfo.CreateLogString()}");

            bool result;

            try
            {
                result = ProcessTransfer(file, fileInfo);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error while transfer process");
                MyMultiplayer.Static.DisconnectClient(clientId);
                return false;
            }

            return result && _queue.TryRemove(clientId, out _);
        }

        public void QueueIncomingTransfer(TransferFile file, TransferFileInfo fileTransferInfo)
        {
            var dest = (GateDestinationViewModel)_discoveryManager.GetGateByName(file.SourceGateName, out _)
                .Destinations.First(b => b.Id == file.SourceDestinationId);

            var info = new IngoingGridTransferEvent(fileTransferInfo, dest, file);
            GridTransferEventShim.RaiseEvent(ref info);
            if (info.Cancelled)
            {
                Log.Info($"Transfer was cancelled by event handler; {fileTransferInfo.CreateLogString()}");
                return;
            }

            _queue[fileTransferInfo.SteamUserId] = (file, fileTransferInfo);
        }

        private bool ProcessTransfer(TransferFile file, TransferFileInfo fileInfo)
        {
            var wormhole = _discoveryManager.GetGateByName(fileInfo.DestinationWormhole, out _);

            if (Utilities.FindFreePos(new(wormhole.Position, Plugin.Instance.Config.GateRadius), Utilities.FindGridsRadius(file.Grids)) is not { } freePos)
            {
                Log.Warn($"Unable to transfer incoming grid - no free pos for spawn - {fileInfo.CreateLogString()}");
                MyMultiplayer.Static.DisconnectClient(fileInfo.SteamUserId);
                return false;
            }

            MyEntities.RemapObjectBuilderCollection(file.Grids);

            _spawnManager.RemapOwnership(file, fileInfo.SteamUserId);
            Utilities.UpdateGridsPositionAndStop(file.Grids, freePos);

            var dest = (GateDestinationViewModel)_discoveryManager.GetGateByName(file.SourceGateName, out _)
                .Destinations.First(b => b.Id == file.SourceDestinationId);

            _spawnManager.SpawnGridsParallel(file.Grids, grids =>
            {
                var spawnedInfo = new IngoingGridSpawnedEvent(fileInfo, dest, grids);
                GridTransferEventShim.RaiseEvent(ref spawnedInfo);
            });

            var PlayerName = fileInfo.PlayerName;
            Log.Warn($"Player {PlayerName} used wormhole to jump to this server.");

            if (Plugin.Instance.Config.JumpInNotification != string.Empty)
            {
                var JumpIn = Plugin.Instance.Config.JumpInNotification;

                if (JumpIn.Contains("{PlayerName}"))
                    JumpIn = Regex.Replace(JumpIn, @"{PlayerName}", $"{PlayerName}");

                MyAPIGateway.Utilities.SendMessage(JumpIn);
            }

            return true;
        }
    }
}