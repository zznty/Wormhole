using System.Collections.Concurrent;
using NLog;
using ParallelTasks;
using Sandbox.Engine.Multiplayer;
using Torch.API;
using Torch.API.Managers;
using Torch.Managers;
using Torch.Server.Managers;

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
            Torch.GameStateChanged += (_, state) =>
            {
                if (state != TorchGameState.Loaded)
                    return;

                Torch.CurrentSession.Managers.GetManager<MultiplayerManagerDedicated>().PlayerJoined += MultiplayerOnPlayerJoined;
            };
        }

        private void MultiplayerOnPlayerJoined(IPlayer player)
        {
            if (!_queue.TryGetValue(player.SteamId, out var value))
                return;

            var (file, fileInfo) = value;
            Parallel.Start(() =>
            {
                if (ProcessTransfer(file, fileInfo))
                    _queue.TryRemove(player.SteamId, out _);
            });
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