﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Controls;
using NLog;
using ProtoBuf;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using Torch.Event;
using Torch.Mod;
using Torch.Mod.Messages;
using Torch.Utils;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using Wormhole.Managers;
using Wormhole.Managers.Events;
using Wormhole.ViewModels;
using Wormhole.Views;

namespace Wormhole
{
    public class Plugin : TorchPluginBase, IWpfPlugin
    {
        [ReflectedStaticMethod(Type = typeof(EventManager), Name = "AddDispatchShims")]
        private static readonly Action<Assembly> _registerAction = null!;

        public static readonly Logger Log = LogManager.GetLogger("Wormhole");

        private Persistent<Config> _config;

        private Gui _control;

        // public const string AdminGatesConfig = "admingatesconfig";

        // private const string AdminGatesConfirmReceivedFolder = "admingatesconfirmreceived";
        // private const string AdminGatesConfirmSentFolder = "admingatesconfirmsent";
        private const string AdminGatesBackupFolder = "grids_backup";

        public const string AdminGatesFolder = "admingates";
        private Task _saveOnEnterTask;

        // The actual task of saving the game on exit or enter
        private Task _saveOnExitTask;

        private int _tick;

        private string _gridDir;

        // private string _gridDirSent;
        // private string _gridDirReceived;
        private string _gridDirBackup;
        
        private ClientEffectsManager _clientEffectsManager;
        private JumpManager _jumpManager;
        private DestinationManager _destinationManager;
        private WormholeDiscoveryManager _discoveryManager;
        private ServerQueryManager _serverQueryManager;

        public static Plugin Instance { get; private set; }
        public Config Config => _config?.Data;

        public UserControl GetControl() => _control ??= new (this);

        public override void Init(ITorchBase torch)
        {
            base.Init(torch);
            Instance = this;
            SetupConfig();

            _clientEffectsManager = new (Torch);
            Torch.Managers.AddManager(_clientEffectsManager);
            _jumpManager = new (Torch);
            Torch.Managers.AddManager(_jumpManager);
            _destinationManager = new (Torch);
            Torch.Managers.AddManager(_destinationManager);
            _discoveryManager = new (Torch);
            Torch.Managers.AddManager(_discoveryManager);
            _serverQueryManager = new (Torch);
            Torch.Managers.AddManager(_serverQueryManager);
            Torch.Managers.AddManager(new SpawnManager(Torch));
            _transferManager = new(Torch);
            Torch.Managers.AddManager(_transferManager);
            Torch.Managers.AddManager(new WhitelistManager(Torch));

            _registerAction(typeof(Plugin).Assembly);
        }

        #region WorkSources

        public override void Update()
        {
            base.Update();
            if (++_tick != Config.Tick) return;
            _tick = 0;
            try
            {
                foreach (var wormhole in Config.WormholeGates)
                {
                    var gate = new BoundingSphereD(wormhole.Position, Config.GateRadius);
                    WormholeTransferOut(wormhole, gate);
                    WormholeTransferIn(wormhole.Name.Trim());
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not run Wormhole");
            }
        }

        #endregion

        #region Config

        public void Save()
        {
            _config.Save();
            _clientEffectsManager.RecalculateVisualData();
            _discoveryManager.EnsureLatestDiscovery();
        }

        private void SetupConfig()
        {
            var configFile = Path.Combine(StoragePath, "Wormhole.cfg");
            try
            {
                _config = Persistent<Config>.Load(configFile);
            }
            catch (Exception e)
            {
                Log.Warn(e);
            }
        }

        #endregion

        #region Outgoing Transferring

        private readonly List<MyEntity> _tmpEntities = new ();
        private TransferManager _transferManager;

        public void WormholeTransferOut(GateViewModel gateViewModel, BoundingSphereD gate)
        {
            // MyEntities.GetTopMostEntitiesInSphere(ref gate).OfType<MyCubeGrid>() 
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref gate, _tmpEntities, MyEntityQueryType.Dynamic);
            foreach (var grid in _tmpEntities.OfType<MyCubeGrid>())
            {
                var gts = grid.GridSystems.TerminalSystem;
                if (gts == null)
                    continue;

                var jumpDrives = gts.Blocks.OfType<MyJumpDrive>().Where(_destinationManager.IsValidJd).ToList();

                foreach (var jumpDrive in jumpDrives)
                    WormholeTransferOutFile(grid, jumpDrive, gateViewModel, jumpDrives);
            }
            _tmpEntities.Clear();
        }

        private void WormholeTransferOutFile(MyCubeGrid grid, MyJumpDrive wormholeDrive,
            GateViewModel gateViewModel, IEnumerable<MyJumpDrive> wormholeDrives)
        {
            DestinationViewModel pickedDestination;

            if (Config.AutoSend && gateViewModel.Destinations.Count == 1)
                pickedDestination = gateViewModel.Destinations[0];
            else
                pickedDestination = _destinationManager.TryGetDestination(wormholeDrive, gateViewModel);

            if (pickedDestination is null)
                return;

            var playerInCharge = Sync.Players.GetControllingPlayer(grid);

            if (playerInCharge?.Identity is null ||
                !wormholeDrive.CanJumpAndHasAccess(playerInCharge.Identity.IdentityId) ||
                !Utilities.HasRightToMove(playerInCharge, grid))
                return;
            
            foreach (var disablingWormholeDrive in wormholeDrives)
                disablingWormholeDrive.Enabled = false;

            var grids = Utilities.FindGridList(grid, Config.IncludeConnectedGrids);

            if (grids.Count == 0)
                return;

            Task.Run(async () =>
            {
                try
                {
                    var jumpTask = _jumpManager.StartJump(gateViewModel, playerInCharge, wormholeDrive.CubeGrid);

                    if (pickedDestination is GateDestinationViewModel destination &&
                        !_discoveryManager.IsLocalGate(destination.Name) &&
                        _discoveryManager.GetGateByName(destination.Name, out var address) is { })
                    {
                        var serverQueryTask = Config.CheckIsTargetServerFull ? _serverQueryManager.GetServerStatus(address) : Task.FromResult(ServerStatus.CanAccept);
                        await Task.WhenAll(jumpTask, serverQueryTask);

                        void Respond(string msg)
                        {
                            MyVisualScriptLogicProvider.SendChatMessage(msg, "Wormhole",
                                playerInCharge.Identity.IdentityId, MyFontEnum.Red);

                            MyVisualScriptLogicProvider.ShowNotification(msg, 15000,
                                MyFontEnum.Red, playerInCharge.Identity.IdentityId);
                        } 
                        
                        switch (serverQueryTask.Result)
                        {
                            case ServerStatus.CanAccept:
                                break;
                            case ServerStatus.Full:
                                Respond("Destination server is FULL!");
                                Log.Info($"Destination server is full for {playerInCharge.DisplayName} ({playerInCharge.Id.SteamId})");
                                return;
                            case ServerStatus.RequestTimeout:
                                Respond("Destination server is not responding!");
                                Log.Info($"Destination server is not responding for {playerInCharge.DisplayName} ({playerInCharge.Id.SteamId})");
                                return;
                            case ServerStatus.Loading:
                                Respond("Destination server is in loading, please wait.");
                                Log.Info($"Destination server is in loading for {playerInCharge.DisplayName} ({playerInCharge.Id.SteamId})");
                                return;
                            case ServerStatus.UnknownError:
                                Respond("Unknown error occurred when checking destination server status,\nlet admin take actions.");
                                return;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                    else
                    {
                        await jumpTask;
                    }

                    await Torch.InvokeAsync(() =>
                    {
                        switch (pickedDestination)
                        {
                            case GateDestinationViewModel gateDestination:
                                ProcessGateJump(gateDestination, grid, grids, wormholeDrive, gateViewModel, playerInCharge);
                                break;
                            case InternalDestinationViewModel internalDestination:
                                ProcessInternalGpsJump(internalDestination, grid, grids, wormholeDrive, gateViewModel,
                                    playerInCharge);
                                break;
                        }
                    });
                    _clientEffectsManager.NotifyJumpStatusChanged(JumpStatus.Succeeded, gateViewModel, grid);
                }
                catch (Exception e)
                {
                    Log.Fatal(e);
                    throw;
                }
            });
        }

        #endregion

        #region Outgoing Processing

        private void ProcessInternalGpsJump(InternalDestinationViewModel dest, MyCubeGrid grid, ICollection<MyCubeGrid> grids,
            MyJumpDrive wormholeDrive, GateViewModel gateViewModel, MyPlayer playerInCharge)
        {
            var pos = dest.TryParsePosition() ??
                      throw new InvalidOperationException($"Invalid gps position {dest.Gps}");
            var point = Utilities.PickRandomPointInSpheres(pos, dest.InnerRadius, dest.OuterRadius);
            
            var box = grids.Select(static b => b.PositionComp.WorldAABB)
                .Aggregate(static(a, b) => a.Include(b));
            var toGate = new BoundingSphereD(point, Config.GateRadius);

            var freePos = Utilities.FindFreePos(toGate,
                (float) BoundingSphereD.CreateFromBoundingBox(box).Radius);

            if (freePos is null)
                return;
            
            var fileInfo = new TransferFileInfo
            {
                DestinationWormhole = null,
                GridName = grid.DisplayName,
                PlayerName = playerInCharge.DisplayName,
                SteamUserId = playerInCharge.Id.SteamId
            };
            var info = new InternalGridTransferEvent(fileInfo, dest, grids);
            GridTransferEventShim.RaiseEvent(ref info);
            
            if (info.Cancelled)
            {
                Log.Info($"Internal gps transfer was cancelled by event handler; {fileInfo.CreateLogString()}");
                MyVisualScriptLogicProvider.SendChatMessageColored(info.CancelMessage, Color.Red, "Wormhole", playerInCharge.Identity.IdentityId);
                return;
            }

            wormholeDrive.CurrentStoredPower = 0;
            _clientEffectsManager.NotifyJumpStatusChanged(JumpStatus.Perform, gateViewModel, grid, freePos);

            MyVisualScriptLogicProvider.CreateLightning(gateViewModel.Position);
            Utilities.UpdateGridPositionAndStopLive(wormholeDrive.CubeGrid, freePos.Value);
            MyVisualScriptLogicProvider.CreateLightning(point);
        }
        
        private void ProcessGateJump(GateDestinationViewModel dest, MyCubeGrid grid, IList<MyCubeGrid> grids,
            MyJumpDrive wormholeDrive, GateViewModel gateViewModel, MyPlayer playerInCharge)
        {
            var destGate = _discoveryManager.GetGateByName(dest.Name, out var ownerIp);
                    
            if (_discoveryManager.IsLocalGate(dest.Name))
            {
                var box = grids.Select(static b => b.PositionComp.WorldAABB)
                    .Aggregate(static(a, b) => a.Include(b));
                var toGatePoint = destGate.Position;
                var toGate = new BoundingSphereD(toGatePoint, Config.GateRadius);

                var freePos = Utilities.FindFreePos(toGate,
                    (float) BoundingSphereD.CreateFromBoundingBox(box).Radius);

                if (freePos is null)
                {
                    Log.Warn($"No free pos for grid {grid.DisplayName} owner {playerInCharge.DisplayName} ({playerInCharge.Id.SteamId})");
                    return;
                }

                var fileInfo = new TransferFileInfo
                {
                    DestinationWormhole = dest.Name,
                    GridName = grid.DisplayName,
                    PlayerName = playerInCharge.DisplayName,
                    SteamUserId = playerInCharge.Id.SteamId
                };
                var info = new InternalGridTransferEvent(fileInfo, dest, grids);
                GridTransferEventShim.RaiseEvent(ref info);

                if (info.Cancelled)
                {
                    Log.Info($"Internal transfer was cancelled by event handler; {fileInfo.CreateLogString()}");
                    MyVisualScriptLogicProvider.SendChatMessageColored(info.CancelMessage, Color.Red, "Wormhole", playerInCharge.Identity.IdentityId);
                    return;
                }

                wormholeDrive.CurrentStoredPower = 0;
                _clientEffectsManager.NotifyJumpStatusChanged(JumpStatus.Perform, gateViewModel, grid, freePos);

                MyVisualScriptLogicProvider.CreateLightning(gateViewModel.Position);
                Utilities.UpdateGridPositionAndStopLive(wormholeDrive.CubeGrid, freePos.Value);
                MyVisualScriptLogicProvider.CreateLightning(toGatePoint);
            }
            else
            {

                var transferFileInfo = new TransferFileInfo
                {
                    DestinationWormhole = dest.Name,
                    SteamUserId = playerInCharge.Id.SteamId,
                    PlayerName = playerInCharge.DisplayName,
                    GridName = grid.DisplayName
                };

                Log.Info($"creating filetransfer: {transferFileInfo.CreateLogString()}");

                var info = new OutgoingGridTransferEvent(transferFileInfo, dest, grids);
                GridTransferEventShim.RaiseEvent(ref info);
                if (info.Cancelled)
                {
                    Log.Info("Outgoing transfer was cancelled by event handler");
                    MyVisualScriptLogicProvider.SendChatMessageColored(info.CancelMessage, Color.Red, "Wormhole", playerInCharge.Identity.IdentityId);
                    return;
                }
                
                var filename = transferFileInfo.CreateFileName();

                wormholeDrive.CurrentStoredPower = 0;
                _clientEffectsManager.NotifyJumpStatusChanged(JumpStatus.Perform, gateViewModel, grid);

                MyVisualScriptLogicProvider.CreateLightning(gateViewModel.Position);

                var objectBuilders = grids.Select(b => (MyObjectBuilder_CubeGrid)b.GetObjectBuilder()).ToList();

                static IEnumerable<long> GetIds(MyObjectBuilder_CubeBlock block)
                {
                    if (block.Owner > 0)
                        yield return block.Owner;
                    if (block.BuiltBy > 0)
                        yield return block.BuiltBy;
                    if (block is MyObjectBuilder_Cockpit {Pilot: { }} cockpit)
                        yield return cockpit.Pilot.OwningPlayerIdentityId!.Value;
                }

                var identitiesMap = objectBuilders.SelectMany(static b => b.CubeBlocks)
                    .SelectMany(GetIds).Distinct().Where(static b => !Sync.Players.IdentityIsNpc(b))
                    .ToDictionary(static b => b, static b => Sync.Players.TryGetIdentity(b).GetObjectBuilder());

                var sittingPlayerIdentityIds = new HashSet<long>();
                foreach (var cubeBlock in objectBuilders.SelectMany(static cubeGrid => cubeGrid.CubeBlocks))
                {
                    if (!Config.ExportProjectorBlueprints)
                        if (cubeBlock is MyObjectBuilder_ProjectorBase projector)
                            projector.ProjectedGrids = null;

                    if (cubeBlock is not MyObjectBuilder_Cockpit cockpit) continue;
                    if (cockpit.Pilot?.OwningPlayerIdentityId == null) continue;

                    var playerSteamId = Sync.Players.TryGetSteamId(cockpit.Pilot.OwningPlayerIdentityId.Value);
                    sittingPlayerIdentityIds.Add(cockpit.Pilot.OwningPlayerIdentityId.Value);
                    Utilities.SendConnectToServer(ownerIp, playerSteamId);
                }

                using (var stream =
                    File.Create(Utilities.CreateBlueprintPath(Path.Combine(Config.Folder, AdminGatesFolder),
                        filename)))
                using (var compressStream = new GZipStream(stream, CompressionMode.Compress))
                    Serializer.Serialize(compressStream, new TransferFile
                    {
                        Grids = objectBuilders,
                        IdentitiesMap = identitiesMap,
                        PlayerIdsMap = identitiesMap.Select(static b =>
                            {
                                Sync.Players.TryGetPlayerId(b.Key, out var id);
                                return (b.Key, id.SteamId);
                            }).Where(static b => b.SteamId > 0)
                            .ToDictionary(static b => b.Key, static b => b.SteamId),
                        SourceDestinationId = dest.Id,
                        SourceGateName = gateViewModel.Name
                    });

                foreach (var identity in sittingPlayerIdentityIds.Select(Sync.Players.TryGetIdentity)
                    .Where(b => b.Character is { })) Utilities.KillCharacter(identity.Character);
                foreach (var cubeGrid in grids)
                {
                    cubeGrid.Close();
                }

                // Saves the game if enabled in config.
                if (!Config.SaveOnExit) return;
                // (re)Starts the task if it has never been started o´r is done
                if (_saveOnExitTask is null || _saveOnExitTask.IsCompleted)
                    _saveOnExitTask = Torch.Save();
            }
        }

        #endregion

        #region Ingoing Transferring

        public void WormholeTransferIn(string wormholeName)
        {
            EnsureDirectoriesCreated();

            var changes = false;

            foreach (var file in Directory.EnumerateFiles(_gridDir, "*.sbcB5")
                    .Where(s => Path.GetFileNameWithoutExtension(s).Split('_')[0] == wormholeName))
                //if file not null if file exists if file is done being sent and if file hasnt been received before
            {
                var fileName = Path.GetFileName(file);
                if (!File.Exists(file)) continue;

                Log.Info("Processing recivied grid: " + fileName);
                var fileTransferInfo = TransferFileInfo.ParseFileName(fileName);
                if (fileTransferInfo is null)
                {
                    Log.Error("Error parsing file name");
                    continue;
                }

                TransferFile transferFile;
                try
                {
                    using var stream = File.OpenRead(file);
                    using var decompressStream = new GZipStream(stream, CompressionMode.Decompress);

                    transferFile = Serializer.Deserialize<TransferFile>(decompressStream);
                    if (transferFile.Grids is null || transferFile.IdentitiesMap is null)
                        throw new InvalidOperationException("File is empty or invalid");
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Corrupted file at {fileName}");
                    continue;
                }

                if (Sync.Players.TryGetPlayerIdentity(new (fileTransferInfo.SteamUserId))?.Character is { } character)
                    Utilities.KillCharacter(character);

                _transferManager.QueueIncomingTransfer(transferFile, fileTransferInfo);

                changes = true;
                var backupFileName = fileName;
                if (File.Exists(Path.Combine(_gridDirBackup, backupFileName)))
                {
                    var transferString = Path.GetFileNameWithoutExtension(backupFileName);
                    var i = 0;
                    do
                    {
                        backupFileName = $"{transferString}_{++i}.sbcB5";
                    } while (File.Exists(Path.Combine(_gridDirBackup, backupFileName)));
                }
                
                File.Copy(Path.Combine(_gridDir, fileName), Path.Combine(_gridDirBackup, backupFileName));

                File.Delete(Path.Combine(_gridDir, fileName));
            }

            // Saves game on enter if enabled in config.
            if (!changes || !Config.SaveOnEnter) return;

            if (_saveOnEnterTask is null || _saveOnEnterTask.IsCompleted)
                _saveOnEnterTask = Torch.Save();
        }

        #endregion

        private void EnsureDirectoriesCreated()
        {
            _gridDir ??= Path.Combine(Config.Folder, AdminGatesFolder);
            // _gridDirSent ??= Path.Combine(Config.Folder, AdminGatesConfirmSentFolder);
            // _gridDirReceived ??= Path.Combine(Config.Folder, AdminGatesConfirmReceivedFolder);
            _gridDirBackup ??= Path.Combine(Config.Folder, AdminGatesBackupFolder);
            if (!Directory.Exists(_gridDir))
                Directory.CreateDirectory(_gridDir);
            // if (!Directory.Exists(_gridDirSent))
            //     Directory.CreateDirectory(_gridDirSent);
            // if (!Directory.Exists(_gridDirReceived))
            //     Directory.CreateDirectory(_gridDirReceived);
            if (!Directory.Exists(_gridDirBackup))
                Directory.CreateDirectory(_gridDirBackup);
        }
    }
}