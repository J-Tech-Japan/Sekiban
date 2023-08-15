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
        let serialized = SekibanJsonHelper.Serialize(Branch("Japan"))
        this.TestOutputHelper.WriteLine(serialized)
        let deserialized = SekibanJsonHelper.Deserialize<Branch>(serialized)
        let serializedFromDeserialized = SekibanJsonHelper.Serialize(deserialized)
        this.TestOutputHelper.WriteLine(serializedFromDeserialized)
        Assert.Equal(serialized, serializedFromDeserialized)

    [<Fact>]
    member this.SerializeOptionChecking() =
        let options: JsonSerializerOptions = JsonSerializerOptions()
        //      options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        //        options.PropertyNameCaseInsensitive = true
        let serialized = JsonSerializer.Serialize<Branch>(Branch("Japan"), options)
        this.TestOutputHelper.WriteLine(serialized)
        let deserialized = JsonSerializer.Deserialize<Branch>(serialized, options)

        let serializedFromDeserialized =
            JsonSerializer.Serialize<Branch>(deserialized, options)

        this.TestOutputHelper.WriteLine(serializedFromDeserialized)
        Assert.Equal(serialized, serializedFromDeserialized)

    [<Fact>]
    member this.CreateAggregate() =
        this.WhenCommand(CreateBranch("Japan")).ThenPayloadIs(Branch("Japan"))
