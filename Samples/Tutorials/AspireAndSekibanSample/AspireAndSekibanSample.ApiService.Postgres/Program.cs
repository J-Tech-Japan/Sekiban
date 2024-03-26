using AspireAndSekibanSample.Domain;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddSekibanWithDependency<AspireAndSekibanSampleDomainDependency>();

builder.AddSekibanPostgresDbOnlyFromConnectionStringName("SekibanAspirePostgres");
builder.AddSekibanAzureBlobStorage();

// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<AspireAndSekibanSampleDomainDependency>();
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());

var app = builder.Build();

// Configure the HTTP request pipeline.
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