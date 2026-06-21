using Cascade.Example.BuildContext.Domain.Doors.Events;
using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;
using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.SharedKernel.Security;
using CascadeEsdm.SharedKernel.ValueObjects;
using CascadeEsdm.WriteModel;
using CascadeEsdm.WriteModel.CommandHandling;
using CascadeEsdm.WriteModel.Exceptions;

namespace Cascade.Example.BuildContext.Domain.Doors.Commands;

public record RenameDoor(DoorId DoorId, DoorName Name) : ICommand
{
    public Subject GetSubject(ICommandEnvelope envelope) =>
        Subject.ForAggregate<DoorAggregate>(DoorId.Value);
}

internal class RenameDoorExecutor : ICommandExecutor<RenameDoor, DoorAggregate>
{
    public async IAsyncEnumerable<EventEnvelope> ExecuteAsync(
        ICommandEnvelope<RenameDoor> envelope, DoorAggregate aggregate)
    {
        if (!aggregate.Door.Exists)
            throw new NotFoundException("Door not found");

        if (aggregate.Door.Name == envelope.Command.Name)
        {
            await Task.CompletedTask;
            yield break;
        }

        yield return envelope.CreateEvent(
            new DoorRenamed(envelope.Command.Name.Value),
            aggregate);

        await Task.CompletedTask;
    }

    public Task<ISecurityDescriptor?> GetSecurityDescriptorAsync(
        ICommandEnvelope<RenameDoor> envelope, DoorAggregate aggregate) =>
        Task.FromResult<ISecurityDescriptor?>(null);
}
