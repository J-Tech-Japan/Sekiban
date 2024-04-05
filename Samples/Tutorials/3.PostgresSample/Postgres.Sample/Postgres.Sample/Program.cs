using Postgres.Sample.Domain;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.AddSekibanWithDependency<DomainDependency>();
builder.AddSekibanPostgresDbWithAzureBlobStorage();
builder.AddSekibanWebFromDomainDependency<DomainDependency>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(configure => configure.ConfigureForSekibanWeb());

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
