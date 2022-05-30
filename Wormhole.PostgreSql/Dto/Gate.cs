using PetaPoco;
using Torch.Collections;
using Torch.Utils;
using Wormhole.ViewModels;
namespace Wormhole.PostgreSql.Dto;

[TableName("gates")]
[PrimaryKey("id", AutoIncrement = false)]
[ExplicitColumns]
public class Gate
{
    [Column]
    public uint Id { get; set; }
    [Column]
    public string Name { get; set; } = string.Empty;
    [Column]
    public string Description { get; set; } = string.Empty;
    [Column]
    public string HexColor { get; set; } = string.Empty;
    [Column]
    public double X { get; set; }
    [Column]
    public double Y { get; set; }
    [Column]
    public double Z { get; set; }
    [Column]
    public int ServerId { get; set; }

    public GateViewModel ToViewModel(IDatabase database)
    {
        var list = new MtObservableList<DestinationViewModel>();
        list.GetPrivateField<List<DestinationViewModel>>("Backing").AddRange(database.Fetch<Destination>("where \"gateId\" = @0", Id).Select<Destination, DestinationViewModel>(b => b));
        
        return new()
        {
            Name = Name,
            Description = Description,
            X = X,
            Y = Y,
            Z = Z,
            HexColor = HexColor,
            Destinations = list
        };
    }
}
