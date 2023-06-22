using MultiTenant.WebApi;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.SwashbuckleHelpers;
var builder = WebApplication.CreateBuilder(args);



// Sekiban Web Setting
builder.Services.AddSekibanWebAddon(new MultiTenantWebDependency());
// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new MultiTenantWebDependency(), configuration: builder.Configuration);
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


app.Run();
