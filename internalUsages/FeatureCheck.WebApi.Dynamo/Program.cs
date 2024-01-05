using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.Dynamo;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);


// Sekiban Web Setting
builder.Services.AddSekibanWeb(new FeatureCheckWebDependency());
// Sekiban Core Setting
builder.Services.AddSekibanWithDependency(new FeatureCheckDependency(), builder.Configuration);
// Sekiban Dynamo Setting
builder.Services.AddSekibanDynamoDB(builder.Configuration);

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.AddSekibanSwaggerGen());
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
