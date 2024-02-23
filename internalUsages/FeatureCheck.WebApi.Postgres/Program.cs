using FeatureCheck.Domain.Shared;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency<FeatureCheckDependency>();

// Sekiban Postgres Setting
builder.AddSekibanPostgresDb();

// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<FeatureCheckDependency>(web => web.AllowAllIfLoggedIn());
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

app.MapControllers();
app.Run();
