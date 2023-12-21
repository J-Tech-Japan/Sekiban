# Quick Start


## Test it out.

To start quickly, we recommend you to start testing sekiban with already set up get started project.
It is located in the repository, `/Tutorials/1.GetStarted` folder.

### Open GetStarted Solution.



Get Started Project is made with .NET 7.There is multiple way to test it out GetStarted Solution


1. Using Cosmos DB  [Test out with Cosmos DB](./test-out-cosmos.md).
2. Using Dynamo DB  [Test out with Dynamo DB](./test-out-dynamo.md).


### Create your own Sekiban Project.

Do make your own project, usually you need three projects.

To make your own project, you can refer Tutorial Project in `/Tutorials/1.GetStarted` folder.

1. `Domain Project`. This project is usually only includes Aggregate, Command, Event, Projection, Query etc. core event sourcing contents.

    `Domain Project` will add `Sekiban.Core` Nuget Package.

    `Domain Project` includes  Aggregate, Command, Event, Projection, Query etc.

    `Domain Project` includes `Domain Dependency Definition`.

2. `Test Project`. This project tests Domain Project.

    `Test Project` will reference `Domain Project`.
    `Test Project` will add `Sekiban.Testing` Nuget Package.

    `Test Project` will include `Aggregate Test` and/or `Unified Test`.

3. `Executing Project`. This project is either console application, web application, function application or any other executing application form. In many cases, it is Web API interface to access `Domain Project`.

    `Executing Project` will reference `Domain Project`.

    `Executing Project` will add Infrastructure project. Currently Sekiban supports `Azure Cosmos DB` and `AWS Dynamo DB`.

    If `Executing Project` will use Azure Cosmos DB, add `Sekiban.Infrastructure.Cosmos` Nuget package.

    If `Executing Project` will use AWS Dynamo DB, add `Sekiban.Infrastructure.Dynamo` Nuget Package.

    When `Executing Project` will be `Web API Project that uses Sekiban.Web API Generator`, it will includes following items.
    
    - Add `Sekiban.Web` Nuget Package.
    - `Web Dependency Definition` that inherits IWebDependencyDefinition.
    - `Program.cs` will have `AddSekibanCoreWithDependency` as Sekiban.Core Settings.
    - `Program.cs` will have `AddSekibanCosmosDB` as Infrastructure Settings.
    - `Program.cs` will have `AddSekibanWeb` as Web Settings.

Those are basic Project Settings. Detail information will follow with  [Sekiban Event Sourcing Basics](./sekiban-event-sourcing-basics.md).