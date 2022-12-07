namespace SampleProjectStoryXTest.Stories;

// public interface IQueryExecutor
// {
//     Task<ListQueryResult<TOutput>> ExecuteListQueryAsync<TOutput>(IInput<TOutput> input)
//         where TOutput : IOutput;
//     Task<TOutput> ExecuteQueryAsync<TOutput>(IInput<TOutput> input)
//         where TOutput : IOutput;
// }
// public class QueryExecutor : IQueryExecutor
// {
//     public Task<ListQueryResult<TOutput>> ExecuteListQueryAsync<TOutput>(IInput<TOutput> input) where TOutput : IOutput =>
//         throw new NotImplementedException();
//     public Task<TOutput> ExecuteQueryAsync<TOutput>(IInput<TOutput> input) where TOutput : IOutput => throw new NotImplementedException();
// }
// public interface IOutput
// {
// }
// public interface IInput<TOutput> where TOutput : IOutput
// {
// }
// public record Input(int Value) : IInput<Output1>
// {
// }
// public record Output1 : IOutput
// {
// }
// public interface IQueryHandler<TInput>
// {
// }
// public interface IQuery<TAggregatePayload, TInput, TOutput> : IQueryHandler<TInput>
//     where TAggregatePayload : IAggregatePayload, new()
//     where TInput : IInput<TOutput>
//     where TOutput : IOutput
// {
//     public IEnumerable<TOutput> HandleFilter(
//         TInput param,
//         IEnumerable<AggregateState<TAggregatePayload>> list);
//
//     public IEnumerable<TOutput> HandleSort(TInput param, IEnumerable<TOutput> filteredList);
// }
// public class Query : IQuery<Client, Input, Output1>
// {
//     public IEnumerable<Output1> HandleFilter(Input param, IEnumerable<AggregateState<Client>> list) => throw new NotImplementedException();
//     public IEnumerable<Output1> HandleSort(Input param, IEnumerable<Output1> filteredList) => throw new NotImplementedException();
//     public Task<Output1> ExecuteAsync(Input input) => throw new NotImplementedException();
// }
// public class ClassRelationTest
// {
//
//     public void Test()
//     {
//         var query = new Query();
//         var input = new Input(1);
//         var result = query.ExecuteAsync(input);
//     }
//     public async Task Test2()
//     {
//         var executor = new QueryExecutor();
//
//         var input = new Input(1);
//         var result1 = await executor.ExecuteQueryAsync(input);
//         var result2 = await executor.ExecuteListQueryAsync(input);
//     }
// }
