namespace Wormhole;

public class TransferFileInfo
{
    public string DestinationWormhole;
    public string GridName;
    public string PlayerName;
    public ulong SteamUserId;

    public static TransferFileInfo ParseFileName(string path)
    {
        TransferFileInfo info = new ();
        var pathItems = path.Split('_');
        if (pathItems.Length != 4) return null;

        info.DestinationWormhole = pathItems[0];
        info.SteamUserId = ulong.Parse(pathItems[1]);
        info.PlayerName = pathItems[2];

        var lastPart = pathItems[3];
        if (lastPart.EndsWith(".sbcB5")) lastPart = lastPart.Substring(0, lastPart.Length - ".sbcB5".Length);
        info.GridName = lastPart;

        return info;
    }

    public string CreateLogString()
    {
        return
            $"dest: {DestinationWormhole};steamid: {SteamUserId};playername: {PlayerName};gridName: {GridName};";
    }

    public string CreateFileName()
    {
        return
            $"{DestinationWormhole}_{SteamUserId}_{Utilities.LegalCharOnly(PlayerName)}_{Utilities.LegalCharOnly(GridName)}";
    }
}