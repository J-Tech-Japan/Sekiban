﻿using CustomerDomainContext.AggregateEventSubscribers;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using System.Reflection;

namespace CustomerDomainContext.Shared;

public static class Dependency
{
    public static RegisteredEventTypes GetRegisteredAggregateEvents() => new(Assembly.GetExecutingAssembly());

    public static IEnumerable<(Type serviceType, Type? implementationType)> GetDependencies()
    {
        // Aggregate Event Subscribers
        yield return (
            typeof(INotificationHandler<ClientCreated>),
            typeof(ClientCreatedSubscriber));

        yield return (
            typeof(INotificationHandler<ClientDeleted>),
            typeof(ClientDeletedSubscriber));

        // Aggregate: Branch
        yield return (
            typeof(ICreateAggregateCommandHandler<Branch, CreateBranch>),
            typeof(CreateBranchHandler));

        // Aggregate: Client
        yield return (
            typeof(ICreateAggregateCommandHandler<Client, CreateClient>),
            typeof(CreateClientHandler));

        yield return (
            typeof(IChangeAggregateCommandHandler<Client, ChangeClientName>),
            typeof(ChangeClientNameHandler));

        yield return (
            typeof(IChangeAggregateCommandHandler<Client, DeleteClient>),
            typeof(DeleteClientHandler));

        // Aggregate: LoyaltyPoint
        yield return (
            typeof(ICreateAggregateCommandHandler<LoyaltyPoint, CreateLoyaltyPoint>),
            typeof(CreateLoyaltyPointHandler));

        yield return (
            typeof(IChangeAggregateCommandHandler<LoyaltyPoint, AddLoyaltyPoint>),
            typeof(AddLoyaltyPointHandler));

        yield return (
            typeof(IChangeAggregateCommandHandler<LoyaltyPoint, UseLoyaltyPoint>),
            typeof(UseLoyaltyPointHandler));

        yield return (
            typeof(IChangeAggregateCommandHandler<LoyaltyPoint, DeleteLoyaltyPoint>),
            typeof(DeleteLoyaltyPointHandler));
    }
}
