using CascadeEsdm.SharedKernel.Infrastructure.Storage;

namespace Cascade.Example.BuildContext.Domain;

public class EventStreamContainer : IEventStreamContainer
{
    public string Name { get; } = "event-stream";
    public int TimeToLive { get; } = 0;
}