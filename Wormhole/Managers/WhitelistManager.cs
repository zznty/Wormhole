using Torch.API;
using Torch.API.Event;
using Torch.Managers;
using Torch.Server;
using Wormhole.Managers.Events;
namespace Wormhole.Managers;

public class WhitelistManager : Manager, IEventHandler
{
    [Dependency] private IEventManager _eventManager = null!;

    public WhitelistManager(ITorchBase torchInstance) : base(torchInstance)
    {
    }

    public override void Attach()
    {
        base.Attach();
        _eventManager.RegisterHandler(this);
    }

    [EventHandler]
    private void OnOutgoingGridTransfer(ref OutgoingGridTransferEvent info)
    {
        var config = (TorchConfig)Torch.Config;

        if (!info.Gate.RemoveFromSourceWhitelist || !config.EnableWhitelist)
            return;
        
        foreach (var clientId in Utilities.GetAllCharactersClientIds(info.Grids))
        {
            config.Whitelist.Remove(clientId);
        }
    }

    [EventHandler]
    private void OnIngoingGridSpawned(ref IngoingGridTransferEvent info)
    {
        var config = (TorchConfig)Torch.Config;

        if (!info.Gate.AddToDestinationWhitelist || !config.EnableWhitelist)
            return;
        
        foreach (var clientId in info.File.PlayerIdsMap.Values)
        {
            if (!config.Whitelist.Contains(clientId))
                config.Whitelist.Add(clientId);
        }
    }
}
