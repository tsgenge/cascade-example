using System;
using CascadeEsdm.SharedKernel.Events;

namespace Cascade.Example.BuildContext.Schema.Doors.Events;
public record DoorAdded(Guid DoorId, string Name) : IDomainEvent;