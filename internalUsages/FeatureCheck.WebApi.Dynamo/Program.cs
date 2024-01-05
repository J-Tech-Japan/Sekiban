using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.Dynamo;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency(new FeatureCheckDependency());
// Sekiban Dynamo Setting
builder.AddSekibanDynamoDB();

// Sekiban Web Setting
builder.Services.AddSekibanWeb<FeatureCheckWebDependency>()
    .AddSwaggerGen(options => options.ConfigureForSekiban());

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
