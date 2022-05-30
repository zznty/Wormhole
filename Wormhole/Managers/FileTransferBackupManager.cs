using System.IO;
using NLog;
using Torch.API;
using Torch.Managers;
namespace Wormhole.Managers;

public class FileTransferBackupManager : Manager
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    
    [Dependency]
    private readonly FileTransferFsManager _fsManager = null!;

    public FileTransferBackupManager(ITorchBase torchInstance) : base(torchInstance)
    {
    }

    public void BackupTransfer(FileSystemEventArgs e)
    {
        var backupFileName = e.Name;
        if (File.Exists(Path.Combine(_fsManager.AdminGatesBackupDirectory.FullName, backupFileName)))
        {
            var transferString = Path.GetFileNameWithoutExtension(backupFileName);
            var i = 0;
            do
            {
                backupFileName = $"{transferString}_{++i}.sbcB5";
            } while (File.Exists(Path.Combine(_fsManager.AdminGatesBackupDirectory.FullName, backupFileName)));
        }
        
        Log.Info($"Creating transfer backup {backupFileName}");
        File.Copy(e.FullPath, Path.Combine(_fsManager.AdminGatesBackupDirectory.FullName, backupFileName));
    }
}
