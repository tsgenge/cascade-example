using CascadeEsdm.SharedKernel.ValueObjects;

namespace Cascade.Example.BuildContext.Domain.Doors.ValueObjects;

public record DoorId(Guid Value) : IValueObject<Guid>
{
    public bool IsEmpty => Value == DoorId.Empty.Value;
    public static DoorId Empty => new DoorId(Guid.Empty);
}