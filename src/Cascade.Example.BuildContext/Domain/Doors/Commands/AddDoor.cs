using Cascade.Example.BuildContext.Domain.Doors.Events;
using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;
using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.SharedKernel.Exceptions;
using CascadeEsdm.SharedKernel.Security;
using CascadeEsdm.SharedKernel.ValueObjects;
using CascadeEsdm.WriteModel;
using CascadeEsdm.WriteModel.CommandHandling;

namespace Cascade.Example.BuildContext.Domain.Doors.Commands;

public record AddDoor(DoorId DoorId, DoorName Name) : ICommand
{
    public Subject GetSubject(ICommandEnvelope envelope) =>
        Subject.ForAggregate<DoorAggregate>(DoorId.Value);
}

internal class AddDoorExecutor : ICommandExecutor<AddDoor, DoorAggregate>
{
    public async IAsyncEnumerable<EventEnvelope> ExecuteAsync(
        ICommandEnvelope<AddDoor> envelope, DoorAggregate aggregate)
    {
        if (aggregate.Door.Exists)
            throw new ConflictException("Door already exists");

        yield return envelope.CreateEvent(
            new DoorAdded(envelope.Command.DoorId.Value, envelope.Command.Name.Value),
            aggregate);

        await Task.CompletedTask;
    }

    public Task<ISecurityDescriptor?> GetSecurityDescriptorAsync(
        ICommandEnvelope<AddDoor> envelope, DoorAggregate aggregate) =>
        Task.FromResult<ISecurityDescriptor?>(null);
}
