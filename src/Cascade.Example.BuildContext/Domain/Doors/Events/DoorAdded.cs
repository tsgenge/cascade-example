using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Hydration;

namespace Cascade.Example.BuildContext.Domain.Doors.Events;

public record DoorAdded(Guid DoorId, string Name) : IDomainEvent;

internal class DoorAddedApplier : IEventApplier<DoorAdded, DoorAggregate>
{
    public void Apply(DoorAggregate aggregate, DoorAdded @event, EventEnvelope envelope)
    {
        aggregate.Id = @event.DoorId;
        aggregate.Door.Id = new(@event.DoorId);
        aggregate.Door.Name = new(@event.Name);
        aggregate.Door.IsOpen = false;
    }
}
