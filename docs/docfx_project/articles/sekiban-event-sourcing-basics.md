# Sekiban Event Sourcing Basics

## Basics of Event Sourcing.

Basic concept of Event Sourcing is, source of truth is always in the events and state can be calculated from stream of events. Events is append only and should contain what happened as events, preferbly contains why that happened as well.

## Source of the events - stream.

And for event sourcing, first concept you need to understand is stream of events. Events are grouped with `Partition`, and one `Partition` has multiple events and make state. We call this grouping as `Aggregate`. In Sekiban, `Aggregate` has types and has set of events. `Aggregate` manages its state after each events. It is explaining in image below.

![Event Sourcing concept](../images/sekiban-event-sourcing-basics/image1.png)

This image shows how aggregate is changed with stream of events.

1. Event1 create user John Doe, john@example.com and 0 point.
2. Event2 Changes names to John Smith. Aggregate name is changed but email and point are still same from last state.
3. Event3 adds point 1000. Aggregate name and email stays same and point are added 1000 and current point become 1000.
4. Event4 uses point 200. Aggregate name and email stays same and point reduces 1000 minus 200, current point becomes 800.

Like this image and explains, you can define aggregate and events, and event will change state of aggregate. In Sekiban, we can define Aggregate and Event as below.

This is from SekibanEventSourcingBasics Tutorial, located in the repository, `/Tutorials/1.SekibanEventSourcingBasics` folder.

```csharp
   public record UserPoint(string Name, string Email, int Point) : IAggregatePayload<UserPoint>
   {
       public static UserPoint CreateInitialPayload(UserPoint? _) => new UserPoint(string.Empty,string.Empty,0);
   }

   public record UserPointCreated(string Name, string Email, int Point) : IEventPayload<UserPoint, UserPointCreated>
   {
       public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointCreated> ev) =>
           new(ev.Payload.Name, ev.Payload.Email, ev.Payload.Point);
   }

   public record UserPointNameChanged(string ChangedName) : IEventPayload<UserPoint, UserPointNameChanged>
   {
       public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointNameChanged> ev) =>
           aggregatePayload with { Name = ev.Payload.ChangedName };
   }

   public record UserPointReceived(int Point, string Note) : IEventPayload<UserPoint, UserPointReceived>
   {
       public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointReceived> ev) =>
           aggregatePayload with { Point = aggregatePayload.Point + ev.Payload.Point };
   }

   public record UserPointUsed(int Point, string Note) : IEventPayload<UserPoint, UserPointUsed>
   {
       public static UserPoint OnEvent(UserPoint aggregatePayload, Event<UserPointUsed> ev) =>
           aggregatePayload with { Point = aggregatePayload.Point - ev.Payload.Point };
   }

```

Code above is simple yet shows how you can define event sourcing stream.
This case aggregate (stream) is named `UserPoint`. In this `UserPoint` stream manages ONE User with Point. If two Users exists, it should use two different stream.

You can see from the code above, this code represents AggregatePayload and EventPayload. There are common information for the Aggregate and Events, which is AggregateId, TimeStamp, SortKey, PartitionKey etc is in the Aggregates and Events, and developer will write code for the contents, in the Payload.

### "Functional" way to write event sourcing.
The code above is live code that can run in the Sekiban, it might be slightly different with what you see in other event sourcing library or sample code. Sekiban took event sourcing as "Functional" way and try to use c# feature of the functional side. IAggregatePayload Interface method "CreateInitialPayload" and IEventPayload "OnEvent" method are both static and pure function, which is not changing instance, rather create new instance and returns as a new object. This makes it more compatible for parallel executions and easier to see code base. 


## Only way to Produce Event - Command.
