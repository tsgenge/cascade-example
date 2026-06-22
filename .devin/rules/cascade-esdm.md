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
| `CascadeEsdm.Storage.Azure` | Azure Table Storage implementation of `ITableStore<TEntity>` |
| `CascadeEsdm.DistributedLocks` | Azure Storage distributed lock provider |
| `CascadeEsdm.Logging.OpenTelemetry` | OpenTelemetry structured logging / Application Insights |
| `CascadeEsdm.EventExtractor` | Pre-build tool — extracts `IDomainEvent` records into a publishable events assembly |

---

## Composition / Registration

Register Cascade in your DI host using the fluent builder. All three infrastructure components are **required** before the write model can be configured — the builder enforces this at startup:

```csharp
services.AddCascadeEsdm(cascade => cascade
    .WithInfrastructure(infra => infra
        .UsingCosmosDbStorage(storage => storage
            .WithConnectionString(connectionString)
            .WithDatabaseName("cascade")
            .WithEventStreamContainer<EventStreamContainer>())
        .UsingAzureDistributedLocks(locks => locks
            .WithConnectionString(azuriteConnectionString))
        .UsingApplicationInsights())
    .WithWriteModel(write => write
        .UsingExecutors(executors => executors
            .AddCommandExecutor<AddPersonExecutor>()
            .AddCommandExecutor<ChangePersonFirstNameExecutor>())
        .UsingAppliers(appliers => appliers
            .AddEventApplier<PersonAddedApplier>()
            .AddEventApplier<PersonFirstNameChangedApplier>())
        .WithPolicies(policies => policies
            .AddPolicy<SendWelcomeEmailPolicy>())));
```

### Entry Point

`services.AddCascadeEsdm(cascade => { ... })` creates a `CascadeBuilder` and allows you to configure the entire system.

### Infrastructure Configuration

The infrastructure builder requires three components:
- **Storage Provider**: Where events and read models are stored
- **Distributed Lock Provider**: For handling concurrency
- **Telemetry Logger**: For observability

#### Storage Configuration

```csharp
infra.UsingCosmosDbStorage(storage => storage
    .WithConnectionString(connectionString)
    .WithDatabaseName("cascade")
    .WithEventStreamContainer<EventStreamContainer>())
```

- `WithConnectionString(string)`: **Required** - Cosmos DB connection string
- `WithDatabaseName(string)`: **Required** - Database name (defaults to "cascade")
- `WithEventStreamContainer<TContainer>()`: **Required** - Specifies the container for event streams
- `WithOptions(CosmosClientOptions)`: Optional - Configure Cosmos client options

The `TContainer` type must implement `IDocumentContainerDefinition` and have a parameterless constructor:

```csharp
public class EventStreamContainer : IDocumentContainerDefinition
{
    public string Name => "eventstreams";
    public string PartitionKeyPath => "/partitionKey";
}
```

#### Distributed Locks Configuration

```csharp
infra.UsingAzureDistributedLocks(locks => locks
    .WithConnectionString(connectionString))
```

- `WithConnectionString(string)`: **Required** - Azure Storage connection string for distributed locks

#### Telemetry Configuration

```csharp
infra.UsingApplicationInsights()
```

Registers OpenTelemetry-based logging with Application Insights.

#### SignalR Configuration (Optional)

```csharp
infra.UseSignalR(signalR => signalR
    .ConfigureSignalROptions(options => { ... }))
```

Configures SignalR for real-time view change notifications.

### Write Model Configuration

```csharp
cascade.WithWriteModel(write => { ... })
```

After infrastructure is configured, you can register the write model components.

#### Register Command Executors

```csharp
write.UsingExecutors(executors => executors
    .AddCommandExecutor<TExecutor>()
    .AddCommandExecutor<TExecutor2>())
```

This registers:
- Command authorizers
- Aggregate factories and hydrators
- Event applier factories
- Event stream readers/writers
- Command handler decorators (logging, event writing, serialization)
- The specified command executors

| Method | Description |
|---|---|
| `AddCommandExecutor<TExecutor>()` | Registers a single executor; `TCommand` and `TAggregate` are inferred via reflection from the executor's `ICommandExecutor<,>` interface |
| `AddCommandsFromAssembly<TExampleType>()` | Discovers and registers all commands and their executors in the assembly containing `TExampleType`; throws `MissingExecutorException` if any command has no matching executor |

```csharp
// Or register all commands in an assembly at once:
write.UsingExecutors(executors => executors
    .AddCommandsFromAssembly<OrderAggregate>())
```

#### Register Event Appliers

```csharp
write.UsingAppliers(appliers => appliers
    .AddEventApplier<TApplier>()
    .AddEventApplier<TApplier2>())
```

Registers the specified event appliers for handling events during aggregate hydration. `TEvent` and `TAggregate` are inferred via reflection from the applier's `IEventApplier<,>` interface.

| Method | Description |
|---|---|
| `AddEventApplier<TApplier>()` | Registers a single applier; `TEvent` and `TAggregate` are inferred via reflection |
| `AddEventAppliersFromAssembly<TExampleType>()` | Discovers and registers all `IEventApplier<,>` implementations in the assembly containing `TExampleType` |

#### Register Policies

```csharp
write.WithPolicies(policies => policies
    .AddPolicy<SendWelcomeEmailPolicy>()
    .AddPoliciesFromAssembly<PersonAggregate>()
    .AddPoliciesFromNamespace<SendWelcomeEmailPolicy>())
```

Registers reactive policies that execute in response to domain events. See the [Policies](#policies) section for implementation details.

### Read Model Configuration

```csharp
cascade.WithReadModel(read => read
    .WithViews(views => views
        .AddView<OrderView, ViewsContainer>()))
```

Registers read model projections. `WithViews` requires a notification service to have been registered in `WithInfrastructure()` (e.g. via `UseSignalR`).

| Method | Description |
|---|---|
| `AddView<TView, TContainer>()` | Registers a single view backed by the specified container |
| `AddViewsFromAssembly<TExampleType>(getContainer)` | Discovers all `IView` implementations in the assembly and resolves each container via the provided delegate; throws if any view has no container resolved |

```csharp
// Or using assembly scanning:
read.WithViews(views => views
    .AddViewsFromAssembly<OrderView>(viewType => typeof(ViewsContainer)))
```

### Validation

The system validates that all required infrastructure components are registered before allowing write model configuration. If any component is missing, an `InvalidOperationException` is thrown with a clear message indicating what's missing:

```
Missing required infrastructure components: Storage Provider, Distributed Lock Provider, Telemetry Logger, Event Stream Container. Ensure you have called the appropriate Use* methods on the infrastructure builder.
```

---

## Aggregates

### Purpose

The aggregate provides the transactional boundary for domain write operations and enforces business rules. Aggregates contain commands which emit events.

Aggregates should be as small as possible; if an aggregate needs information from another to make a decision, consider the architecture of your aggregates — a merge may be required. BUT, it's easier to merge aggregates than split them later.

### Standards

- Aggregates are the root entry points of the domain model.
- Aggregates are containers for Entities.
- Entities within an aggregate are collections of ValueObjects.
- Entities are mutable.
- Entities are exposed as public properties on the aggregate to allow mutation during event application (Hydration).
- ValueObjects are immutable.
- Aggregates implement `IAggregateRoot`.
- Aggregates are hydrated by replaying events from the event stream.
- State is mutated only inside `IEventApplier.Apply()` — never inside command executors.
- Command executors receive the already-hydrated aggregate; they read state and yield events.
- Command executors never mutate state directly.

### Folder Structure

```
Domain/
  Orders/                         ← aggregate directory (pluralised)
    OrderAggregate.cs
    Entities/                     ← entity subdirectory
      OrderItem.cs
    ValueObjects/                 ← value object subdirectory
      OrderId.cs
      OrderReference.cs
    Commands/                     ← command subdirectory
      PlaceOrder.cs
    Events/                       ← event subdirectory
      OrderPlaced.cs
    Exceptions/                   ← aggregate-specific exceptions (optional)
      OrderAlreadyExistsException.cs
    Services/                     ← aggregate services (optional)
```

- Each aggregate has its own directory (pluralised) in the `/Domain` folder.
- Subdirectories for Entities, ValueObjects, Commands, and Events are required.
- Subdirectories for Services and Exceptions are optional.

---

## Commands

### Standards

- Commands are immutable record objects with primary constructors accepting only value objects (see [ValueObjects](#value-objects)), ensuring validity.
- Commands should not be shared across aggregates. Some commands may share a name (for example `SetSecurityDescriptor`) but each aggregate should have its own implementation to prevent coupling.
- If shared services need to recognise shared commands, use a shared interface in a common library (Shared Kernel).
- Place commands in the `Commands` folder within the aggregate directory.
- A command cannot exist without being valid due to the role of Value Objects in validation. This removes a huge amount of "logic" checking for validity in your domain.
- Commands implement `ICommand`, which requires a `GetSubject` method.
- Commands are created `public`.
- Commands that are not "Add" commands should include the ID of the aggregate as a property (as a value object) to allow formation of the Subject.
- Use the static factory methods of `Subject` in `GetSubject` for convenience. Since a command is always per aggregate, it always knows what aggregate it is for.

```csharp
public record PlaceOrder(OrderId OrderId, string Reference) : ICommand
{
    public Subject GetSubject(ICommandEnvelope envelope) =>
        Subject.ForAggregate<OrderAggregate>(OrderId.Value);
}
```

### Naming

- Name in the imperative as **VerbNoun** (e.g. `PlaceOrder`, `ChangePersonName`)
- Avoid CRUD terminology (`Create`, `Update`, `Delete`) — prefer `Add`, `Change`, `Remove`
- Name uniquely to prevent confusion (e.g. `ChangePersonName` not `ChangeName`)
- Commands don't need Time, Id, or other metadata — these are on `ICommandEnvelope`

### Command Executor

- Each command has a single `ICommandExecutor<TCommand, TAggregate>`, implemented in the **same file** as the command to ensure high topological cohesion.
- `ExecuteAsync` yields `EventEnvelope` instances via `IAsyncEnumerable`. The method signature must always use the `async` keyword.
- `GetSecurityDescriptorAsync` returns the security descriptor (or `null` if no auth required).
- Commands should not directly change aggregate state — they emit events.
- Use `await Task.CompletedTask` when no actual async work occurs.
- Throw exceptions inheriting from `ExceptionBase` for validation failures.
- The `ICommandExecutor` must be implemented as an `internal class`.

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

        await Task.CompletedTask;
    }

    public Task<ISecurityDescriptor?> GetSecurityDescriptorAsync(
        ICommandEnvelope<PlaceOrder> envelope, OrderAggregate aggregate) =>
        Task.FromResult<ISecurityDescriptor?>(null);
}
```

### Command Execution Recommendations

Command executors should validate whether the aggregate exists before proceeding. The aggregate should expose a boolean property (e.g. `Exists`) for this purpose — throw `NotFoundException` if the aggregate is expected to exist but doesn't, or `ConflictException` if it exists but shouldn't.

After validation, determine whether the command is a **No-Op** — a command that would not change the state of the aggregate (e.g. changing a property to its current value). In that case, the executor should complete without yielding any events.

### Concurrency Locking

Apply `[CommandLock]` to a command to acquire a distributed lock before execution. The lock is scoped to the subject (aggregate-level) or to the subject + command type (command-level).

### Execution of Commands

Inject `ICommandHandler<TCommand>` via DI and call `HandleAsync`, passing a `CommandEnvelope<TCommand>`:

```csharp
using CascadeEsdm.SharedKernel.ValueObjects;
using CascadeEsdm.WriteModel;
using CascadeEsdm.WriteModel.CommandHandling;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly ICommandHandler<PlaceOrder> _handler;

    public OrdersController(ICommandHandler<PlaceOrder> handler)
    {
        _handler = handler;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceOrder(
        [FromBody] PlaceOrder command)
    {
        await _handler.HandleAsync(new CommandEnvelope<PlaceOrder>(
            command,
            HttpContext.ToAuthenticatedContext(),
            new ClientChannel("api")));

        return Accepted();
    }
}
```

### Decorator Chain

The registered `ICommandHandler<TCommand>` is decorated automatically. The chain (outermost first) is:

1. `SerialisedCommandHandlerDecorator` — acquires the distributed lock (when `[CommandLock]` is present)
2. `EventWritingCommandHandlerDecorator` — persists emitted events to the event stream (Transaction Outbox)
3. `LoggingCommandHandlerDecorator` — structured logging via OpenTelemetry
4. `CommandHandler` — hydrates the aggregate, runs the executor, verifies event metadata

---

## Command Envelopes

### Definition

Command envelopes wrap commands with metadata required for processing. The framework provides `ICommandEnvelope` and `ICommandEnvelope<TCommand>` interfaces, with `CommandEnvelope` and `CommandEnvelope<TCommand>` implementations. `ICommandEnvelope` (non-generic) is used for serialisation purposes and is marked as deprecated — prefer `ICommandEnvelope<TCommand>` in all executor implementations.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Unique identifier for this command invocation |
| `Type` | `string` | The command type name (typeof(TCommand).Name) |
| `Command` | `ICommand` / `TCommand` | The wrapped command instance |
| `SecurityContext` | `AuthenticatedContext` | User identity and tenant information |
| `Channel` | `ClientChannel` | Originating channel (e.g., API, WebSocket) |
| `Time` | `DateTimeOffset` | When the command was created (UTC) |

### Instancing

Create a `CommandEnvelope<TCommand>` using the constructor:

```csharp
var envelope = new CommandEnvelope<PlaceOrder>(
    command: new PlaceOrder(orderId, reference),
    securityContext: new AuthenticatedContext(userIdentity, tenant),
    channel: new ClientChannel("api"));
```

The envelope automatically assigns:
- A new `Guid` for `Id`
- The current UTC time for `Time`
- The command type name for `Type`

For serialisation scenarios, a constructor accepting all properties is also available.

### Client Channel

The client channel is used during the asynchronous eventual consistency. When an event is used to project a view, on its update the source event ClientChannel is used to notify the client of the update. Clients generally use the update of a view to refresh their local state.

---

## Events

### Standards

- Events are immutable record objects representing historical facts.
- Use primitive types for all event properties (do not use value objects). Events are statements of historical fact — they do not need validation, logic, or transformation that value objects provide.
- All events inherit from the `IDomainEvent` marker interface and are `public`.
- Use primary constructors to enforce value provision at creation.
- Do not include validation or encapsulated logic — events represent truths, not intentions.
- Events do not need to define metadata such as Id, Time, or Subject — these are stored on the `EventEnvelope`.
- Place events in the `Events` folder under their respective aggregate.
- Events are emitted by `ICommandExecutor` implementations during command execution.

```csharp
public record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;
```

### Naming

- Name events in the past tense using a **NounVerb** pattern (e.g. `WorkItemCommentAdded`). They should not include the word "Event".
- Events should be the past tense version of the command, where possible.
- Avoid CRUD verbs (`Created`, `Updated`, `Deleted`), preferring instead `Added`, `Changed`, `Removed`.

### Event Envelopes

#### Definition

Event envelopes wrap domain events with metadata required for storage, routing, and projection. The `EventEnvelope` is a record in `CascadeEsdm.SharedKernel.Events` that contains the event plus all contextual information about when, why, and by whom the event was created.

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Unique identifier for this event instance |
| `Source` | `EventSource` | References the aggregate type, command ID, and command type that produced this event |
| `Subject` | `Subject` | The aggregate instance this event belongs to |
| `Type` | `string` | The event type name (event.GetType().Name) |
| `SecurityContext` | `AuthenticatedContext` | User identity and tenant that triggered the command |
| `Channel` | `ClientChannel` | Originating channel (used for view projection notifications) |
| `Event` | `IDomainEvent` | The wrapped domain event instance |
| `Sequence` | `int` | Position within the aggregate's event stream |
| `Time` | `DateTimeOffset` | When the event occurred (UTC) |

#### Event Source

The `EventSource` value object identifies the origin of an event:

```csharp
// Format: {AssemblyName}/{AggregateType}/{CommandType}/{CommandId}
// Example: MyDomain/OrderAggregate/PlaceOrder/550e8400-e29b-41d4-a716-446655440000
```

Use `EventSource.ForAggregate<TAggregate>(commandId, commandType)` to create an event source for an aggregate.

#### Creating Events

Events are created using the `CreateEvent` extension method on `ICommandEnvelope`:

```csharp
public async IAsyncEnumerable<IEventEnvelope> ExecuteAsync(
    ICommandEnvelope<PlaceOrder> envelope, OrderAggregate aggregate)
{
    yield return envelope.CreateEvent(
        new OrderPlaced(envelope.Command.OrderId.Value, envelope.Command.Reference),
        aggregate);

    await Task.CompletedTask;
}
```

The extension method automatically:
- Increments `aggregate.LastSequence` for the sequence number
- Creates the `EventSource` from the aggregate type, command ID, and command type
- Extracts the `Subject` from the command
- Copies `SecurityContext` and `Channel` from the command envelope
- Sets `Time` to current UTC
- Sets `Type` to the event type name
- Generates a new `Guid` for the event `Id`

### Event Appliers

- Implement `IEventApplier<TEvent, TAggregate>` in the **same file** as the event record. The applier mutates the aggregate directly using its public properties.
- The `IEventApplier` should be implemented as an `internal class`.
- Event appliers do not need to validate the event — it is a historical fact.
- When setting ValueObject properties of an entity during applier execution, use `new()` to reduce `using` statements.
- The `IEventApplier` does not need (and should not) change the `LastSequence` property of the aggregate.
- Event appliers are registered in the composition root via `UsingAppliers`.
- Event appliers should be **optimistic** in approach — since they are replaying historical events, they do not need to verify or validate using if statements.

```csharp
using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Hydration;

public record PersonFirstNameChanged(Guid PersonId, string FirstName) : IDomainEvent;

internal class PersonFirstNameChangedApplier : IEventApplier<PersonFirstNameChanged, PersonAggregate>
{
    public void Apply(PersonAggregate aggregate, PersonFirstNameChanged @event, EventEnvelope envelope)
    {
        aggregate.Person.FirstName = new(@event.FirstName);
    }
}
```

The following guard is unnecessary — the event is a historical fact and optimistic application is correct:

```csharp
// ❌ Unnecessary
if (aggregate.Person != null)
{
    aggregate.Person.FirstName = new(@event.FirstName);
}

// ✅ Correct
aggregate.Person.FirstName = new(@event.FirstName);
```

### Registration

| Method | Description |
|---|---|
| `AddEventApplier<TApplier>()` | Registers a single applier; `TEvent` and `TAggregate` are inferred via reflection from the applier's `IEventApplier<,>` interface |
| `AddEventAppliersFromAssembly<TExampleType>()` | Discovers and registers all `IEventApplier<,>` implementations in the assembly containing `TExampleType` |

```csharp
write.UsingAppliers(appliers => appliers
    .AddEventApplier<OrderPlacedApplier>()
    .AddEventApplier<OrderCancelledApplier>())

// Or register all appliers in an assembly at once:
write.UsingAppliers(appliers => appliers
    .AddEventAppliersFromAssembly<OrderAggregate>())
```

### Aggregate Hydration Using Events

- Events are ingested into the aggregate during hydration from the event stream source. This typically occurs during command execution in the `CommandHandler` base and is handled by the framework.
- The `IAggregateHydrator<TAggregate>` implementation forms the aggregate by pulling events from the event stream, resolving the `IEventApplier<TEvent, TAggregate>` for each event, and applying them.

### Inheritance Constraint

The Event Extractor is syntactic — it only extracts records where `IDomainEvent` appears **literally in the record's own base list**. Do not rely on inherited interface satisfaction:

```csharp
// ✅ Extracted — IDomainEvent is in the base list
public record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;

// ❌ NOT extracted — IDomainEvent is not directly in the base list
public record OrderPlaced(Guid OrderId, string Reference) : OrderEventBase(OrderId);
```

If you want derived records extracted, either keep `IDomainEvent` on each record, or flatten the hierarchy.

---

## Policies

### Purpose

Policies react to domain events after they have been persisted. A policy receives an `EventEnvelope`, decides whether it supports that event, and executes side-effects such as issuing further commands, sending notifications, or triggering integrations.

A single event can activate zero or many policies. All supporting policies execute concurrently — successful policies complete even if others fail. If any policy fails, a `PolicyExecutionException` is thrown containing details of every failure.

### Standards

- Policies implement `IPolicy` from `CascadeEsdm.WriteModel.Policies`.
- Policies are resolved from DI — constructor injection works as expected.
- `Supports(EventEnvelope)` determines whether the policy handles a given event. Pattern-match on `envelope.Event`.
- `ExecuteAsync(EventEnvelope, CancellationToken)` performs the side-effect.
- Policies must not mutate aggregate state — they trigger further commands or external actions.
- Policies are registered via `WithPolicies()` on the `WriteModelBuilder`.
- Place policies in a `Policies` folder within the aggregate directory.

### Implementation

```csharp
using CascadeEsdm.SharedKernel.Events;
using CascadeEsdm.WriteModel.Policies;

internal class SendWelcomeEmailPolicy : IPolicy
{
    private readonly IEmailService _emailService;

    public SendWelcomeEmailPolicy(IEmailService emailService)
    {
        _emailService = emailService;
    }

    public bool Supports(EventEnvelope envelope) =>
        envelope.Event is PersonAdded;

    public async Task ExecuteAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var @event = (PersonAdded)envelope.Event;
        await _emailService.SendWelcomeAsync(@event.Email, cancellationToken);
    }
}
```

### Registration

```csharp
write.WithPolicies(policies => policies
    .AddPolicy<SendWelcomeEmailPolicy>()
    .AddPoliciesFromAssembly<PersonAggregate>()
    .AddPoliciesFromNamespace<SendWelcomeEmailPolicy>())
```

| Method | Description |
|---|---|
| `AddPolicy<TPolicy>()` | Registers a single policy |
| `AddPoliciesFromAssembly<TExampleType>()` | Discovers all `IPolicy` implementations in the assembly |
| `AddPoliciesFromNamespace<TExampleType>()` | Discovers all `IPolicy` implementations in the namespace (and child namespaces) |

### Dispatching

Inject `IPolicyDispatcher` and call `DispatchAsync`:

```csharp
await _policyDispatcher.DispatchAsync(envelope);
```

### Error Handling

If one or more policies throw, the dispatcher waits for all remaining policies to complete and then throws a `PolicyExecutionException`. Each failure is available via `PolicyExecutionException.Failures` — an `IReadOnlyList<PolicyFailure>` containing the policy class name and the thrown exception.

### Folder Structure

```
Domain/
  People/
    Policies/
      SendWelcomeEmailPolicy.cs
      NotifyAdminPolicy.cs
```

---

## Entities

Entities represent state of the aggregate at the hydration point, a means to organise hydrated events into structured and focused models that make sense for the domain.

### Standards

- An entity is mutable.
- An entity properties are _not_ primitives.
- An entity properties should be immutable value objects.
- An entity should not be referenced or accessed outside of its aggregate root (the aggregate folder).
- An entities state is built by replaying all events from a snapshot or from the stream start.
- The entities are available as properties on the aggregate.
- The entity should not contain logic or rules (value objects do that).
- Entities should exist in the /Entities folder of the aggregate.
- Command Executors do not change entities. They use entities to determine state and emit events.
- Event Appliers do change entities. This is what loads the state.

---

## Value Objects

### Standards

- All ValueObjects must be immutable and implement `IValueObject<TValueType>`.
- Use primary constructors for setting the value, or expose the value as a readonly property.
- Implement implicit conversion operators to and from the underlying primitive type for ergonomic usage.
- ValueObjects should be used as properties of entities/aggregates and commands for strong typing and domain clarity.
- When creating a new instance, use `new(value)` rather than `new ValueObjectName(value)`.

### ID-Style ValueObjects

For ValueObjects that represent identifiers:
- Provide static `Empty` and `IsEmpty` semantics.

```csharp
public record OrderId(Guid Value) : IValueObject<Guid>
{
    public static OrderId Empty => new(Guid.Empty);
    public static bool IsEmpty(OrderId id) => id.Value == Guid.Empty;

    public static implicit operator OrderId(Guid value) => new(value);
    public static implicit operator Guid(OrderId id) => id.Value;
}
```

### Non-ID ValueObjects

For ValueObjects that represent domain values (names, descriptions, etc.):
- No `Empty`/`IsEmpty` semantics required.
- If validation is required, use a constant `Pattern` property and check using regex (for strings). Throw a `System.ComponentModel.DataAnnotations.ValidationException` on failure, or a suitable exception inheriting from `ExceptionBase`.

```csharp
public record EmailAddress(string Value) : IValueObject<string>
{
    private const string Pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    public EmailAddress(string value) : this(value)
    {
        if (!Regex.IsMatch(value, Pattern))
            throw new ValidationException($"Invalid email address: {value}");
    }

    public static implicit operator EmailAddress(string value) => new(value);
    public static implicit operator string(EmailAddress vo) => vo.Value;
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

### Standards

- All exceptions should inherit from `ExceptionBase`, allowing consistent error handling across the application.
- A valid and suitable `HttpStatusCode` should be defined for each exception, or inherited from a parent exception.
- Exceptions generally occur during command execution. The nature of events means exceptions are less likely to occur during hydration or replay.
- Common exceptions (`ConflictException`, `NotFoundException`, `UnauthorisedException`, etc.) are defined in `CascadeEsdm.WriteModel.Abstractions/Exceptions`. New exceptions can be created where needed, and placed into the aggregate's `/Exceptions` directory or, where shared, into a suitable location.

### Endpoint Handling

Commands should ideally be executed in the API layer to ensure immediate exception feedback.

#### API Handling

- API endpoints should use suitable middleware to handle thrown exceptions and translate them to HTTP responses using the exception's `HttpStatusCode`.

#### Queue Consumer Processing

- Queue consumers should move messages that throw exceptions to a dead letter queue with the error message as the `DeadLetterReason`.

---

## Event Extractor

### Why It Exists

In an event-sourced system, domain events are the shared language between bounded contexts. They are the facts other systems subscribe to — not commands, not aggregates, not internal state.

A common problem is **write-model leakage**: the events assembly consumers reference starts pulling in write-model concerns — command handlers, appliers, hydration logic, infrastructure dependencies. The consumer now has a transitive dependency on your internal domain machinery.

The alternative — duplicating event definitions into a separate project by hand — works initially but drifts. Two copies of the same event diverge silently. The write model moves on; the published contract doesn't.

The `CascadeEsdm.EventExtractor` solves this by treating your write-model source as the **single source of truth** and generating the events assembly automatically at build time. You write your events once, in context, alongside their appliers. The extractor lifts only the publishable parts — the event records — into a clean, dependency-light assembly.

### What It Does

At pre-build time the tool:

1. **Scans** all `.cs` files under your project root for `record` types implementing `IDomainEvent`
2. **Strips** write-model-only concerns from each file — `IEventApplier` classes, and `using` directives for write-model namespaces
3. **Rewrites** namespaces from your write-model root (e.g. `Acme.Orders.WriteModel`) to a schema root (e.g. `Acme.Orders.Schema`)
4. **Resolves** any external enum dependencies referenced by event records but defined in non-event files, and copies them into an `Enums/` subfolder
5. **Generates** a standalone `.csproj` referencing only `CascadeEsdm.SharedKernel.Abstractions` (on first run; never overwritten thereafter)
6. **Reports** what was found and written to stdout

The result is a compilable events-only project your consumers can reference without pulling in any write-model code.

### Setup

#### 1. Install the tool

```bash
dotnet tool install -g CascadeEsdm.EventExtractor
```

#### 2. Add `CascadeEsdm.WriteModel.Abstractions` to your write-model project

The MSBuild targets are bundled in the `CascadeEsdm.WriteModel.Abstractions` NuGet package and activate automatically. No further configuration is required for a default setup.

#### 3. Build

On the next build, the extractor runs before compilation and writes the events project alongside your write-model project:

```
MyApp.WriteModel/
MyApp.Schema/           ← generated
  MyApp.Schema.csproj
  Orders/
    Events/
      OrderPlaced.cs
  Enums/
    OrderStatus.cs
```

Add `MyApp.Schema/` to source control. Add it to your solution. Maybe build and publish to your own private nuget feed. Reference it from consumer projects.

### Key configurable MSBuild properties

| Property | Default | Description |
|---|---|---|
| `CascadeEventsEnabled` | `true` | Set to `false` to disable extraction entirely |
| `CascadeEventsOutputDir` | `$(MSBuildProjectDirectory)\..\AssemblyName.Schema` | Where the generated project is written |
| `CascadeEventsAssemblyName` | RootNamespace with write-model suffix stripped, + `.Schema` | Assembly name of the generated project |
| `CascadeEventsOverwrite` | `false` | When `true`, regenerates all files on every build; the `.csproj` is still never overwritten |
| `CascadeEventsRequireExtractor` | `false` | When `true`, a missing tool is a build error instead of a warning |

#### Assembly name defaulting

If `CascadeEventsAssemblyName` is not set, the tool strips a recognised write-model suffix from `RootNamespace` and appends `.Schema`:

| `RootNamespace` | Resolved assembly name |
|---|---|
| `Acme.Orders.WriteModel` | `Acme.Orders.Schema` |
| `Acme.Orders.Domain` | `Acme.Orders.Schema` |
| `Acme.Orders.Write` | `Acme.Orders.Schema` |
| `Acme.Orders.Application` | `Acme.Orders.Schema` |
| `Acme.Orders` | `Acme.Orders.Schema` |

> **Important:** The root namespace of generated files is always set equal to the resolved assembly name. The two cannot differ. This invariant is required for `$type`-based deserialisation — see [Service Bus serialisation](#service-bus-serialisation) below.
>
> If you override `CascadeEventsAssemblyName`, ensure your consumer's `SchemaTypeNameMapper` will see the same name. When in doubt, rely on the default.

### Service Bus Serialisation

When publishing an `EventEnvelope` to a service bus topic, the `IDomainEvent` stored in `Event` must carry a `$type` discriminator that consumers can resolve without access to the write-model assembly.

`DefaultSerialisationSettings.ForServiceBusPublishing()` provides serialiser options that rewrite the `$type` from the write-model identity to the schema assembly identity automatically — no configuration required:

```csharp
var options = DefaultSerialisationSettings.ForServiceBusPublishing();
var json = JsonSerializer.Serialize(envelope, options);
```

Given a write-model event `Acme.Orders.WriteModel.Orders.Events.OrderPlaced` in assembly `Acme.Orders.WriteModel`, the emitted `$type` will be:

```
Acme.Orders.Schema.Orders.Events.OrderPlaced, Acme.Orders.Schema
```

This is exactly what the schema assembly contains. A consumer that references `Acme.Orders.Schema` and uses the same `ForServiceBusPublishing()` options (or `UsingTypeQualifiedName()` with the schema assembly loaded) can deserialise the envelope without any additional wiring.

#### How the mapping works

`SchemaTypeNameMapper` applies the same deterministic suffix-strip rule as the extractor to both the namespace prefix and the assembly component of the `$type` string:

1. Strip the recognised write-model suffix from the assembly name (`.WriteModel`, `.Domain`, `.Write`, `.Application`) and append `.Schema`
2. Replace the matching namespace prefix in the fully-qualified type name with the new assembly name

Because the rule is derived entirely from the type itself, the publisher needs no knowledge of the schema project — there is no configuration to keep in sync.

#### Constraint

This mapping relies on the schema assembly name and root namespace being identical. The extractor enforces this: the root namespace of generated files always equals the resolved assembly name and cannot be overridden independently. If you override `CascadeEventsAssemblyName`, the same name must be used as the root namespace of the generated project (which the extractor sets automatically).

### What Gets Extracted

#### Event records

Any `record` whose base list contains `IDomainEvent` is included:

```csharp
// write-model source — Acme.Orders.WriteModel.Orders.Events
public record OrderPlaced(Guid OrderId, string Reference, OrderStatus Status) : IDomainEvent;
```

Becomes in the schema assembly:

```csharp
// generated — Acme.Orders.Schema.Orders.Events
public record OrderPlaced(Guid OrderId, string Reference, OrderStatus Status) : IDomainEvent;
```

#### Inheritance

The scanner is syntactic — it does not resolve types. A record is included **only if `IDomainEvent` appears literally in its own base list**. If you use a base record hierarchy, every level that should be extracted must declare `IDomainEvent` directly:

```csharp
// ✅ extracted — IDomainEvent is in the base list
public abstract record OrderEventBase(Guid OrderId) : IDomainEvent;

// ✅ extracted — IDomainEvent is in the base list
public record OrderPlaced(Guid OrderId, string Reference) : IDomainEvent;

// ❌ not extracted — IDomainEvent is not in the base list, only OrderEventBase is
public record OrderPlaced(Guid OrderId, string Reference) : OrderEventBase(OrderId);
```

If you want derived records extracted, either keep `IDomainEvent` on each record, or flatten the hierarchy — base record properties can be composed into each event directly.

#### Co-located enums

Enums defined in the same file as event records are included verbatim.

#### External enum dependencies

Enums referenced by event records but defined elsewhere in the project are detected and copied into an `Enums/` subfolder under the events project root, placed in a `<TargetRootNamespace>.Enums` namespace.

#### Non-primitive parameter types

The extractor only copies enums automatically. Classes, records, structs, and interfaces referenced in event parameters are **not copied** — doing so would risk pulling in arbitrarily deep type graphs, including types from high-level assemblies that have no place in a minimal events contract.

The recommended approach is to **use primitives wherever possible** in event records:

```csharp
// ✅ prefer — portable, no external dependencies
public record OrderPlaced(Guid OrderId, string Reference, int StatusCode) : IDomainEvent;

// ⚠️ works but requires manual wiring — consumers must also reference the type
public record SecurityDescriptorSet(MySecurityDescriptor Descriptor) : IDomainEvent;
```

If a non-primitive type is genuinely part of the public event contract, add a reference to the assembly that defines it directly in the generated `.csproj`. Because the `.csproj` is never overwritten, this addition is stable across rebuilds:

```xml
<ItemGroup>
  <PackageReference Include="Acme.Shared.Contracts" Version="1.0.0" />
</ItemGroup>
```

#### What is stripped

- `IEventApplier<TEvent, TAggregate>` classes
- `using` directives for write-model-only namespaces:
  - `CascadeEsdm.WriteModel.Hydration`
  - `CascadeEsdm.WriteModel.CommandHandling`
  - `CascadeEsdm.WriteModel.Security`
  - `CascadeEsdm.WriteModel.Composition`
  - `CascadeEsdm.WriteModel.EventStream`

### Cohesion vs Abstraction

The core tension this tool resolves is between two legitimate pressures:

**Cohesion** says: keep the event record and its applier together. The `PersonAdded` record and `PersonAddedApplier` belong side by side — they describe the same fact and its effect. Splitting them into separate projects fragments understanding and makes navigation harder.

**Abstraction** says: consumers should depend on the minimal contract. A read-model projection handler that subscribes to `OrderPlaced` should not compile against your command handlers, your aggregate hydration logic, or your CosmosDB infrastructure.

Without tooling you are forced to choose: either accept write-model leakage into the published contract, or split your event definitions out manually and maintain two copies.

The extractor removes the choice. **Write everything in one place, publish only what belongs in the contract.** The events assembly is generated, not authored — there is no second copy to drift.

### The Generated Project

On first run a `.csproj` is created with a single dependency:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <AssemblyName>Acme.Orders.Schema</AssemblyName>
    <RootNamespace>Acme.Orders.Schema</RootNamespace>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CascadeEsdm.SharedKernel.Abstractions" Version="*" />
  </ItemGroup>

</Project>
```

The `.csproj` is **never overwritten** on subsequent builds regardless of `CascadeEventsOverwrite`. This lets you pin the version, add additional references, or adjust the target framework without them being stomped. Source files are only rewritten when their content has changed.

### Missing Tool Behaviour

If `cascade-extract-events` is not installed:

- By default a **build warning** is emitted and extraction is skipped — your project still builds
- Set `CascadeEventsRequireExtractor=true` to promote this to a **build error**

```xml
<PropertyGroup>
  <CascadeEventsRequireExtractor>true</CascadeEventsRequireExtractor>
</PropertyGroup>
```

---

## Read Model — Creating and Configuring Views

### Overview

The read layer materialises domain events into query-optimised **views**. Each view is a denormalised row that represents a projection of one or more events. When an event is received, the framework:

1. **Locates** the target row (or creates a new one)
2. **Determines** what structural change to apply (add, update, or remove)
3. **Maps** event properties onto the view using AutoMapper

All of this is expressed declaratively through a fluent configuration API — no manual mapping code, no switch statements over event types.

### Concepts

| Term | Description |
|---|---|
| **View** | A read-model row — the materialised, query-optimised projection of events. Implements `IView` |
| **Partition** | The storage partition key for a view. Declared via `[PartitionFormat]` attribute |
| **ViewProfileConfiguration** | The entry point for mapping events to a view. Package users inherit this and override `Configure` |
| **Row Locator** | How an event finds the target row — a key-value pair identifying which view property to match against |
| **Mutation Strategy** | What the event does to the row — `AddsNewRow`, `ChangesRows`, or `RemovesRows` |
| **Partition Strategy** | How the partition key is resolved — **static** (from the envelope) or **explicit** (from event properties) |

### Step 1 — Define the View

A view implements `IView` (or `IAuthoredView` if the row should track who created it):

```csharp
using CascadeEsdm.ReadModel.Views;

[PartitionFormat("orders")]
public class OrderView : IView
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public IList<string> ClientPermissions { get; set; } = new List<string>();

    // Domain-specific properties
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public decimal Total { get; set; }
}
```

#### IView members

| Property | Purpose |
|---|---|
| `Id` | Row identifier — set by the `AddsNewRow` locator |
| `ParentId` | Optional parent aggregate reference — set from the `Subject` when an event creates a row |
| `Created` | Timestamp set automatically when the row is first created |
| `Modified` | Timestamp updated automatically on every event projection |
| `ClientPermissions` | Permission strings for client-side authorisation |

#### IAuthoredView

If the view should record the identity of the user who created the row, implement `IAuthoredView` instead:

```csharp
public class OrderView : IAuthoredView
{
    // ... all IView members plus:
    public UserIdentity Author { get; set; } = null!;
}
```

#### PartitionFormat

The `[PartitionFormat]` attribute declares how the storage partition key is composed. Supported tokens:

| Token | Source |
|---|---|
| `{partitionId}` | An explicit identifier derived from the event or aggregate |
| `{tenantId}` | The tenant from the authenticated context |
| `{userId}` | The user from the authenticated context |

Examples:

```csharp
[PartitionFormat("orders")]                              // static — all orders in one partition
[PartitionFormat("workitems-{partitionId}")]              // explicit — partition per parent
[PartitionFormat("profiles-{tenantId}")]                  // tenant-scoped
[PartitionFormat("attendees-{tenantId}-{partitionId}")]   // tenant + explicit
```

### Step 2 — Create the Configuration

Inherit `ViewProfileConfiguration<TView>` and override `Configure`. This is the only method package users implement — the framework calls `Build` internally:

```csharp
using CascadeEsdm.ReadModel.Projecting.Configuration;

internal class OrderViewConfiguration : ViewProfileConfiguration<OrderView>
{
    protected override void Configure(ViewEventBuilder<OrderView> builder)
    {
        // Configuration goes here
    }
}
```

### Step 3 — Choose a Partition Strategy

The first call inside `Configure` selects how the partition key is resolved:

#### Static partition

Use when all events for this view share the same partition, derived from the `EventEnvelope` (typically the tenant or a fixed string):

```csharp
var config = builder.UsesStaticPartitionKey();
```

#### Explicit partition

Use when the partition key comes from the event itself (e.g. a parent aggregate ID):

```csharp
var config = builder.UsesExplicitPartitionKey();
```

### Step 4 — Register Events

Each event type is registered using `.For<TEvent>()`, which begins the fluent chain:

#### Static partition flow

```
config.For<TEvent>()
    → .UsingRowLocator(...)       // how to find the row
    → .AddsNewRow(...)            // OR .ChangesRows() OR .RemovesRows()
    → [optional property mapping]
```

#### Explicit partition flow

```
config.For<TEvent>()
    → .UsingPartitionIdentifier(...)   // where to find the partition key
    → .AndRowLocator(...)              // how to find the row within that partition
    → .AddsNewRow(...)                 // OR .ChangesRows() OR .RemovesRows()
    → [optional property mapping]
```

### Step 5 — Configure Row Location

The row locator tells the framework which view property to match against the event to find existing rows:

```csharp
.UsingRowLocator((evt, envelope) => new KeyValuePair<string, Guid>(
    nameof(OrderView.Id),    // the view property to search
    evt.OrderId))            // the value to match
```

For explicit partitions, the partition identifier comes first:

```csharp
.UsingPartitionIdentifier((evt, envelope) => envelope!.Subject.Id)
.AndRowLocator((evt, envelope) => new KeyValuePair<string, Guid>(
    nameof(OrderView.Id),
    evt.OrderId))
```

### Step 6 — Choose a Mutation Strategy

#### AddsNewRow

The event creates a new view row. Provide a function that returns the new row's `Id`:

```csharp
.AddsNewRow((evt, envelope) => evt.OrderId)
```

`Created` and `Modified` are set automatically from the envelope timestamp.

#### ChangesRows

The event updates an existing row:

```csharp
.ChangesRows()
```

`Modified` is updated automatically from the envelope timestamp.

#### RemovesRows

The event deletes the matched row:

```csharp
.RemovesRows()
```

### Step 7 — Map Event Properties to View

After `AddsNewRow` or `ChangesRows`, chain AutoMapper member mappings to express how event properties translate to view properties.

#### Direct property mapping

Use `.ForProperty` for type-safe, expression-based mapping between event and view properties of the same type:

```csharp
.AddsNewRow((e, o) => e.OrderId)
.ForProperty(v => v.Reference, e => e.Reference)
.ForProperty(v => v.Status, (e, envelope) => "Placed")
```

#### AutoMapper ForMember

Use `.ForMember` for more complex mappings — computing values, accessing existing view state:

```csharp
.ChangesRows()
.ForMember(v => v.Total, x => x.MapFrom(e => e.Amount))
.ForMember(v => v.Status, x => x.MapFrom((e, existing) => existing.Status == "Draft" ? "Submitted" : existing.Status))
```

#### ConvertUsing

For mutations that can't be expressed with member mappings (e.g. modifying collections, nested objects):

```csharp
.ChangesRows()
.ConvertUsing((evt, view) =>
{
    view.Items.Add(new LineItem(evt.ProductId, evt.Quantity));
    return view;
})
```

A three-argument overload provides access to the AutoMapper `ResolutionContext` (and thus the `EventEnvelope` via `context.State`):

```csharp
.ConvertUsing((evt, view, context) =>
{
    var envelope = context.State as EventEnvelope;
    view.LastModifiedBy = envelope?.Source.CommandName;
    return view;
})
```

### Complete Example — Static Partition

```csharp
[PartitionFormat("orders")]
public class OrderView : IView
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public IList<string> ClientPermissions { get; set; } = new List<string>();
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public decimal Total { get; set; }
}

internal class OrderViewConfiguration : ViewProfileConfiguration<OrderView>
{
    protected override void Configure(ViewEventBuilder<OrderView> builder)
    {
        var config = builder.UsesStaticPartitionKey();

        config.For<OrderPlaced>()
            .UsingRowLocator((e, o) => new(nameof(OrderView.Id), e.OrderId))
            .AddsNewRow((e, o) => e.OrderId)
            .ForProperty(v => v.Reference, e => e.Reference);

        config.For<OrderTotalUpdated>()
            .UsingRowLocator((e, o) => new(nameof(OrderView.Id), e.OrderId))
            .ChangesRows()
            .ForMember(v => v.Total, x => x.MapFrom(e => e.NewTotal));

        config.For<OrderCancelled>()
            .UsingRowLocator((e, o) => new(nameof(OrderView.Id), e.OrderId))
            .RemovesRows();
    }
}
```

### Complete Example — Explicit Partition

Use explicit partitions when the view's storage partition depends on event properties rather than the envelope alone — typically for child entities scoped to a parent aggregate:

```csharp
[PartitionFormat("lineitems-{partitionId}")]
public class LineItemView : IView
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Modified { get; set; }
    public IList<string> ClientPermissions { get; set; } = new List<string>();
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

internal class LineItemViewConfiguration : ViewProfileConfiguration<LineItemView>
{
    protected override void Configure(ViewEventBuilder<LineItemView> builder)
    {
        var config = builder.UsesExplicitPartitionKey();

        config.For<LineItemAdded>()
            .UsingPartitionIdentifier((e, o) => o!.Subject.Id)
            .AndRowLocator((e, o) => new(nameof(LineItemView.Id), e.LineItemId))
            .AddsNewRow((e, o) => e.LineItemId)
            .ForProperty(v => v.ParentId, (e, o) => o!.Subject.Id);

        config.For<LineItemQuantityChanged>()
            .UsingPartitionIdentifier((e, o) => o!.Subject.Id)
            .AndRowLocator((e, o) => new(nameof(LineItemView.Id), e.LineItemId))
            .ChangesRows();

        config.For<LineItemRemoved>()
            .UsingPartitionIdentifier((e, o) => o!.Subject.Id)
            .AndRowLocator((e, o) => new(nameof(LineItemView.Id), e.LineItemId))
            .RemovesRows();
    }
}
```

### Key rules for AI agents

- View configurations are `internal class` — they are registered automatically, not instantiated by consumers
- Every event must have both a **row locator** and a **mutation strategy** — the framework validates this
- `Created` and `Modified` are set automatically — do not map them manually
- Use `.ForProperty` for type-safe same-type mappings; use `.ForMember` for computed or cross-type mappings
- Use `.ConvertUsing` only when member-level mapping is insufficient (e.g. collection mutations)
- The `EventEnvelope` is available as the second parameter in locator lambdas (`o` or `envelope`) — access `Subject`, `Time`, `SecurityContext` etc.
- For explicit partitions, the partition identifier must be resolved **before** the row locator (`.UsingPartitionIdentifier` → `.AndRowLocator`)

---

## Bounded Context

Bounded context are collections of related aggregates. Ideally, bounded context should allow teams who own them to minimise coordination with other teams during feature development; bounded context tend to be defined by their ubiquitous language. If we call it this, you call it that - we're likely in different bounded context.

### Size

Bounded Context should be as big as you can make it. At the start of the project, your bounded context should be everything. As it grows (complexity increases over time), it should be split away into smaller bounded context based on what you learn during development; new language emerging for example. However, the best reason to break a bounded context into two or more is _cognitive load_ - when the team can no longer hold the entire context in their head.

### Abstraction

Bounded context should be _logically abstracted_ from other bounded contexts. This means that the bounded context should not depend on the internal structure of other bounded contexts. Instead, it should depend on the public interface of other bounded contexts. It should not be a _new microservice_, which just create all sorts of friction and complexity. Instead, abstract into a separate assembly in the same solution, or a new solution but in the same repo.

Just don't take the microservice route unless you really need to.

---

## Code Style

- XML doc comments (`///`) only on `public` members of `public` types
- No inline comments (`//`) anywhere — code should speak for itself
- No comments on non-public members — they drift from implementation and become lies; prefer clear naming

---

## Design Principles

**Opinionated by intent.** Cascade has opinions so your engineers don't need to. The right decisions are already made — concurrency strategy, hydration, command dispatch, event storage.

**Technology Abstraction.** Azure today, something else tomorrow. Storage and lock providers are pluggable. The domain code doesn't change.

**Engineers focus on function.** Write commands, emit events, build projections. The framework handles the rest.

**Cohesion over unnecessary abstraction.** Events and their appliers live together. The extractor publishes only what belongs in the contract. No artificial splits to satisfy infrastructure concerns.

---

## Status

Beta — Q2 2026. The core write model and infrastructure packages are stable. The event extractor is in active development.

Packages are available on [NuGet](https://www.nuget.org/packages?q=cascadeesdm).
