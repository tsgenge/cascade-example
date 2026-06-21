using Cascade.Example.Api.Extensions;
using Cascade.Example.BuildContext.Domain.Doors.Commands;
using Cascade.Example.BuildContext.Domain.Doors.ValueObjects;
using CascadeEsdm.SharedKernel.ValueObjects;
using CascadeEsdm.WriteModel;
using CascadeEsdm.WriteModel.CommandHandling;
using Microsoft.AspNetCore.Mvc;

namespace Cascade.Example.Api.Controllers;

[ApiController]
[Route("doors")]
public class DoorsController : ControllerBase
{
    private readonly ICommandHandler<AddDoor> _addDoor;
    private readonly ICommandHandler<RenameDoor> _renameDoor;
    private readonly ICommandHandler<OpenDoor> _openDoor;
    private readonly ICommandHandler<CloseDoor> _closeDoor;

    public DoorsController(
        ICommandHandler<AddDoor> addDoor,
        ICommandHandler<RenameDoor> renameDoor,
        ICommandHandler<OpenDoor> openDoor,
        ICommandHandler<CloseDoor> closeDoor)
    {
        _addDoor = addDoor;
        _renameDoor = renameDoor;
        _openDoor = openDoor;
        _closeDoor = closeDoor;
    }

    [HttpPost]
    public async Task<IActionResult> AddDoor(
        [FromBody] AddDoor command)
    {
        await _addDoor.HandleAsync(new CommandEnvelope<AddDoor>(
            command,
            HttpContext.ToAuthenticatedContext(),
            new ClientChannel("api")));

        return Accepted();
    }

    [HttpPut("{doorId}/name")]
    public async Task<IActionResult> RenameDoor(
        [FromRoute] Guid doorId,
        [FromBody] RenameDoorBody body)
    {
        await _renameDoor.HandleAsync(new CommandEnvelope<RenameDoor>(
            new RenameDoor(new(doorId), new(body.Name)),
            HttpContext.ToAuthenticatedContext(),
            new ClientChannel("api")));

        return Accepted();
    }

    [HttpPut("{doorId}/open")]
    public async Task<IActionResult> OpenDoor(
        [FromRoute] Guid doorId)
    {
        await _openDoor.HandleAsync(new CommandEnvelope<OpenDoor>(
            new OpenDoor(new(doorId)),
            HttpContext.ToAuthenticatedContext(),
            new ClientChannel("api")));

        return Accepted();
    }

    [HttpPut("{doorId}/close")]
    public async Task<IActionResult> CloseDoor(
        [FromRoute] Guid doorId)
    {
        await _closeDoor.HandleAsync(new CommandEnvelope<CloseDoor>(
            new CloseDoor(new(doorId)),
            HttpContext.ToAuthenticatedContext(),
            new ClientChannel("api")));

        return Accepted();
    }
}

public record RenameDoorBody(string Name);
