# Introducing Sekiban - Event Sourcing and CQRS Framework.

## What is Sekiban?

**Sekiban** is application development framework using **Event Sourcing and CQRS**. Sekiban uses C# and dotnet 7+, and developers can use declarative api to build event sourcing applications. As a event store, sekiban can use [Microsoft Azure Cosmos DB](https://azure.microsoft.com/en-us/products/cosmos-db) and [Amazon Dynamo DB](https://aws.amazon.com/dynamodb/). Application can be deploy either azure or AWS.

## What is Event Sourcing and CQRS.
If you reach this page, you would provably known Event Sourcing and CQRS well. If you would like detail about it, going to the source, Greg Young's youtube talk would be good to get an idea of it. [Transcript of Greg Young's Talk at Code on the Beach 2014: CQRS and Event Sourcing](https://www.eventstore.com/blog/transcript-of-greg-youngs-talk-at-code-on-the-beach-2014-cqrs-and-event-sourcing)
Greg Strongly recomends not to use frameworks, and build your own event sourcing system. I agree, building framework let us understand more. We think many would rather focus on business logic and start developing with tools and document database. We think now many databases and cloud platform allow to do event sourcing with framework and this speed up many developers. 

**Quick disclaimer:** Even though you use framework, developers needs to understand concepts of event sourcing, especially after you launch first version, you need to optimize Query performance when more data and users are using system. It requires basic knowledge of the Event Sourcing and CQRS.

## Sekiban has following main features below.

- Simple Commands and Events
- Publish only commands
- Optimistic aggregate version check
- Event versions
- Single Aggregate Projections
- Multi Aggregate Live Projections
- Single Aggregate / Multi Aggregate Projections Snapshots
- Large snapshots can be stored in Azure Blob Storage / Amazon S3.
- Testing framework
- Cosmos DB / Dynamo DB datastore.
- Tenant Partitions
- Query from multi projections
- Command and Query Web API Generator with swagger support.

Those and more features helps developers to simply forcus on the business side and implement commands, events, projections, tests and queries.

## Compatibility

Sekiban has compatibility with Dotnet 7 and 8. 

For Cosmos DB, we only support with new [Hieralchiral Partitions Key](https://learn.microsoft.com/en-us/azure/cosmos-db/hierarchical-partition-keys?tabs=net-v3%2Cbicep). This allow to create Tenant(Root Partition Key), Aggregate Type and Partition Key can be efficiently query using Cosmos DB.

For Dynamo DB, you need to set Partiton Key and Sort Key for using Sekiban.

## How big can Sekiban Projects can be?
Current version of Sekiban (0.14) does not have build in materialized view helper features. Sekiban's Live Projection can be handle small and midium size systems. Usually sekiban can be used with Live Projection if system memory is enough for the size of the Multiple Projections. For Live Projection Please see the great article from Anton Stöckl [Live Projections for Read Models with Event Sourcing and CQRS](https://medium.com/@TonyBologni/live-projections-for-read-models-with-event-sourcing-and-cqrs-42dd18b19fd0). Developers can implement using Cosmos DB and Dynamo DB change feed. Although it is very simple to create materialized view using Change Feed, it is not tightly related with Sekiban Framework, so we did not include in the framework.

## Open Source.
We release Sekiban open source under Apache 2.0. See [License](https://github.com/J-Tech-Japan/Sekiban/blob/main/LICENSE). You can fork and fix bugs or add more features if you would like. Also anyone who is interesting in contributing, we have [Contribution Guideline](https://github.com/J-Tech-Japan/Sekiban/blob/main/CONTRIBUTING.md) and [Code of Conduct](https://github.com/J-Tech-Japan/Sekiban/blob/main/CODE_OF_CONDUCT.md). J-Tech Japan is the owner of this project and decide the future plan.

## Supports and training
J-Tech Japan accepts sponsors for this project to maintain hight quality framwork. Also if you need training or seminor, please contact to [sekibanadmin@jtechs.com](mailto:sekibanadmin@jtechs.com)



[Quick Start](./quick-start.md) to see how you can start using Sekiban.