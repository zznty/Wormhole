using PetaPoco;
namespace Wormhole.PostgreSql.Dto;

[TableName("servers")]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class Server
{
    [Column]
    public int Id { get; set; }
    [Column]
    public string Ip { get; set; } = string.Empty;
}
