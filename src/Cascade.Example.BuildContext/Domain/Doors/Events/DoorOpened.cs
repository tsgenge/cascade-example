using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Hydration;

namespace Cascade.Example.BuildContext.Domain.Doors.Events;

public record DoorOpened() : IDomainEvent;

internal class DoorOpenedApplier : IEventApplier<DoorOpened, DoorAggregate>
{
    public void Apply(DoorAggregate aggregate, DoorOpened @event, EventEnvelope envelope)
    {
        aggregate.Door.IsOpen = true;
    }
}