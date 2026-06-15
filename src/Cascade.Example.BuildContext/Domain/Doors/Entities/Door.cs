using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;

namespace Cascade.Example.BuildContext.Domain.Doors.Entities;

public class Door
{
    public DoorId Id { get; init; }
    public DoorName Name { get; init; }
    public bool IsOpen { get; init; }

    public bool Exists => Id == DoorId.Empty;

    public static Door Empty => new Door
    {
        Id = DoorId.Empty,
        Name = DoorName.Empty,
        IsOpen = false,
    };
}