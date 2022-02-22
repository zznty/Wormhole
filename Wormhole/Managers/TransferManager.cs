﻿using System;
using System.Collections.Concurrent;
using NLog;
using Sandbox.Engine.Multiplayer;
using Torch.API;
using Torch.Managers;
using Wormhole.Patches;

namespace Wormhole.Managers
{
    public class TransferManager : Manager
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentDictionary<ulong, (TransferFile, Utilities.TransferFileInfo)> _queue = new();
        
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

        public void QueueIncomingTransfer(TransferFile file, Utilities.TransferFileInfo fileTransferInfo)
        {
            _queue[fileTransferInfo.SteamUserId] = (file, fileTransferInfo);
        }

        private bool ProcessTransfer(TransferFile file, Utilities.TransferFileInfo fileInfo)
        {
            var wormhole = _discoveryManager.GetGateByName(fileInfo.DestinationWormhole, out _);

            if (Utilities.FindFreePos(new(wormhole.Position, Plugin.Instance.Config.GateRadius), Utilities.FindGridsRadius(file.Grids)) is not { } freePos)
            {
                Log.Warn($"Unable to transfer incoming grid - no free pos for spawn - {fileInfo.CreateLogString()}");
                MyMultiplayer.Static.DisconnectClient(fileInfo.SteamUserId);
                return false;
            }
            
            _spawnManager.RemapOwnership(file, fileInfo.SteamUserId);
            Utilities.UpdateGridsPositionAndStop(file.Grids, freePos);
            
            _spawnManager.SpawnGridsParallel(file.Grids);
            return true;
        }
    }
}