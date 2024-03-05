using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.AspireTest;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Aspire.Infrastructure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency(new FeatureCheckDependency());

// Sekiban Cosmos Setting
builder.AddSekibanCosmosDb().AddSekibanCosmosAspire("AspireCosmos").AddSekibanBlobAspire("AspireBlob");

// Sekiban Web Setting
builder.Services.AddSekibanWeb<FeatureCheckWebAspireDependency>().AddSwaggerGen(options => options.ConfigureForSekibanWeb());

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet(
    "test",
    async ([FromServices] IBlobContainerAccessor blobContainerAccessor) =>
    {
        var container = await blobContainerAccessor.GetContainerAsync("AspireBlob");
        var blob = container.GetBlobClient("test.txt");
        await blob.UploadAsync(new BinaryData("test"));
        return "test";
    });

app.MapControllers();
app.Run();
