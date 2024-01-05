using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.Dynamo;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.Services.AddSekibanWithDependency(new FeatureCheckDependency(), builder.Configuration);

// Sekiban Dynamo Setting
builder.Services.AddSekibanDynamoDB(builder.Configuration);

// Sekiban Web Setting
builder.Services.AddSekibanWeb(new FeatureCheckWebDependency())
    .AddSwaggerGenWithSekibanOpenApiFilter();

builder.Services.AddEndpointsApiExplorer();

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
