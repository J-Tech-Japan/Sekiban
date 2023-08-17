module fsCustomer.Domain

open System
open FSharp.Control
open Sekiban.Core.Aggregate
open Sekiban.Core.Command
open Sekiban.Core.Events
open Sekiban.Core.Query.QueryModel

type Branch =
    { Name: string }

    interface IAggregatePayload with
        member this.CreateInitialPayload() = { Name = "" }

type CreateBranch(name: string) =
    member this.Name = name

    interface ICommand<Branch> with
        member this.GetAggregateId() = Guid.NewGuid()

type BranchCreated =
    { Name: string }

    interface IEventPayload<Branch, Branch, BranchCreated> with
        static member OnEvent(aggregatePayload, ev) = { Name = ev.Payload.Name }

type CreateBranchHandler() =
    interface ICommandHandler<Branch, CreateBranch> with
        member this.HandleCommandAsync(getAggregateState, command) =
            taskSeq { yield { Name = command.Name } :> IEventPayloadApplicableTo<Branch> }


type Client(name: string, email: string, branchId: Guid) =
    member this.Name = name
    member this.Email = email
    member this.BranchId = branchId

    interface IAggregatePayload with
        member this.CreateInitialPayload() = Client("", "", Guid.Empty)

    new() = Client("", "", Guid.Empty)

type CreateClient =
    { Name: string
      Email: string
      BranchId: Guid }

    interface ICommand<Client> with
        member this.GetAggregateId() = Guid.NewGuid()

type ClientCreated =
    { Name: string
      Email: string
      BranchId: Guid }

    interface IEventPayload<Client, Client, ClientCreated> with
        static member OnEvent(aggregatePayload, ev) =
            Client(ev.Payload.Name, ev.Payload.Email, ev.Payload.BranchId)

type ClientEmailExistsQueryResponse =
    { Exists: bool }

    interface IQueryResponse

type ClientEmailExistsQueryParam =
    { Email: string }

    interface IQueryParameter<ClientEmailExistsQueryResponse>



type ClientEmailExistsQuery =
    interface IAggregateQuery<Client, ClientEmailExistsQueryParam, ClientEmailExistsQueryResponse> with
        member this.HandleFilter(queryParam, list) =
            { Exists = list |> Seq.exists (fun client -> client.Payload.Email = queryParam.Email) }

type CreateClientHandler(queryExecutor: IQueryExecutor) =
    member this.QueryExecutor = queryExecutor

    interface ICommandHandler<Client, CreateClient> with
        member this.HandleCommandAsync(getAggregateState, command) =
            taskSeq {
                let emailExistsQueryResponse =
                    this.QueryExecutor.ExecuteAsync({ Email = command.Email })
                    |> Async.AwaitTask
                    |> Async.RunSynchronously

                if not emailExistsQueryResponse.Exists then
                    yield
                        { Name = command.Name
                          Email = command.Email
                          BranchId = command.BranchId }
                        :> IEventPayloadApplicableTo<Client>
            }
