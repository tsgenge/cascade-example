using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Hydration;

namespace Cascade.Example.BuildContext.Domain.Doors.Events;

public record DoorClosed() : IDomainEvent;

internal class DoorClosedApplier : IEventApplier<DoorClosed, DoorAggregate>
{
    public void Apply(DoorAggregate aggregate, DoorClosed @event, EventEnvelope envelope)
    {
        aggregate.Door.IsOpen = false;
    }
}