using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi.Dynamo;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.SwashbuckleHelpers;
var builder = WebApplication.CreateBuilder(args);


// Sekiban Web Setting
builder.Services.AddSekibanWebAddon(new FeatureCheckWebDependency());
// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new FeatureCheckDependency(), configuration: builder.Configuration);
// Sekiban Dynamo Setting
builder.Services.AddSekibanDynamoDB();

builder.Services.AddControllers();
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
