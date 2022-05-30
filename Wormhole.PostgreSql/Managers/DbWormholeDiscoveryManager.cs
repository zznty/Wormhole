using Npgsql;
using NpgsqlTypes;
using PetaPoco;
using Torch.API;
using Torch.Managers;
using VRage.Security;
using Wormhole.Managers;
using Wormhole.PostgreSql.Dto;
using Wormhole.ViewModels;
namespace Wormhole.PostgreSql.Managers;

public class DbWormholeDiscoveryManager : Manager, IWormholeDiscoveryManager
{
    private readonly Wormhole.Config _config;
    [Dependency]
    private readonly DbManager _dbManager = null!;

    public DbWormholeDiscoveryManager(ITorchBase torchInstance, Wormhole.Config config) : base(torchInstance)
    {
        _config = config;
    }
    public override void Attach()
    {
        base.Attach();
        EnsureLatestDiscovery();
    }
    public IEnumerable<(string ip, IEnumerable<GateViewModel> gates)> Servers => _dbManager.Db.Fetch<Server>().Select(b => (b.Ip, _dbManager.Db.Fetch<Gate>(Sql.Builder.Where("serverId = @0", b.Id)).Select(g => g.ToViewModel(_dbManager.Db))));
    public bool IsLocalGate(string name)
    {
        var gate = _dbManager.Db.SingleOrDefault<Gate>(FnvHash.Compute(name)) ?? 
                   throw new InvalidOperationException($"Could not find gate with name {name}! Naming mistake?");
        return _dbManager.Db.Single<Server>(gate.ServerId).Ip == _config.ThisIp;
    }
    public GateViewModel GetGateByName(string name, out string ownerIp)
    {
        var gate = _dbManager.Db.SingleOrDefault<Gate>(FnvHash.Compute(name)) ?? 
                   throw new InvalidOperationException($"Could not find gate with name {name}! Naming mistake?");

        ownerIp = _dbManager.Db.Single<Server>(gate.ServerId).Ip;
        return gate.ToViewModel(_dbManager.Db);
    }
    public void EnsureLatestDiscovery()
    {
        EnsureGates(EnsureServer());
    }

    private Server EnsureServer()
    {
        _dbManager.Db.Execute("insert into servers (ip) values (@0) on conflict (ip) do nothing", _config.ThisIp);

        return _dbManager.Db.Single<Server>(Sql.Builder.Where("ip = @0", _config.ThisIp));
    }

    private void EnsureGates(Server thisServer)
    {
        foreach (var gate in _config.WormholeGates)
        {
            _dbManager.Db.Execute("insert into gates (id, name, description, \"hexColor\", x, y, z, \"serverId\") values (@0, @1, @2, @3, @4, @5, @6, @7) on conflict (id) do nothing",
                gate.Id, gate.Name, gate.Description, gate.HexColor, gate.X, gate.Y, gate.Z, thisServer.Id);

            EnsureDestinations(gate);
        }
    }
    private void EnsureDestinations(GateViewModel gate)
    {
        foreach (var destination in gate.Destinations)
        {
            _dbManager.Db.Execute("insert into destinations (\"displayName\", id, \"addWhitelist\", \"removeWhitelist\", destination, \"isInternal\", \"gateId\") values (@0, @1, @2, @3, @4, @5, @6) on conflict (id) do nothing",
                destination.DisplayName, destination.Id, destination.AddToDestinationWhitelist, destination.RemoveFromSourceWhitelist, destination switch
                {
                    InternalDestinationViewModel intDest => intDest.Gps,
                    GateDestinationViewModel gateDest => gateDest.Name,
                    _ => throw new ArgumentOutOfRangeException(nameof(destination), destination.GetType(), null)
                }, destination is InternalDestinationViewModel, gate.Id);
        }
    }
}
