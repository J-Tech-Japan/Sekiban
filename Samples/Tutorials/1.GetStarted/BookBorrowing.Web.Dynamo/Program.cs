using BookBorrowing.Domain;
using BookBorrowing.Web.Dynamo;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Dynamo;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new BookBorrowingDependency(), configuration: builder.Configuration);
// Sekiban Cosmos Setting
builder.Services.AddSekibanDynamoDB();
// Sekiban Web Setting
builder.Services.AddSekibanWeb(new BookBorrowingWebDependency());

// Add services to the container.
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
