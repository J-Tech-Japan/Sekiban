using MemStat.Net;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddTransient<IMemoryUsageFinder, MemoryUsageFinder>();

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
        .Remap(_ => (MemoryUsageFinder)memoryUsageFinder)
        .Conveyor(
            finder => finder.LinuxMemoryInfo.Match(
                value => value.ToResultBox(),
                () => new InvalidOperationException("LinuxMemoryInfo is not set.")))
        .UnwrapBox());
app.MapGet(
    "/memoryusage3",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Remap(_ => (MemoryUsageFinder)memoryUsageFinder)
        .Conveyor(
            finder => finder.MacVmStat.Match(
                value => value.ToResultBox(),
                () => new InvalidOperationException("LinuxMemoryInfo is not set.")))
        .UnwrapBox());
app.MapGet(
    "/memoryusage4",
    ([FromServices] IMemoryUsageFinder memoryUsageFinder) => memoryUsageFinder
        .ReceiveCurrentMemoryUsage()
        .Remap(_ => (MemoryUsageFinder)memoryUsageFinder)
        .Conveyor(
            finder => finder.WindowsComputerInfo.Match(
                value => value.ToResultBox(),
                () => new InvalidOperationException("Windows MemoryInfo is not set.")))
        .UnwrapBox());

app.Run();

internal record MemoryInfo(double TotalMemory, double MemoryUsagePercentage);

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
