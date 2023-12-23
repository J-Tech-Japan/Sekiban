using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi;
using SekibanEventSourcingBasics.Cosmos.Web;
using SekibanEventSourcingBasics.Domain;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new DomainDependency(), configuration: builder.Configuration);
// Sekiban Cosmos Setting
builder.Services.AddSekibanCosmosDB();
// Sekiban Web Setting
builder.Services.AddSekibanWeb(new SekibanWebDependency());

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
