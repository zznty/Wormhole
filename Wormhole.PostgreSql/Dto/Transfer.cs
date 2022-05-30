using PetaPoco;
namespace Wormhole.PostgreSql.Dto;

[TableName("transfer")]
[PrimaryKey("clientId", AutoIncrement = false)]
[ExplicitColumns]
public class Transfer
{
    [Column]
    public decimal ClientId { get; set; }
    [Column]
    public string PlayerName { get; set; } = string.Empty;
    [Column]
    public string GridName { get; set; } = string.Empty;
    [Column]
    public string DestinationWormhole { get; set; } = string.Empty;
    [Column]
    public uint File { get; set; }
}
