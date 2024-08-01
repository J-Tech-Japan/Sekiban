module fsCustomer.Domain
open System
open System.IO
open System.Threading.Tasks
open FSharp.Control
open Microsoft.FSharp.Control
open Sekiban.Core.Aggregate
open Sekiban.Core.Command
open Sekiban.Core.Events
open Sekiban.Core.Query.QueryModel
open ResultBoxes
type Branch =
    { Name: string }

    interface IAggregatePayload<Branch> with
        static member CreateInitialPayload(_ :Branch): Branch = { Name = "" }

type BranchCreated =
    { Name: string }

    interface IEventPayload<Branch, Branch, BranchCreated> with
        static member OnEvent(aggregatePayload, ev) = { Name = ev.Payload.Name }

type CreateBranch(name: string) =
    member this.Name = name

    interface ICommandWithHandler<Branch,CreateBranch> with
        member this.GetAggregateId() = Guid.NewGuid()
        member this.GetRootPartitionKey() = failwith "todo"
        static member HandleCommand(command, context) = context.AppendEvent({ Name = command.Name })

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

type CreateClientHandler(queryExecutor: IQueryExecutor) =
    member this.QueryExecutor = queryExecutor

    // interface ICommandHandlerAsync<Client, CreateClient> with
    //     member this.HandleCommandAsync(command, _) =
    //         taskSeq {
    //             let emailExistsQueryResponse =
    //                 this.QueryExecutor.ExecuteAsync({ Email = command.Email })
    //                 |> Async.AwaitTask
    //                 |> Async.RunSynchronously
    //
    //             if not emailExistsQueryResponse.Exists then
    //                 yield
    //                     { Name = command.Name
    //                       Email = command.Email
    //                       BranchId = command.BranchId }
    //                     :> IEventPayloadApplicableTo<Client>
    //         }

           
type CreateClient =
    { Name: string
      Email: string
      BranchId: Guid }

    interface ICommandWithHandlerAsync<Client,CreateClient> with
        member this.GetAggregateId() = Guid.NewGuid()
        member this.GetRootPartitionKey() = failwith "todo"
        static member HandleCommandAsync(command, context) =
            async {
                let queueExecutor = context.GetRequiredService<IQueryExecutor>()
                let! emailExistsQueryResponse = queueExecutor.Conveyor (fun (x:IQueryExecutor) -> x.ExecuteWithResultAsync({ Email = command.Email })) |> Async.AwaitTask 
                let exists = (emailExistsQueryResponse.Verify (fun (x:ClientEmailExistsQueryResponse) -> if x.Exists then ExceptionOrNone.FromException(InvalidDataException("Email not exists")) else ExceptionOrNone.None))
                if exists.IsSuccess = false || exists.GetValue().Exists then
                    return ResultBox.FromException<UnitValue>(InvalidOperationException("Email already exists"))
                else
                    let event : ClientCreated = { Name = command.Name; Email = command.Email; BranchId = command.BranchId }
                    return context.AppendEvent(event)
            } |> Async.StartAsTask
          