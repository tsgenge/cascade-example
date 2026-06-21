using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;

namespace Cascade.Example.BuildContext.Domain.Doors.Entities;

public class Door
{
    public DoorId Id { get; set; }
    public DoorName Name { get; set; }
    public bool IsOpen { get; set; }

    public bool Exists => Id != DoorId.Empty;

    public static Door Empty => new Door
    {
        Id = DoorId.Empty,
        Name = DoorName.Empty,
        IsOpen = false,
    };
}