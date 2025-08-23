# Sekiban - Event Sourcing and CQRS Framework using C#

Sekiban - Event Sourcing and CQRS Framework using C#. It can store data into Azure Cosmos DB or PostgreSQL

<p align="center">
  <img alt="Sekiban Logo" src="./docs/images/Sekiban_Signature.svg" width="600">
</p>

## (NEW) Sekiban new version - Sekiban.Pure.Orleans

We moved main repository for the new Sekiban.Pure.Orleans and Sekiban.Pure.Dapr project and Sekiban Typescript Dapr. Past project Sekiban.Core and upto 0.23 version to core_main branch of this repository.

[Sekiban core_main branch](https://github.com/J-Tech-Japan/Sekiban/tree/core_main)

mainレポジトリは、新しく開発中の Sekiban.Pure.Orleansプロジェクト、Sekiban.Pure.Daprと Sekiban Typescript Dapr のみを残したものとなっています。旧バージョンは、core_mainブランチに残しています。
[Sekiban core_main branch](https://github.com/J-Tech-Japan/Sekiban/tree/core_main)



## What is Sekiban?

**Sekiban** is an application development framework that leverages **Event Sourcing and CQRS** principles. Built with C#
and .NET Core 9+, it allows developers to use a declarative API to create event sourcing applications. Sekiban now supports multiple runtime environments including [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/overview) with actor model for enhanced scalability and performance in multi-threaded environments, and [Dapr](https://dapr.io/) for distributed application development. Sekiban supports
various event stores such as [Microsoft Azure Cosmos DB](https://azure.microsoft.com/en-us/products/cosmos-db)
and PostgreSQL, facilitating deployment on Azure, AWS, or any cloud platform.

## Available Implementations

Sekiban is available in multiple implementations, all supporting Azure Cosmos DB or PostgreSQL as event stores:

### C# with Microsoft Orleans
The C# implementation with Microsoft Orleans provides high-performance event sourcing using the actor model. This implementation offers exceptional scalability and performance in multi-threaded environments.

Create a new C# Orleans Sekiban application with:

```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-orleans-aspire -n YourProjectName
```

### C# with Dapr
The C# implementation with Dapr integration provides distributed event sourcing using virtual actors.

Create a new C# Dapr Sekiban application with:

```bash
dotnet new install Sekiban.Pure.Templates
dotnet new sekiban-dapr-aspire -n YourProjectName
```

### TypeScript with Dapr (Alpha)
The TypeScript implementation with Dapr offers the same event sourcing capabilities for Node.js developers in alpha version. It supports both in-memory storage for development and Azure Cosmos DB or PostgreSQL with Dapr actors for production use. Create a new TypeScript Sekiban application with:

```bash
npx create-sekiban-app my-app
```

This creates a complete TypeScript application with domain models, REST API, event handlers, and projectors. Learn more in the [TypeScript packages directory](./ts).

## Getting started


Do you want to start coding? If so, please see [Readme Pure](https://github.com/J-Tech-Japan/Sekiban/blob/main/README_Sekiban_Pure.md)

日本語の情報をお探しでしょうか？ [Sekiban の紹介](https://github.com/J-Tech-Japan/Sekiban/blob/main/README_Sekiban_Pure_JP.md) のページから幾らかの記事が日本語で書かれています。開発者は日本語も分かりますので日本語の情報も増やしていきたいと思っています。

## AI Programming?

## Sekiban MCP (Model Context Protocol)

Sekiban provides MCP (Model Context Protocol) support to enhance your development experience with AI coding assistants. The Sekiban MCP server offers direct access to Sekiban documentation and examples through Server-Sent Events (SSE).

**MCP Server URL:** 
https://sekiban-doc-mcp.azurewebsites.net/
if this does not work in your case, try below.
https://sekiban-doc-mcp.azurewebsites.net/sse

### Adding Sekiban MCP to Claude Desktop

To register Sekiban MCP with Claude Desktop, run the following command:

```bash
claude mcp add sekibanDocument --transport sse https://sekiban-doc-mcp.azurewebsites.net/sse
```

This integration allows you to access Sekiban documentation, examples, and best practices directly from your AI assistant, making development more efficient and informed.

## DCB (Dynamic Consistency Boundary)

Sekiban is adopting the DCB (Dynamic Consistency Boundary) approach to offer a simpler and more flexible consistency model for event‑driven systems. DCB, introduced by Sara Pellegrini in the blog post "Killing the Aggregate", replaces rigid per‑aggregate transactional boundaries with a context‑sensitive consistency boundary based on the set of events relevant to a decision. Instead of maintaining multiple streams and coordinating cross‑aggregate invariants via sagas and compensating events, DCB allows a single event stream per bounded context where each event can carry multiple tags representing the entities or concepts it affects. Querying becomes a matter of filtering by event types and/or tags, enabling you to enforce strong consistency only where it truly matters while preserving scalability and simplicity. Learn more at https://dcb.events

Current status in Sekiban:

- C# implementation: actively under development inside this repository.
- Data store: PostgreSQL already supported for the new DCB flow (additional providers may follow).
- Upcoming query features: composite filtering using combinations of event types and multiple tags to express richer consistency and projection conditions.
- Release plan: packaging the DCB components as a NuGet library is in progress; an initial preview will be published soon.

Our goal is to let you model cross‑entity invariants without saga complexity, keeping a clean decision model and leveraging optimistic concurrency over dynamic query scopes instead of fixed aggregate streams.

## Sponsors

Sekiban is an Apache 2.0 open source project with its ongoing development. We will launch soon for the sponsorship information. If you are interested in the special sponsorship, please [contact us](https://github.com/J-Tech-Japan/Sekiban#support-and-training). Also, you can sponsor us from [Github Sponsors](https://github.com/sponsors/J-Tech-Japan)

<p align="center">
  <h3 align="center">Special Sponsor</h3>
</p>

<p align="center">
  <a target="_blank" href="https://www.jtsnet.co.jp">
  <img alt="special sponsor jts" src="./docs/images/jtslogo.png" width="500">
  </a>
</p>


## Understanding Event Sourcing and CQRS

If you've landed here, you're likely already familiar with Event Sourcing and CQRS. However, if you require more
detailed insights, Greg Young's informative YouTube talk might be of assistance. Check out
the [transcript](https://www.kurrent.io/blog/transcript-of-greg-youngs-talk-at-code-on-the-beach-2014-cqrs-and-event-sourcing)
of Greg's presentation at the Code on the Beach 2014 event on CQRS and Event Sourcing.
Greg suggests creating your event sourcing system instead of relying on frameworks, a viewpoint I share, as constructing
your framework can deepen your understanding of event sourcing concepts.

However, we recognize many developers prefer concentrating on business logic and swiftly proceeding with tools and
document databases. Given the recent advances in database speed and scalability across the cloud, event sourcing can be
quite efficient with the use of frameworks and cloud solutions.
Fortunately, as Sekiban is open-source, developers wishing to delve deeper can experiment with the source code housed in
the repository.

***Disclaimer:*** Despite using a framework, it's essential for developers to be familiar with event sourcing concepts,
especially when optimizing Query performance for a growing user base and data set post-launch. Adequate knowledge of
Event Sourcing and CQRS is mandatory when converting it to a microservice or improving performances.

## Key Features of Sekiban

Enhance your application development experience with Sekiban suited for those who prioritize efficiency and
effectiveness:

- **Simple Commands and Events:** Facilitates straightforward creation of functionality within your application.
- **Publish-only Commands and Events:** Provides way to save events without projecting aggregates.
- **Optimistic Aggregate Version Check:** Enables efficient conflict detection in concurrent environments.
- **Event Versions:** Allows for improved forward compatibility.
- **Single/Multi Aggregate Projections:** Supports projections at both individual and multiple aggregate levels.
- **Projections Snapshots:** Offers the ability to capture the state of a projection at a certain point in time.
- **Large Snapshots Storage:** Accommodate sizable snapshots using Azure Blob Storage or Amazon S3.
- **Built-in Testing Framework:** Simplifies testing with an integrated suite.
- **Azure Cosmos DB / Dynamo DB Datastore:** Provides scalable and globally distributed data storage solutions.
- **Tenant Partitions:** Facilitates separation of data based on tenant for easier data management and handling.
- **Query from Multiple Projections:** Enables querying from a range of projections for more dynamic data analysis.
- **Command and Query Web API Generator with Swagger Support:** Streamlines API design, building, and documentation.

These, coupled with many more features, allow developers to focus on business aspects, implementing commands, events,
projections, tests, and queries for practical solutions without unnecessary complexity.

## Compatibility

Sekiban Pure is compatible with .NET 9.

For **Azure Cosmos DB**, Sekiban supports
the [Hierarchical Partition Key](https://learn.microsoft.com/en-us/azure/cosmos-db/hierarchical-partition-keys?tabs=net-v3%2Cbicep)
feature. This enables more efficient querying using Azure Cosmos DB by creating a Tenant (or Root Partition Key), Aggregate
Type, and Partition Key.

When using **Dynamo DB** with Sekiban, you need to set up a Partition Key and Sort Key.

## Scale of Sekiban Projects

As of Sekiban version 1.0.0.Preview, we are working on built-in materialized view helper features. Sekiban's Live
Projection capability is well-suited for small to medium-sized systems. Typically, Sekiban can handle Live Projections
provided the system memory can accommodate the size of the Multiple Projections.

For more insights on Live Projections, please reference this informative article by Anton
Stöckl: [Live Projections for Read Models with Event Sourcing and CQRS](https://medium.com/@TonyBologni/live-projections-for-read-models-with-event-sourcing-and-cqrs-42dd18b19fd0).

Developers can implement materialized views using Azure Cosmos DB's and Dynamo DB's change feed feature. Although creation of
materialized views with Change Feed is a straightforward process, it does not directly pertain to the Sekiban
Framework's core functions, hence it hasn't been included within the framework.

## Open Source

Sekiban is released as open source under the Apache 2.0 license. You can view
our [License](https://github.com/J-Tech-Japan/Sekiban/blob/main/LICENSE) here. We welcome developers to fork the
project, fix bugs, or add new features as they wish. For those interested in contributing, we offer a
detailed [Contribution Guideline](https://github.com/J-Tech-Japan/Sekiban/blob/main/CONTRIBUTING.md)
and [Code of Conduct](https://github.com/J-Tech-Japan/Sekiban/blob/main/CODE_OF_CONDUCT.md). J-Tech Japan owns this
project and determines its future direction.

## Documentation

We are planning to deploy documentation on [Sekiban.dev](https://www.sekiban.dev), we have not done yet. eventually please see

https://github.com/J-Tech-Japan/Sekiban/blob/main/docs/docfx_project/articles/intro.md

## Support and Training

J-Tech Japan welcomes sponsors for this project to help maintain a high-quality framework. If you require training or
seminars, please contact us at [sekibanadmin@jtechs.com](mailto:sekibanadmin@jtechs.com).

[Contribution Guideline](https://github.com/J-Tech-Japan/Sekiban/blob/main/CONTRIBUTING.md)

[Code of Conduct](https://github.com/J-Tech-Japan/Sekiban/blob/main/CODE_OF_CONDUCT.md)

## About Us
**J-Tech Japan (株式会社ジェイテックジャパン)** has been developing sekiban since 2022. We are located in Tokyo, Japan and we have been developing enterprise web application since 2008.
<p align="center">
  <a target="_blank" href="https://www.jtechs.com/japan/">
  <img alt="developer J-Tech Japan." src="./docs/images/jtechjapanlogo.svg" width="500">
  </a>
</p>

## Contributors

<a href="https://github.com/J-Tech-Japan/Sekiban/graphs/contributors">
  <img alt="contributors" src="https://contrib.rocks/image?repo=J-Tech-Japan/Sekiban"/>
</a>

# License

Apache 2.0
[See License](https://github.com/J-Tech-Japan/Sekiban/blob/main/LICENSE)

Copyright (c) 2022- J-Tech Japan
