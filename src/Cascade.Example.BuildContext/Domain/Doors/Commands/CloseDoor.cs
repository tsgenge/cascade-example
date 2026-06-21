using Cascade.Example.BuildContext.Domain.Doors.Events;
using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;
using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.SharedKernel.Security;
using CascadeEsdm.SharedKernel.ValueObjects;
using CascadeEsdm.WriteModel;
using CascadeEsdm.WriteModel.CommandHandling;
using CascadeEsdm.WriteModel.Exceptions;

namespace Cascade.Example.BuildContext.Domain.Doors.Commands;

public record CloseDoor(DoorId DoorId) : ICommand
{
    public Subject GetSubject(ICommandEnvelope envelope) =>
        Subject.ForAggregate<DoorAggregate>(DoorId.Value);
}

internal class CloseDoorExecutor : ICommandExecutor<CloseDoor, DoorAggregate>
{
    public async IAsyncEnumerable<EventEnvelope> ExecuteAsync(
        ICommandEnvelope<CloseDoor> envelope, DoorAggregate aggregate)
    {
        if (!aggregate.Door.Exists)
            throw new NotFoundException("Door not found");

        if (aggregate.Door.IsOpen)
            yield return envelope.CreateEvent(new DoorClosed(), aggregate);

        await Task.CompletedTask;
    }

    public Task<ISecurityDescriptor?> GetSecurityDescriptorAsync(
        ICommandEnvelope<CloseDoor> envelope, DoorAggregate aggregate) =>
        Task.FromResult<ISecurityDescriptor?>(null);
}
