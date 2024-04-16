using AspireAndSekibanSample.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Sekiban.Aspire.Infrastructure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddSekibanWithDependency(new AspireAndSekibanSampleDomainDependency());
builder.AddSekibanCosmosDb().AddSekibanCosmosAspire("SekibanAspireCosmos").AddSekibanBlobAspire("SekibanAspireBlob");
// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<AspireAndSekibanSampleDomainDependency>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());

//builder.AddKeyedAzureCosmosDbClient("SekibanAspireCosmos");
//builder.AddAzureCosmosDBClient("SekibanAspireCosmos");

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// Configure the HTTP request pipeline.
app.UseExceptionHandler();


app.MapDefaultEndpoints();


//app.MapGet("/test/getKeyedClient", async ([FromKeyedServices("SekibanAspireCosmos")] CosmosClient client) =>
//{
//    await client.CreateDatabaseIfNotExistsAsync("SekibanDb");
//    var containerResponse = await client.GetDatabase("SekibanDb").CreateContainerIfNotExistsAsync("SekibanContainer", "/id");
//    var container = client.GetContainer("SekibanDb", "SekibanContainer");
//    await container.CreateItemAsync(new TestModel("",3,  Guid.NewGuid().ToString()) );
//    // get all items
//    var items = new List<dynamic>();
//    var query = (IQueryable<TestModel>)container.GetItemLinqQueryable<TestModel>();
//    query = query.Where(x => x.id != "ddddeeee");
//    var iterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition());
//    while (iterator.HasMoreResults)
//    {
//        var response = await iterator.ReadNextAsync();
//        items.AddRange(response);
//    }

//    return items.Count;
//}).WithOpenApi();
//app.MapGet("/test/getClient", async ([FromServices] CosmosClient client) =>
//{
//    await client.CreateDatabaseIfNotExistsAsync("SekibanDb");
//    var containerResponse = await client.GetDatabase("SekibanDb").CreateContainerIfNotExistsAsync("SekibanContainer", "/id");
//var container = client.GetContainer("SekibanDb", "SekibanContainer");
//await container.CreateItemAsync(new { id = Guid.NewGuid().ToString() });
//// get all items
//var items = new List<dynamic>();
//var iterator = container.GetItemQueryIterator<dynamic>();
//while (iterator.HasMoreResults)
//{
//    var response = await iterator.ReadNextAsync();
//    items.AddRange(response);
//}
//return items.Count;
//}).WithOpenApi();

// need this to use sekiban.web
app.MapControllers();
app.Run();

public record TestModel(string Name, int Age, string id);
