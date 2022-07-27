using CosmosInfrastructure;
using CustomerDomainContext.Shared;
using MediatR;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.WebHelper.Common;
using Sekiban.WebHelper.Common.Extensions;
using System.Reflection;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var controllerItems = new SekibanControllerItems(Dependency.GetAggregateTypes().ToList(), Dependency.GetDependencies().ToList());
builder.Services.AddSingleton<ISekibanControllerItems>(controllerItems);
var controllerOptions = new SekibanControllerOptions();
builder.Services.AddSingleton(controllerOptions);
#if true
builder.Services.AddControllers(options => options.Conventions.Add(new SekibanControllerRouteConvention(controllerOptions)))
    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new SekibanControllerFeatureProvider(controllerItems)));
#else
builder.Services.AddControllers();
#endif
// // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// builder.Services.AddOpenApiDocument(); // nswag

// Sekiban
// MediatR
builder.Services.AddMediatR(Assembly.GetExecutingAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly());

// Sekibanイベントソーシング
builder.Services.AddSekibanCore();
builder.Services.AddSekibanCosmosDB();
builder.Services.AddSekibanHTTPUser();

builder.Services.AddSekibanSettingsFromAppSettings();

// 各ドメインコンテキスト
builder.Services.AddSingleton(new RegisteredEventTypes(Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

builder.Services.AddSingleton(new SekibanAggregateTypes(Dependency.GetAssembly(), Sekiban.EventSourcing.Shared.Dependency.GetAssembly()));

builder.Services.AddTransient(Dependency.GetDependencies());
builder.Services.AddTransient(Sekiban.EventSourcing.Shared.Dependency.GetDependencies());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // app.UseOpenApi();// nswag
    // app.UseSwaggerUi3(); // serve Swagger UI// nswag
    // app.UseReDoc();// nswag
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
