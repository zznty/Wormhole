using System;
using System.Threading.Tasks;
using NLog;
using SteamQueryNet;
using Torch.API;
using Torch.Managers;

namespace Wormhole.Managers
{
    public class ServerQueryManager : Manager
    {
        private static readonly ILogger Log = LogManager.GetCurrentClassLogger();
        public ServerQueryManager(ITorchBase torchInstance) : base(torchInstance)
        {
        }

        public async Task<ServerStatus> GetServerStatus(string address)
        {
            try
            {
                using var query = new ServerQuery(address);
                var info = await query.GetServerInfoAsync();

                return info.Players >= info.MaxPlayers ? ServerStatus.Full : ServerStatus.CanAccept;
            }
            catch (TimeoutException)
            {
                Log.Warn($"Request to {address} timed out");
                return ServerStatus.RequestTimeout;
            }
            catch (Exception e)
            {
                Log.Error(e, $"Excetion ocoured in server status request to {address}");
                return ServerStatus.UnknownError;
            }
        }
    }

    public enum ServerStatus
    {
        CanAccept,
        Full,
        RequestTimeout,
        // TODO: maybe later
        Loading,
        UnknownError
    }
}