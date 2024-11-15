using FeatureCheck.Domain.Shared;
using Scalar.AspNetCore;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// builder.Services.AddControllers();
builder.AddSekibanWithDependency<FeatureCheckDependency>();
builder.AddSekibanCosmosDb();
builder.AddSekibanWebFromDomainDependency<FeatureCheckDependency>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
