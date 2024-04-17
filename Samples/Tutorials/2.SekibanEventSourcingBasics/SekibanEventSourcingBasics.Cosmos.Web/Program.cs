using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi;
using Sekiban.Web.OpenApi.Extensions;
using SekibanEventSourcingBasics.Domain;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency<DomainDependency>();
// Sekiban Cosmos Setting
builder.AddSekibanCosmosDb();
// Sekiban Web Setting
builder.Services.AddSekibanWebFromDomainDependency<DomainDependency>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());

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
