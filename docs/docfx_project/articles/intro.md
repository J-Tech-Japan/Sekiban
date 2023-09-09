# What is Sekiban?

**Sekiban** is application development framework using **Event Sourcing**. Sekiban uses C# and dotnet 7+, and developers can use declarative api to build event sourcing applications. As a event store, sekiban can use [Microsoft Azure Cosmos DB](https://azure.microsoft.com/en-us/products/cosmos-db) and [Amazon Dynamo DB](https://aws.amazon.com/dynamodb/). Application can be deploy either azure or AWS.

## Sekiban has following main features for now.

- Simple Commands and Events
- Publish only commands
- Optimistic aggregate version check
- Event versions
- Single Aggregate Projections
- Multi Aggregate Live Projections
- Single Aggregate / Multi Aggregate Projections Snapshots
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

## How big can Sekiban Project can be?
Current version of Sekiban (0.14) does not have build in materialized view helper features. Sekiban's Live Projection can be handle small and midium size systems. For Live Projection Please see the great article from Anton Stöckl [Live Projections for Read Models with Event Sourcing and CQRS](https://medium.com/@TonyBologni/live-projections-for-read-models-with-event-sourcing-and-cqrs-42dd18b19fd0). Developers can implement using Cosmos DB and Dynamo DB change feed. Although it is very simple to create materialized view using Change Feed, it is not tightly related with Sekiban Framework, so we did not include in the framework.

## Open Source.
We release Sekiban open source under Apache 2.0. See [License](https://github.com/J-Tech-Japan/Sekiban/blob/main/LICENSE). You can fork and fix bugs or add more features if you would like. Also anyone who is interesting in contributing, we have [Contribution Guideline](https://github.com/J-Tech-Japan/Sekiban/blob/main/CONTRIBUTING.md) and [Code of Conduct](https://github.com/J-Tech-Japan/Sekiban/blob/main/CODE_OF_CONDUCT.md). J-Tech Japan is the owner of this project and decide the future plan.

## Supports and training
J-Tech Japan accepts sponsors for this project to maintain hight quality framwork. Also if you need training or seminor, please contact to [sekibanadmin@jtechs.com](mailto:sekibanadmin@jtechs.com)



[Quick Start](./quick-start) to see how you can start using Sekiban.