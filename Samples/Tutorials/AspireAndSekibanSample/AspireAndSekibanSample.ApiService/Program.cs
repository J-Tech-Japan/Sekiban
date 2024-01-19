using AspireAndSekibanSample.ApiService;
using AspireAndSekibanSample.Domain;
using Sekiban.Aspire.Infrastructure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddSekibanWithDependency(new AspireAndSekibanSampleDomainDependency());
builder.AddSekibanCosmosDB().AddSekibanCosmosAspire("SekibanAspireCosmos").AddSekibanBlobAspire("SekibanAspireBlob");

// Sekiban Web Setting
builder.Services.AddSekibanWeb<AspireAndSekibanSampleWebDependency>()
    .AddSwaggerGen(options => options.ConfigureForSekibanWeb());

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// Configure the HTTP request pipeline.
app.UseExceptionHandler();


app.MapDefaultEndpoints();

// need this to use sekiban.web
app.MapControllers();
app.Run();

