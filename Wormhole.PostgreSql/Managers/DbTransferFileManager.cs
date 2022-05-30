using NLog;
using Npgsql;
using ProtoBuf;
using Sandbox.Game;
using Sandbox.Game.Multiplayer;
using Torch.API;
using Torch.API.Managers;
using Torch.Managers;
using VRage.Game;
using Wormhole.Managers;
using Wormhole.PostgreSql.Dto;
using Parallel = ParallelTasks.Parallel;
namespace Wormhole.PostgreSql.Managers;

public class DbTransferFileManager : Manager, IFileTransferManager
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    
    [Dependency]
    private readonly DbManager _dbManager = null!;

    [Dependency]
    private readonly IWormholeDiscoveryManager _discoveryManager = null!;

    [Dependency]
    private readonly TransferManager _transferManager = null!;
    
    [Dependency]
    private readonly DbTransferFileBackupManager _backupManager = null!;

    [Dependency]
    private readonly IMultiplayerManagerBase _multiplayerManager = null!;

    public DbTransferFileManager(ITorchBase torchInstance) : base(torchInstance)
    {
    }

    public override void Attach()
    {
        base.Attach();
        _multiplayerManager.PlayerJoined += MultiplayerManagerOnPlayerJoined;
    }
    
    private void MultiplayerManagerOnPlayerJoined(IPlayer player)
    {
        Parallel.Start(() => ProcessJump(player.SteamId));
    }

    private void ProcessJump(decimal clientId)
    {
        if (!_dbManager.Db.Exists<Transfer>(clientId))
            return;
        
        var transfer = _dbManager.Db.Single<Transfer>(clientId);

        if (!_discoveryManager.IsLocalGate(transfer.DestinationWormhole))
            return;
        
        Log.Info($"Processing transfer for {clientId} to {transfer.DestinationWormhole}");

        var transferInfo = new TransferInfo(transfer);
        _dbManager.Db.Delete<Transfer>(clientId);

        TransferFile file;
        using (_ = _dbManager.Connection.BeginTransaction())
        using (var dbStream = _dbManager.ObjectManager.OpenRead(transferInfo.FileOid))
            file = Serializer.Deserialize<TransferFile>(dbStream);
        
        _transferManager.QueueIncomingTransfer(file, transferInfo);
        Log.Info($"Queued transfer: {transferInfo.CreateLogString()}");
        
        _backupManager.BackupTransfer(transferInfo);
    }

    public void CreateTransfer(TransferFileInfo transferInfo, TransferFile transferFile)
    {
        if (_dbManager.Db.Exists<Transfer>((decimal)transferInfo.SteamUserId))
        {
            MyVisualScriptLogicProvider.ShowNotification("Error: you already have a transfer pending", 15000, MyFontEnum.Red, Sync.Players.TryGetPlayerIdentity(transferInfo.SteamUserId).IdentityId);
            Log.Info($"Denied jumping for {transferInfo.PlayerName} ({transferInfo.SteamUserId}) a transfer already pending");
            return;
        }
        
        var fileOid = _dbManager.ObjectManager.Create();

        using (var t = _dbManager.Connection.BeginTransaction())
        {
            using (var dbStream = _dbManager.ObjectManager.OpenReadWrite(fileOid))
                Serializer.Serialize(dbStream, transferFile);
            t.Commit();
        }

        _dbManager.Db.Insert(new Transfer
        {
            ClientId = transferInfo.SteamUserId,
            PlayerName = transferInfo.PlayerName,
            GridName = transferInfo.GridName,
            DestinationWormhole = transferInfo.DestinationWormhole,
            File = fileOid
        });
        
        Log.Info($"Uploaded transfer to remote host, fileOid={fileOid}");
    }
}

public class TransferInfo : TransferFileInfo
{
    public TransferInfo(Transfer transfer)
    {
        SteamUserId = (ulong)transfer.ClientId;
        PlayerName = transfer.PlayerName;
        GridName = transfer.GridName;
        DestinationWormhole = transfer.DestinationWormhole;
        FileOid = transfer.File;
    }
    public uint FileOid;

    public override string CreateLogString()
    {
        return base.CreateLogString() + $"fileOid: {FileOid};";
    }
}
