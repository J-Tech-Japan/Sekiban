using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.TestSupport.ExecutedUser;

/// <summary>Simple payload used by executed-user provider scenario tests.</summary>
public sealed record TestCreated : IEventPayload
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>Second payload used by multi-event executed-user provider scenario tests.</summary>
public sealed record TestAdded : IEventPayload
{
    public Guid Id { get; init; }
}

/// <summary>Command that produces a single event in executed-user provider scenario tests.</summary>
public sealed record CreateSingleEventTestCommand(Guid Id, string Name) : ICommand;

/// <summary>Command that produces two events in executed-user provider scenario tests.</summary>
public sealed record CreateMultiEventTestCommand(Guid Id) : ICommand;

/// <summary>Command that produces no events in executed-user provider scenario tests.</summary>
public sealed record NoEventTestCommand : ICommand;
