using CascadeEsdm.SharedKernel.ValueObjects;

namespace Cascade.Example.BuildContext.Domain.Doors.ValueObjects;

public record DoorName(string Value) : IValueObject<string>
{
    public bool IsEmpty => this.Value == DoorName.Empty.Value;
    public static DoorName Empty => new DoorName(string.Empty);
}