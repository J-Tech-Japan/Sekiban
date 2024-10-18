using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Shared;
using FeatureCheck.Domain.Usecases;
using Microsoft.Azure.Cosmos;
using Sekiban.Core.Command;
using Sekiban.Core.Dependency;
using Sekiban.Core.Usecase;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency<FeatureCheckDependency>();

// Sekiban Cosmos Setting
builder.AddSekibanCosmosDb(
    options => options with
    {
        // this is same as default but for sample, it is explicitly written.
        ClientOptions = new CosmosClientOptions
        {
            Serializer = new SekibanCosmosSerializer(),
            AllowBulkExecution = true,
            MaxRetryAttemptsOnRateLimitedRequests = 200,
            ConnectionMode = ConnectionMode.Gateway,
            GatewayModeMaxConnectionLimit = 200
        }
    });

// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<FeatureCheckDependency>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app
    .MapPost("/api/createbranchandclient", SekibanUsecase.CreateSimpleExecutorAsync<AddBranchAndClientUsecase, bool>())
    .WithName("CreateBranchAndClient")
    .WithOpenApi();

app
    .MapPost("/api/createbranch", CommandExecutor.CreateSimpleCommandExecutor<CreateBranch>())
    .WithName("CreateBranch")
    .WithOpenApi();


// app
//     .MapPost(
//         "/api/createbranchandclient",
//         async ([FromBody] AddBranchAndClientUsecase usecase, [FromServices] ISekibanUsecaseContext context) =>
//         await AddBranchAndClientUsecase
//             .ExecuteAsync(usecase, context)
//             .Match(success => Results.Ok(), exception => Results.Problem(exception.Message)))
//     .WithName("GetWeatherForecast")
//     .WithOpenApi();

app.UseAuthorization();

app.MapControllers();
app.Run();
