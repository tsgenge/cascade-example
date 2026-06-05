# Cascade ESDM — AI Agent Context

This file provides context and best practices for AI agents working in a project that uses the Cascade ESDM framework.

---

## What Cascade ESDM Is

Cascade ESDM is an opinionated C# framework for **Event Sourced Domain Model** systems. Engineers implement commands, events, and projections. The framework handles dispatch, hydration, concurrency, and storage.

Engineers **do not** wire up command handlers manually, manage event streams directly, or implement concurrency locking themselves — the framework owns those concerns.

---

## Package Overview

| Package | Purpose |
|---|---|
| `CascadeEsdm.SharedKernel.Abstractions` | Core interfaces: `IDomainEvent`, `IAggregateRoot`, `IEventApplier`, `IValueObject<T>`, `Subject`, `EventEnvelope` |
| `CascadeEsdm.SharedKernel` | Base implementations for aggregates and shared kernel types |
| `CascadeEsdm.WriteModel.Abstractions` | Write-side interfaces: `ICommand`, `ICommandExecutor`, `ICommandEnvelope` |
| `CascadeEsdm.WriteModel` | Command dispatch, aggregate hydration, event stream writing, concurrency, MSBuild integration |
| `CascadeEsdm.ReadModel.Abstractions` | Read-side interfaces for projections and queries |
| `CascadeEsdm.ReadModel` | Read model infrastructure |
| `CascadeEsdm.Storage.CosmosDb` | Azure Cosmos DB event stream and read model storage |
| `CascadeEsdm.DistributedLocks` | Azure Storage distributed lock provider |
| `CascadeEsdm.Logging.OpenTelemetry` | OpenTelemetry structured logging / Application Insights |
| `CascadeEsdm.EventExtractor` | Pre-build tool — extracts `IDomainEvent` records into a publishable events assembly |

---

## Composition / Registration

Register Cascade in your DI host using the fluent builder. All three infrastructure components are **required** before the write model can be configured — the builder enforces this at startup:

```csharp
services.AddCascadeEsdm(cascade => cascade
    .WithInfrastructure(infra => infra
        .UseCosmosDbStorage<AppConfig>(storage => storage
            .EventStreamContainer<EventStreamContainer>()
            .WithContainer<ReadModelContainer>())
        .UseAzureDistributedLocks<AppConfig>(config => config.StorageConnectionString)
        .UseApplicationInsights())
    .WithWriteModel(write => write
        .RegisterWriteModel()
        .RegisterCommandsFromAssembly<MyAggregate>()));
```

`RegisterCommandsFromAssembly<TMarker>()` auto-discovers all aggregates, commands, executors, and handlers in the assembly by convention. It throws `MissingExecutorException` if any command is missing its executor.

Container definitions implement `IDocumentContainerDefinition`:

```csharp
public class EventStreamContainer : IDocumentContainerDefinition
{
    public string Name => "eventstreams";
    public string PartitionKeyPath => "/partitionKey";
}
```

---

## Commands

- Implement `ICommand`
- Define `GetSubject(ICommandEnvelope)` — returns the `Subject` identifying the target aggregate
- Commands are `record` types
- Executors are `internal class` types implementing `ICommandExecutor<TCommand, TAggregate>`
- `ExecuteAsync` yields `EventEnvelope` instances via `IAsyncEnumerable`
- `GetSecurityDescriptorAsync` returns the security descriptor (or `null` if no auth required)

```csharp
public record PlaceOrder(OrderId OrderId, string Reference) : ICommand
{
    public Subject GetSubject(ICommandEnvelope envelope) =>
        Subject.ForAggregate<OrderAggregate>(OrderId.Value);
}

internal class PlaceOrderExecutor : ICommandExecutor<PlaceOrder, OrderAggregate>
{
    public async IAsyncEnumerable<EventEnvelope> ExecuteAsync(
        ICommandEnvelope<PlaceOrder> envelope, OrderAggregate aggregate)
    {
        if (aggregate.Exists)
            throw new ConflictException("Order already exists");

        yield return envelope.CreateEvent(
            new OrderPlaced(envelope.Command.OrderId.Value, envelope.Command.Reference),
            aggregate);
    }

    public Task<ISecurityDescriptor?> GetSecurityDescriptorAsync(
        ICommandEnvelope<PlaceOrder> envelope, OrderAggregate aggregate) =>
        Task.FromResult<ISecurityDescriptor?>(null);
}
```

### Concurrency locking

Apply `[CommandLock]` to a command to acquire a distributed lock before execution. The lock is scoped to the subject (aggregate-level) or to the subject + command type (command-level).

---

## Events

- Implement `IDomainEvent`
- Events are `record` types
- Defined alongside their appliers in the write model
- **Use primitives in event parameters wherever possible** — avoids type graph leakage into published contracts

```csharp
public record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;
```

### Event Appliers

Implement `IEventApplier<TEvent, TAggregate>`. Co-locate the applier with the event record:

```csharp
internal class OrderPlacedApplier : IEventApplier<OrderPlaced, OrderAggregate>
{
    public void Apply(OrderAggregate aggregate, OrderPlaced @event, EventEnvelope envelope)
    {
        aggregate.OrderId = new(@event.OrderId);
        aggregate.Reference = @event.Reference;
        aggregate.Exists = true;
    }
}
```

### Inheritance constraint

The Event Extractor is syntactic — it only extracts records where `IDomainEvent` appears **literally in the record's own base list**. Do not rely on inherited interface satisfaction:

```csharp
// Extracted — IDomainEvent is in the base list
public record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;

// NOT extracted — IDomainEvent is not directly in the base list
public record OrderPlaced(Guid OrderId, string Reference) : OrderEventBase(OrderId);
```

---

## Aggregates

- Implement `IAggregateRoot`
- Aggregates are hydrated by replaying events from the event stream — never loaded from a snapshot of their properties
- State is mutated only inside `IEventApplier.Apply()` — never inside command executors
- Command executors receive the already-hydrated aggregate; they read state and yield events

---

## Value Objects

- Implement `IValueObject<TValue>`, immutable
- Use primary constructors
- Implement implicit conversion operators to/from the underlying primitive
- For ID-style VOs: provide `static Empty` and `static bool IsEmpty` semantics
- Instantiate with `new(value)` syntax

```csharp
public record OrderId(Guid Value) : IValueObject<Guid>
{
    public static OrderId Empty => new(Guid.Empty);
    public static bool IsEmpty(OrderId id) => id.Value == Guid.Empty;

    public static implicit operator OrderId(Guid value) => new(value);
    public static implicit operator Guid(OrderId id) => id.Value;
}
```

---

## Subject

`Subject` is the aggregate identity value object. Format: `Type/[parentId/]id`.

```csharp
Subject.ForAggregate<OrderAggregate>(orderId);
```

---

## Exceptions

- All exceptions inherit from `ExceptionBase`
- Each exception defines an appropriate `HttpStatusCode`
- Common exceptions (`ConflictException`, `NotFoundException`, `UnauthorisedException`, etc.) are in `CascadeEsdm.WriteModel.Abstractions`
- New exceptions go in the aggregate's `/Exceptions` directory, or a shared location if reused
- API endpoints rely on middleware to translate exceptions to HTTP responses
- Queue consumers move messages that throw to a dead letter queue with the exception message as `DeadLetterReason`

---

## Event Extractor

The `CascadeEsdm.EventExtractor` dotnet tool runs at pre-build time and generates a clean, dependency-light events assembly from your write model:

```bash
dotnet tool install -g CascadeEsdm.EventExtractor
```

From your next build, an events project is generated:

```
MyApp.WriteModel/
MyApp.Schema/           ← generated, add to source control
  MyApp.Schema.csproj
  Orders/
    Events/
      OrderPlaced.cs
```

### Key configurable MSBuild properties

| Property | Default | Description |
|---|---|---|
| `CascadeEventsEnabled` | `true` | Set to `false` to disable extraction |
| `CascadeEventsOutputDir` | `../<AssemblyName>.Schema` | Output directory |
| `CascadeEventsAssemblyName` | RootNamespace with write-model suffix stripped + `.Schema` | Generated assembly name |
| `CascadeEventsOverwrite` | `false` | Regenerate all files on every build |
| `CascadeEventsRequireExtractor` | `false` | Promote missing tool to build error |

### Service Bus serialisation

Use `DefaultSerialisationSettings.ForServiceBusPublishing()` when serialising `EventEnvelope` for service bus publishing. This rewrites `$type` from the write-model identity to the schema assembly identity automatically — no configuration required.

---

## Code Style

- XML doc comments (`///`) only on `public` members of `public` types
- No inline comments (`//`) anywhere — code should speak for itself
- No comments on non-public members
