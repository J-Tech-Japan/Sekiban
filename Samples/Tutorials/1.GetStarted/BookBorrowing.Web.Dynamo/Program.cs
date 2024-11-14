using BookBorrowing.Domain;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.AddSekibanWithDependency<BookBorrowingDependency>();
// Sekiban Cosmos Setting
builder.AddSekibanDynamoDb();
// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<BookBorrowingDependency>();

// Add services to the container.
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
app.MapControllers();
app.Run();
