using PetaPoco;
using Wormhole.ViewModels;
namespace Wormhole.PostgreSql.Dto;

[TableName("destinations")]
[PrimaryKey("uid", AutoIncrement = true)]
[ExplicitColumns]
public class Destination
{
    [Column]
    public int Uid { get; set; }
    [Column]
    public string DisplayName { get; set; } = string.Empty;
    [Column]
    public string Id { get; set; } = string.Empty;
    [Column]
    public bool AddWhitelist { get; set; }
    [Column]
    public bool RemoveWhitelist { get; set; }
    [Column(Name = "destination")]
    public string Dest { get; set; } = string.Empty;
    [Column]
    public bool IsInternal { get; set; }
    [Column]
    public uint GateId { get; set; }

    public static implicit operator DestinationViewModel(Destination dest)
    {
        var vm = DestinationViewModel.Create(dest.IsInternal ? DestinationType.InternalGps : DestinationType.Gate);
        
        vm.Id = dest.Id;
        vm.DisplayName = dest.DisplayName;
        vm.AddToDestinationWhitelist = dest.AddWhitelist;
        vm.RemoveFromSourceWhitelist = dest.RemoveWhitelist;
        switch (vm)
        {
            case InternalDestinationViewModel intDest:
                intDest.Gps = dest.Dest;
                break;
            case GateDestinationViewModel gateDest:
                gateDest.Name = dest.Dest;
                break;
        }
        return vm;
    }
}
