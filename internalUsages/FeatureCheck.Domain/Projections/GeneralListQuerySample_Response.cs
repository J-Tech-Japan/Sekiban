using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections;

public record GeneralListQuerySample_Response(string Name, string BranchName) : IQueryResponse;
