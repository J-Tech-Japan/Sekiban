using FeatureCheck.Domain.Shared;
using Microsoft.Azure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency<FeatureCheckDependency>();

// Sekiban Cosmos Setting
builder.AddSekibanCosmosDB(
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
builder.AddSekibanWebFromDomainDependency<FeatureCheckDependency>(web => web.AllowAllIfLoggedIn());
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
