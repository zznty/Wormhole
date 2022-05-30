using Torch.API.Managers;
namespace Wormhole.Managers;

public interface IFileTransferManager : IManager
{
    void CreateTransfer(TransferFileInfo transferInfo, TransferFile transferFile);
}
