using System.IO;
using Torch.API;
using Torch.Managers;
namespace Wormhole.Managers;

public class FileTransferFsManager : Manager
{
    private readonly Config _config;
    private const string AdminGatesBackupFolder = "grids_backup";
    public const string AdminGatesFolder = "admingates";
    
    public DirectoryInfo AdminGatesDirectory { get; private set; }
    public DirectoryInfo AdminGatesBackupDirectory { get; private set; }
    
    public FileTransferFsManager(ITorchBase torchInstance, Config config) : base(torchInstance)
    {
        _config = config;
    }

    public override void Attach()
    {
        AdminGatesDirectory = new(Path.Combine(_config.Folder, AdminGatesFolder));
        AdminGatesBackupDirectory = new(Path.Combine(_config.Folder, AdminGatesBackupFolder));
        
        if (!AdminGatesDirectory.Exists)
            AdminGatesDirectory.Create();

        if (!AdminGatesBackupDirectory.Exists)
            AdminGatesBackupDirectory.Create();
    }
}
