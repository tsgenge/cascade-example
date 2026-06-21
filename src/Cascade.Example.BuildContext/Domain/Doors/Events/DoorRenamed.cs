using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Hydration;

namespace Cascade.Example.BuildContext.Domain.Doors.Events;

public record DoorRenamed(string Name) : IDomainEvent;

internal class DoorRenamedApplier : IEventApplier<DoorRenamed, DoorAggregate>
{
    public void Apply(DoorAggregate aggregate, DoorRenamed @event, EventEnvelope envelope)
    {
        aggregate.Door.Name = new(@event.Name);
    }
}
