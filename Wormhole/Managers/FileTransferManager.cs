using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using NLog;
using ProtoBuf;
using Sandbox.Game.Multiplayer;
using Torch.API;
using Torch.Managers;
namespace Wormhole.Managers;

public class FileTransferManager : Manager, IFileTransferManager
{
    private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
    
    [Dependency]
    private readonly FileTransferFsManager _fsManager = null!;

    [Dependency]
    private readonly FileTransferBackupManager _backupManager = null!;
    
    [Dependency]
    private readonly TransferManager _transferManager = null!;

    private readonly Timer _timer;
    
    public FileTransferManager(ITorchBase torchInstance) : base(torchInstance)
    {
        _timer = new(Callback);
    }
    private void Callback(object state)
    {
        foreach (var file in _fsManager.AdminGatesDirectory.GetFiles("*.sbcB5"))
        {
            ProcessTransferFile(new(WatcherChangeTypes.Created, file.DirectoryName!, file.Name));
        }
    }

    public override void Attach()
    {
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }
    public override void Detach()
    {
        _timer.Dispose();
    }
    
    private void ProcessTransferFile(FileSystemEventArgs e)
    {
        Log.Info($"Processing recivied grid: {e.Name}");
        var info = TransferFileInfo.ParseFileName(e.Name);
        
        if (info is null)
        {
            Log.Error("Error parsing file name");
            return;
        }
        
        TransferFile transferFile;
        try
        {
            using var stream = File.OpenRead(e.FullPath);
            using var decompressStream = new GZipStream(stream, CompressionMode.Decompress);

            transferFile = Serializer.Deserialize<TransferFile>(decompressStream);
            if (transferFile.Grids is null || transferFile.IdentitiesMap is null)
                throw new InvalidOperationException("File is empty or invalid");
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Corrupted file at {e.Name}");
            return;
        }

        if (Sync.Players.TryGetPlayerIdentity(new (info.SteamUserId))?.Character is { } character)
            Utilities.KillCharacter(character);

        _transferManager.QueueIncomingTransfer(transferFile, info);
        _backupManager.BackupTransfer(e);
        
        File.Delete(e.FullPath);
    }
    public void CreateTransfer(TransferFileInfo transferInfo, TransferFile transferFile)
    {
        using var stream = File.Create(Utilities.CreateBlueprintPath(_fsManager.AdminGatesDirectory.FullName, transferInfo.CreateFileName()));
        using var compressStream = new GZipStream(stream, CompressionMode.Compress);
        
        Serializer.Serialize(compressStream, transferFile);
    }
}