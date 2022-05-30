using System.IO;
using System.Windows.Controls;
using NLog;
using Torch;
using Torch.API;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Views;
using Wormhole.PostgreSql.Managers;

namespace Wormhole.PostgreSql;

public class Plugin : TorchPluginBase, IWpfPlugin
{
    private Persistent<Config> _config = null!;

    public override void Init(ITorchBase torch)
    {
        base.Init(torch);
        _config = Persistent<Config>.Load(Path.Combine(StoragePath, "Wormhole.PostgreSql.cfg"));
        Wormhole.Plugin.Instance.SessionManagerFactoriesFactory = SessionManagerFactoriesFactory;
    }
    
    private IEnumerable<SessionManagerFactoryDel> SessionManagerFactoriesFactory()
    {
        LogManager.GetLogger("WormholePostgreSql").Info("Injecting session managers");
        
        yield return s => new DbManager(s.Torch, _config.Data);
        yield return s => new DbTransferFileManager(s.Torch);
        yield return s => new DbTransferFileBackupManager(s.Torch);
        yield return s => new DbWormholeDiscoveryManager(s.Torch, Wormhole.Plugin.Instance.Config);
    }

    public UserControl GetControl() => new PropertyGrid
    {
        Margin = new(3),
        DataContext = _config.Data
    };
}
