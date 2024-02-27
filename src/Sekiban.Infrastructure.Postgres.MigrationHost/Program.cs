using Sekiban.Infrastructure.Postgres.Databases;
var builder = WebApplication.CreateBuilder(args);

// dotnet ef migrations add initial -p ../Sekiban.Infrastructure.Postgres



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddNpgsql<SekibanDbContext>(builder.Configuration.GetConnectionString("DefaultConnection"));
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Run();
