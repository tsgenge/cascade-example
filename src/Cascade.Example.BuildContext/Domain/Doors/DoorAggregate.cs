using Cascade.Example.BuildContext.Domain.Doors.Entities;
using CascadeEsdm.SharedKernel.Aggregates;

namespace Cascade.Example.BuildContext.Domain.Doors;

public class DoorAggregate : IAggregateRoot
{
    public Guid Id { get; set; }
    public Door Door { get; } = Door.Empty;
    public int LastSequence { get; set; }
}