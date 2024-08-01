module fsCustomer.Dependency

open System.Reflection
open Sekiban.Core.Dependency


type FsCustomerDependency() =
    inherit DomainDependencyDefinitionBase()

    override this.Define() = ()

    override this.GetExecutingAssembly() = Assembly.GetExecutingAssembly()
