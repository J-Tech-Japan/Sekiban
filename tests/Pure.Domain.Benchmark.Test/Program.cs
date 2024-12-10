using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Pure.Domain;
using ResultBoxes;
using Sekiban.Pure;
using System.Reflection;
namespace Pure.Benchmark.Test;

// [SimpleJob(RuntimeMoniker.Net90, 1, 5, 10, 10000, baseline: true)]
// [SimpleJob(RuntimeMoniker.NativeAot90, 1, 5, 10, 10000)]
[SimpleJob(RuntimeMoniker.Net90, 1, 5, 10, 3000, baseline: true)]
[SimpleJob(RuntimeMoniker.NativeAot90, 1, 5, 10, 3000)]
// [SimpleJob(RuntimeMoniker.Net90, 1, 5, 10, 100, baseline: true)]
// [SimpleJob(RuntimeMoniker.NativeAot90, 1, 5, 10, 100)]
public class DomainTest
{

    [Benchmark]
    public Task<ResultBox<CommandResponse>> WithReflection()
    {
        Console.WriteLine("WithReflection start");
        var executor = new CommandExecutor { EventTypes = new CpPureDomainEventTypes() };
        Console.WriteLine("executor created");
        var createCommand = new RegisterBranch("a");
        Console.WriteLine("createCommand created");
        return executor.ExecuteGeneralNonGeneric(
            createCommand,
            new BranchProjector(),
            createCommand.SpecifyPartitionKeys,
            null,
            createCommand.Handle,
            OptionalValue<Type>.Empty);
        // result.Log("with");
    }

    [Benchmark]
    public Task<ResultBox<CommandResponse>> WithoutReflection()
    {
        Console.WriteLine("WithoutReflection start");
        var executor = new CommandExecutor { EventTypes = new CpPureDomainEventTypes() };
        Console.WriteLine("executor created");
        var createCommand = new RegisterBranch("a");
        Console.WriteLine("createCommand created");
        var method = executor
                .GetType()
                .GetMethod(nameof(CommandExecutor.ExecuteGeneral), BindingFlags.Public | BindingFlags.Instance) ??
            throw new ApplicationException("Method not found");
        Console.WriteLine("method created");
        var generic = method.MakeGenericMethod(typeof(RegisterBranch), typeof(NoInjection), typeof(IAggregatePayload));
        Console.WriteLine("generic created");
        return (Task<ResultBox<CommandResponse>>)generic.Invoke(
            executor,
            new object[]
            {
                createCommand,
                new BranchProjector(),
                createCommand.SpecifyPartitionKeys,
                OptionalValue<NoInjection>.Empty,
                createCommand.Handle
            });
        // var result = await executor.ExecuteGeneral<RegisterBranch, NoInjection, IAggregatePayload>(
        //     createCommand,
        //     ((ICommandGetProjector)createCommand).GetProjector(),
        //     createCommand.SpecifyPartitionKeys,
        //     OptionalValue<NoInjection>.Empty,
        //     createCommand.Handle);
        // result.Log("without");
        // var result = await executor.ExecuteGeneralNonGeneric(
        //     createCommand,
        //     new BranchProjector(),
        //     createCommand.SpecifyPartitionKeys,
        //     null,
        //     createCommand.Handle,
        //     OptionalValue<Type>.Empty);
        // result.Log("with2");
    }

    [Benchmark]
    public Task<ResultBox<CommandResponse>> WithoutReflection2()
    {
        var executor = new CommandExecutor { EventTypes = new CpPureDomainEventTypes() };
        var createCommand = new RegisterBranch("a");
        // return executor.Execute(createCommand);
        return executor.ExecuteGeneral<RegisterBranch, NoInjection, IAggregatePayload>(
            createCommand,
            new BranchProjector(),
            createCommand.SpecifyPartitionKeys,
            OptionalValue<NoInjection>.Empty,
            createCommand.Handle);
    }
}
public class Program
{
    public static async Task Main(string[] args)
    {
        if (true)
        {
            // var config = new ManualConfig()
            //     .AddJob()
            //     .AddLogger();
            // var config = DefaultConfig.Instance.AddJob(Job.ShortRun);
            // var summary = BenchmarkRunner.Run<DomainTest>(config);
            var summary = BenchmarkRunner.Run<DomainTest>();
        } else
        {
            var test = new DomainTest();
            Console.WriteLine("test start");
            await test.WithReflection().Log();
            Console.WriteLine("test WithReflection end");
            await test.WithoutReflection2().Log();
            Console.WriteLine("test WithoutReflection2 end");
            await test.WithoutReflection().Log();
            Console.WriteLine("test WithoutReflection end");
            // Console.WriteLine("test");
            // var r = ResultBox.FromValue(1);
            // var r2 = ResultBox.FromValue(2);
            // var r3 = ResultBox.FromValue(3);
            // r.Log();
            // r2.Log();
            // r3.Log();

            // await test.WithoutReflection();
            // await test.WithoutReflection2();
            // await test.WithReflection();
            // }
        }
    }
    // public static async Task Main(string[] args)
    // {
    //     var test = new DomainTest();
    //     await test.WithoutRefrection().Log();
    //     await test.WithRefrection().Log();
    // }
}
