using System;
namespace SampleProjectStoryXTest.Stories;

public interface IQueryExecutor<in TInput, out TOutput>
    where TInput : IInput<TOutput>
{
    TOutput Execute(TInput input);
}
public class QueryExecutor<TInput, TOutput> : IQueryExecutor<TInput, TOutput>
    where TInput : IInput<TOutput>
{
    public TOutput Execute(TInput input) =>
        throw new NotImplementedException("This is a dummy implementation. Please replace it with your own implementation.");
}
public interface IInput<out T>
{
}
public record Input(int Value) : IInput<bool>
{
}
public interface IQuery<in TInput, out TOutput>
{
    TOutput Execute(TInput input);
}
public class Query : IQuery<Input, bool>
{
    public bool Execute(Input input) => throw new NotImplementedException();
}
public class ClassRelationTest
{

    public void Test()
    {
        var query = new Query();
        var input = new Input(1);
        var result = query.Execute(input);
    }
}
