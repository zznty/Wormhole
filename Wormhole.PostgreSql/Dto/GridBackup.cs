using PetaPoco;
namespace Wormhole.PostgreSql.Dto;

[TableName("gridBackup")]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
public class GridBackup
{
    [Column]
    public int Id { get; set; }
    [Column]
    public decimal ClientId { get; set; }
    [Column]
    public string GridName { get; set; } = string.Empty;
    [Column]
    public DateTime BackupDate { get; set; } = DateTime.Now;
    [Column]
    public uint File { get; set; }
}
