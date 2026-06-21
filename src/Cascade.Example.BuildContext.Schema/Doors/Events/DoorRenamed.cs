using CascadeEsdm.SharedKernel.Events;

namespace Cascade.Example.BuildContext.Schema.Doors.Events;
public record DoorRenamed(string Name) : IDomainEvent;