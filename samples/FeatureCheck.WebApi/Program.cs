using FeatureCheck.Domain.Shared;
using FeatureCheck.WebApi;
using Sekiban.Addon.Web.Dependency;
using Sekiban.Addon.Web.SwashbuckleHelpers;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Web Setting
builder.Services.AddSekibanWebAddon(new FeatureCheckWebDependency());
// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new FeatureCheckDependency(), configuration: builder.Configuration);
// Sekiban Cosmos Setting
builder.Services.AddSekibanCosmosDB();

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
