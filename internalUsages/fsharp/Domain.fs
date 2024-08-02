module fsCustomer.Domain

open System
open System.ComponentModel.DataAnnotations
open System.IO
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

        static member HandleCommand(command, context) =
            context.AppendEvent({ Name = command.Name })

type BranchExistsQuery(branchId: Guid) =
    member this.BranchId = branchId

    interface INextAggregateQuery<Branch, bool> with
        member this.HandleFilter(list, context) =
            list
            |> Seq.exists (fun branch -> branch.AggregateId = this.BranchId)
            |> ResultBox.FromValue

type Client =
    { Name: string
      Email: string
      BranchId: Guid }

    interface IAggregatePayload<Client> with
        static member CreateInitialPayload(_) =
            { Name = ""
              Email = ""
              BranchId = Guid.Empty }

type ClientCreated(Name: string,Email: string,BranchId: Guid) =
    member this.Name = Name
    member this.Email = Email
    member this.BranchId = BranchId

    interface IEventPayload<Client, Client, ClientCreated> with
        static member OnEvent(aggregatePayload, ev) =
            { Name = ev.Payload.Name
              Email = ev.Payload.Email
              BranchId = ev.Payload.BranchId }

type ClientEmailExistsNextQuery(email: string) =
    member this.Email = email

    interface INextAggregateQuery<Client, bool> with
        member this.HandleFilter(list, context) =
            list
            |> Seq.exists (fun client -> client.Payload.Email = this.Email)
            |> ResultBox.FromValue

type CreateClient =
    {
      [<Required>]Name: string
      [<Required>]Email: string
      [<Required>]BranchId: Guid }

    interface ICommandWithHandlerAsync<Client, CreateClient> with
        member this.GetAggregateId() = Guid.NewGuid()

        static member HandleCommandAsync(command, context) =
            context
                .ExecuteQueryAsync(BranchExistsQuery(command.BranchId))
                .Verify(fun exists ->
                    if exists then
                        ExceptionOrNone.None
                    else
                        ExceptionOrNone.FromException(InvalidDataException("Branch not exists")))
                .Conveyor(fun () -> context.ExecuteQueryAsync(ClientEmailExistsNextQuery(command.Email)))
                .Verify(fun exists ->
                    if exists then
                        ExceptionOrNone.FromException(InvalidDataException("Email not exists"))
                    else
                        ExceptionOrNone.None)
                .Conveyor(fun () ->
                    context.AppendEvent(ClientCreated(command.Name, command.Email, command.BranchId)))
