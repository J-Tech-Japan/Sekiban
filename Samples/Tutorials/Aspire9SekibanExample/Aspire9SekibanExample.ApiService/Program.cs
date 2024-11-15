using SampleDomain;
using Scalar.AspNetCore;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// Microsoft.OpenApi
builder.Services.AddOpenApi();



builder.AddSekibanWithDependency<DomainDependency>();
builder.AddSekibanPostgresDbWithAzureBlobStorage();
builder.AddSekibanWebFromDomainDependency<DomainDependency>();
// Swashbuckle
// builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());
var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // swashbuckle
    // app.UseSwagger();
    // app.UseSwaggerUI();

    // Microsoft.OpenApi
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.DefaultFonts = false);
    // app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Aspire9SekibanExample.ApiService v1"));
}

string[] summaries =
    ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

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
    .WithName("GetWeatherForecast");

app.MapDefaultEndpoints();
app.MapControllers();
app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
