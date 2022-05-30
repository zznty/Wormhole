using NLog;
using Npgsql;
using Torch.API;
using Torch.Managers;
using Wormhole.PostgreSql.Dto;
namespace Wormhole.PostgreSql.Managers;

public class DbTransferFileBackupManager : Manager
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    
    [Dependency]
    private readonly DbManager _dbManager = null!;
    public DbTransferFileBackupManager(ITorchBase torchInstance) : base(torchInstance)
    {
    }

    public void BackupTransfer(TransferInfo transferInfo)
    {
        _dbManager.Db.Insert(new GridBackup
        {
            ClientId = transferInfo.SteamUserId,
            GridName = transferInfo.GridName,
            File = transferInfo.FileOid
        });
        
        Log.Info($"Created backup for transferred grid {transferInfo.GridName} rquested by {transferInfo.SteamUserId}, fileOid: {transferInfo.FileOid}");
    }
}
