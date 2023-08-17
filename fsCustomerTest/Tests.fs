module fsCustomerTest.Tests

open System.Text.Json
open Sekiban.Core.Shared
open Sekiban.Testing.SingleProjections
open Xunit
open Xunit.Abstractions
open fsCustomer.Dependency
open fsCustomer.Domain

[<Fact>]
let ``My test`` () = Assert.True(true)




type BranchSpec(testOutputHelper: ITestOutputHelper) =
    inherit AggregateTest<Branch, FsCustomerDependency>()
    member this.TestOutputHelper = testOutputHelper

    [<Fact>]
    member this.Serialize() =
        let serialized = SekibanJsonHelper.SerializeWithGeneric<Branch>({ Name = "Japan" })
        this.TestOutputHelper.WriteLine(serialized)
        let deserialized = SekibanJsonHelper.Deserialize<Branch>(serialized)
        let serializedFromDeserialized = SekibanJsonHelper.Serialize(deserialized)
        this.TestOutputHelper.WriteLine(serializedFromDeserialized)
        Assert.Equal(serialized, serializedFromDeserialized)

    [<Fact>]
    member this.SerializeOptionChecking() =
        let options: JsonSerializerOptions = JsonSerializerOptions()
        let branch: Branch = { Name = "Japan" }
        let serialized = JsonSerializer.Serialize(branch, options)
        this.TestOutputHelper.WriteLine(serialized)
        let deserialized = JsonSerializer.Deserialize<Branch>(serialized, options)

        let serializedFromDeserialized =
            JsonSerializer.Serialize<Branch>(deserialized, options)

        this.TestOutputHelper.WriteLine(serializedFromDeserialized)
        Assert.Equal(serialized, serializedFromDeserialized)

    [<Fact>]
    member this.CreateAggregate() =
        this.WhenCommand(CreateBranch("Japan")).ThenPayloadIs({ Name = "Japan" })
