module fsCustomer.Domain

open System
open System.IO
open FSharp.Control
open Sekiban.Core.Aggregate
open Sekiban.Core.Command
open Sekiban.Core.Events
open Sekiban.Core.Query.QueryModel
open ResultBoxes

type Branch =
    { Name: string }

    interface IAggregatePayload<Branch> with
        static member CreateInitialPayload(_: Branch) : Branch = { Name = "" }

type BranchCreated =
    { Name: string }

    interface IEventPayload<Branch, Branch, BranchCreated> with
        static member OnEvent(aggregatePayload, ev) = { Name = ev.Payload.Name }

type CreateBranch(name: string) =
    member this.Name = name

    interface ICommandWithHandler<Branch, CreateBranch> with
        member this.GetAggregateId() = Guid.NewGuid()
        member this.GetRootPartitionKey() = failwith "todo"

        static member HandleCommand(command, context) =
            context.AppendEvent({ Name = command.Name })

type BranchExistsQuery =
    { BranchId: Guid }

    interface INextAggregateQuery<Branch, bool> with
        member this.HandleFilter(list, context) =
            list
            |> Seq.exists (fun branch -> branch.AggregateId = this.BranchId)
            |> ResultBox.FromValue

type Client(name: string, email: string, branchId: Guid) =
    member this.Name = name
    member this.Email = email
    member this.BranchId = branchId

    interface IAggregatePayload<Client> with
        static member CreateInitialPayload(_) = Client("", "", Guid.Empty)

    new() = Client("", "", Guid.Empty)


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
         
type ClientEmailExistsNextQuery =
 { Email: string }
 
 interface INextAggregateQuery<Client, bool> with
     member this.HandleFilter(list, context) =
         list
         |> Seq.exists (fun client -> client.Payload.Email = this.Email)
         |> ResultBox.FromValue
           
type CreateClient =
    { Name: string
      Email: string
      BranchId: Guid }

    interface ICommandWithHandlerAsync<Client, CreateClient> with
        member this.GetAggregateId() = Guid.NewGuid()

        static member HandleCommandAsync(command, context) =
            task {
                let! handler =
                    let branchExistsQuery : BranchExistsQuery = { BranchId = command.BranchId }
                    let query : ClientEmailExistsQueryParam = { Email = command.Email }
                    context
                        .ExecuteQueryAsync(branchExistsQuery)
                        .Verify(fun x -> if not x then ExceptionOrNone.FromException(InvalidDataException("Branch not exists")) else ExceptionOrNone.None)
                        .Conveyor(fun () -> context.ExecuteQueryAsync(query))
                        .Verify(fun (x: ClientEmailExistsQueryResponse) ->
                            if x.Exists then
                                ExceptionOrNone.FromException(InvalidDataException("Email not exists"))
                            else
                                ExceptionOrNone.None)
                        .Conveyor(fun (x: ClientEmailExistsQueryResponse) ->
                            let event: ClientCreated =
                                { Name = command.Name
                                  Email = command.Email
                                  BranchId = command.BranchId }

                            context.AppendEvent(event))

                return handler
            }
