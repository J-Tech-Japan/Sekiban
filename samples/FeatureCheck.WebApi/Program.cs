using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi;
using Microsoft.Azure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using Sekiban.Web.Dependency;
using Sekiban.Web.SwashbuckleHelpers;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Web Setting
builder.Services.AddSekibanWeb(new FeatureCheckWebDependency());
// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new FeatureCheckDependency(), configuration: builder.Configuration);
// Sekiban Cosmos Setting
builder.Services.AddSekibanCosmosDB(
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

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(
    config =>
    {
        config.CustomSchemaIds(x => x.FullName);
        config.SchemaFilter<NamespaceSchemaFilter>();
    });
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
