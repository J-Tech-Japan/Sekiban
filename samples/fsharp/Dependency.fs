module fsCustomer.Dependency

open System.Reflection
open Sekiban.Core.Dependency
open fsCustomer.Domain


type FsCustomerDependency() =
    inherit DomainDependencyDefinitionBase()

    override this.Define() =
        do
            this
                .AddAggregate<Branch>()
                .AddCommandHandler<CreateBranch, CreateBranchHandler>()
            |> ignore

            this
                .AddAggregate<Client>()
                .AddCommandHandler<CreateClient, CreateClientHandler>()
                .AddAggregateQuery<ClientEmailExistsQuery>()
            |> ignore

    override this.GetExecutingAssembly() = Assembly.GetExecutingAssembly()
