using BookBorrowing.Domain;
using BookBorrowing.Web.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Web.Dependency;
var builder = WebApplication.CreateBuilder(args);




// Sekiban Core Setting
builder.Services.AddSekibanCoreWithDependency(new BookBorrowingDependency(), configuration: builder.Configuration);
// Sekiban Cosmos Setting
builder.Services.AddSekibanCosmosDB();
// Sekiban Web Setting
builder.Services.AddSekibanWeb(new BookBorrowingWebDependency());


// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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