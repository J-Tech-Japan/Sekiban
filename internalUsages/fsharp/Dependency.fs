module fsCustomer.Dependency

open System.Reflection
open Sekiban.Core.Dependency
open fsCustomer.Domain


type FsCustomerDependency() =
    inherit DomainDependencyDefinitionBase()

    override this.Define() =
        do
            this.AddAggregate<Client>().AddAggregateQuery<ClientEmailExistsQuery>()
            |> ignore

    override this.GetExecutingAssembly() = Assembly.GetExecutingAssembly()
