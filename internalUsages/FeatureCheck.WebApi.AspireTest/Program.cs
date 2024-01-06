using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.AspireTest;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Cosmos.Aspire;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency(new FeatureCheckDependency());

// Sekiban Cosmos Setting
builder.AddSekibanCosmosDB().AddSekibanCosmosAspire("AspireCosmos");

// Sekiban Web Setting
builder.Services.AddSekibanWeb<FeatureCheckWebAspireDependency>().AddSwaggerGen(options => options.ConfigureForSekiban());

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
