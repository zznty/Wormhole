using System.Text;
using PetaPoco;
using ProtoBuf;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.World;
using Torch;
using Torch.API.Managers;
using Torch.Commands;
using Torch.Mod;
using Torch.Mod.Messages;
using VRageMath;
using Wormhole.Managers;
using Wormhole.PostgreSql.Dto;
using Wormhole.PostgreSql.Managers;
namespace Wormhole.PostgreSql;

[Category("wormhole")]
public class Commands : CommandModule
{
    private DbManager Manager => Context.Torch.CurrentSession.Managers.GetManager<DbManager>();
    private IDatabase Db => Manager.Db;
    
    [Command("backup list", "Find id of backup to restore it with `!wormhole backup restore` <id>")]
    public void List(ulong clientId = 0, string? searchPattern = null)
    {
        if (Context.Player is null && clientId is 0)
        {
            Context.Respond("You must specify clientId");
            return;
        }

        if (clientId is 0)
            clientId = Context.Player!.SteamUserId;

        var backups = Db.Fetch<GridBackup>(searchPattern is null ?
            Sql.Builder.Where("\"clientId\" = @0", (decimal)clientId) :
            Sql.Builder.Where("\"clientId\" = @0 and \"gridName\" like @1", (decimal)clientId,
                searchPattern.Replace('*', '%').Replace('?', '_')));
        
        var sb = new StringBuilder();

        if (Context.Player is null)
            sb.AppendLine("Available grids:");
        
        foreach (var gridBackup in backups)
        {
            sb.AppendLine($"{gridBackup.Id} --> {gridBackup.GridName} - {gridBackup.BackupDate:U}");
        }

        if (Context.Player is null)
            sb.AppendLine().AppendLine("Choose one of ids (first number)");

        if (Context.Player is null)
            Context.Respond(sb.ToString());
        else
            ModCommunication.SendMessageTo(new DialogMessage("Available backups", content: sb.ToString(), subtitle: "Choose one of ids (first number)"), Context.Player.SteamUserId);
    }

    [Command("backup restore", "Run `!wormhole backup list` first to find id of backup")]
    public void Restore(int id, bool trySpawnNearOwner = false)
    {
        if (!Db.Exists<GridBackup>(id))
        {
            Context.Respond("Backup with given id is not found");
            return;
        }

        var backup = Db.Single<GridBackup>(id);
        
        TransferFile file;
        using (_ = Manager.Connection.BeginTransaction())
        using (var dbStream = Manager.ObjectManager.OpenRead(backup.File))
            file = Serializer.Deserialize<TransferFile>(dbStream);

        Vector3D? targetPos;
        var ownerId = new MyPlayer.PlayerId((ulong)backup.ClientId);
        if (trySpawnNearOwner && Sync.Players.IsPlayerOnline(ref ownerId))
            targetPos = Sync.Players.TryGetPlayerBySteamId(ownerId.SteamId).Character?.PositionComp.GetPosition();
        else
            targetPos = Context.Player?.GetPosition();

        if (targetPos.HasValue)
            targetPos = Utilities.FindFreePos(new(targetPos.Value, 200), 100);
        
        if (!targetPos.HasValue)
        {
            Context.Respond($"Unable to get target position, try switching {nameof(trySpawnNearOwner)}");
            return;
        }

        var spawnManager = Context.Torch.CurrentSession.Managers.GetManager<SpawnManager>();
        
        spawnManager.RemapOwnership(file, ownerId.SteamId);
        Utilities.UpdateGridsPositionAndStop(file.Grids, targetPos.Value);
        spawnManager.SpawnGridsParallel(file.Grids);

        Context.Respond("Spawned.");
    }
}
