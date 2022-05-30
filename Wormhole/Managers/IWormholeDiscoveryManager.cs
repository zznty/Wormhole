using System.Collections.Generic;
using Torch.API.Managers;
using Wormhole.ViewModels;
namespace Wormhole.Managers;

public interface IWormholeDiscoveryManager : IManager
{
    IEnumerable<(string ip, IEnumerable<GateViewModel> gates)> Servers { get; }
    bool IsLocalGate(string name);
    GateViewModel GetGateByName(string name, out string ownerIp);
    void EnsureLatestDiscovery();
}
