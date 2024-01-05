using MultiTenant.WebApi;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency(new MultiTenantWebDependency());
// Sekiban Cosmos Setting
builder.AddSekibanCosmosDB();

// Sekiban Web Setting
builder.Services.AddSekibanWeb<MultiTenantWebDependency>()
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
