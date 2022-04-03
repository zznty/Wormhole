using System.Collections.Generic;
using Sandbox.Game.Entities;
using Torch.API.Event;
using Torch.Event;
using Wormhole.ViewModels;

namespace Wormhole.Managers.Events;

public record IngoingGridTransferEvent(TransferFileInfo TransferInfo, GateDestinationViewModel Gate, TransferFile File) : IEvent
{
    public bool Cancelled { get; set; }
}

public record IngoingGridSpawnedEvent(TransferFileInfo TransferInfo, GateDestinationViewModel Gate, ICollection<MyCubeGrid> Grids) : IEvent
{
    public bool Cancelled => false;
}

public record OutgoingGridTransferEvent(TransferFileInfo TransferInfo, GateDestinationViewModel Gate, IList<MyCubeGrid> Grids) : IEvent
{
    public bool Cancelled => CancelMessage is not null;
    public string CancelMessage { get; set; }
}

public record InternalGridTransferEvent(TransferFileInfo TransferInfo, DestinationViewModel Dest, ICollection<MyCubeGrid> Grids) : IEvent
{
    public bool Cancelled => CancelMessage is not null;
    public string CancelMessage { get; set; }
}

[EventShim]
internal static class GridTransferEventShim
{
    private static readonly EventList<IngoingGridTransferEvent> IngoingGridTransferEvents = new();
    private static readonly EventList<IngoingGridSpawnedEvent> IngoingGridSpawnedEvents = new();
    private static readonly EventList<OutgoingGridTransferEvent> OutgoingGridTransferEvents = new();
    private static readonly EventList<InternalGridTransferEvent> InternalGridTransferEvents = new();

    public static void RaiseEvent(ref IngoingGridTransferEvent info)
    {
        IngoingGridTransferEvents.RaiseEvent(ref info);
    }
    
    public static void RaiseEvent(ref IngoingGridSpawnedEvent info)
    {
        IngoingGridSpawnedEvents.RaiseEvent(ref info);
    }
    
    public static void RaiseEvent(ref OutgoingGridTransferEvent info)
    {
        OutgoingGridTransferEvents.RaiseEvent(ref info);
    }
    
    public static void RaiseEvent(ref InternalGridTransferEvent info)
    {
        InternalGridTransferEvents.RaiseEvent(ref info);
    }
}