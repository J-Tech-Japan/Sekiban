using MemStat.Net;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryUsageFinder();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app
    .MapGet(
        "/weatherforecast",
        () =>
        {
            var forecast = Enumerable
                .Range(1, 5)
                .Select(
                    index => new WeatherForecast(
                        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                        Random.Shared.Next(-20, 55),
                        summaries[Random.Shared.Next(summaries.Length)]))
                .ToArray();
            return forecast;
        })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

app.MapGet(
    "/memoryusage",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Conveyor(_ => memoryUsageFinder.GetTotalMemoryUsage())
        .Combine(_ => memoryUsageFinder.GetMemoryUsagePercentage())
        .Remap((total, percent) => new MemoryInfo(total, percent))
        .Match(some => Results.Ok(some), error => Results.Ok(error.Message)));

app.MapGet(
    "/memoryusage2",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Conveyor(memoryUsageFinder.GetRawMemoryUsageObject)
        .UnwrapBox());

app.Run();
