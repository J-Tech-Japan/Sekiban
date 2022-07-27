using CosmosInfrastructure;
using MediatR;
using Sekiban.EventSourcing;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Shared;
using Sekiban.WebHelper.Common.Extensions;
using System.Reflection;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

#if false
builder.Services.AddControllers(options => options.Conventions.Add(new SekibanControllerRouteConvention()))
    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new SekibanControllerFeatureProvider(Dependency.GetDependencies())));
#else
builder.Services.AddControllers();
#endif
// // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// builder.Services.AddOpenApiDocument(); // nswag

// Sekiban
// MediatR
builder.Services.AddMediatR(Assembly.GetExecutingAssembly(), Dependency.GetAssembly());

// Sekibanイベントソーシング
builder.Services.AddSekibanCore();
builder.Services.AddSekibanCosmosDB();
builder.Services.AddSekibanHTTPUser();

builder.Services.AddSekibanSettingsFromAppSettings();

// 各ドメインコンテキスト
builder.Services.AddSingleton(new RegisteredEventTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Dependency.GetAssembly()));

builder.Services.AddSingleton(new SekibanAggregateTypes(CustomerDomainContext.Shared.Dependency.GetAssembly(), Dependency.GetAssembly()));

builder.Services.AddTransient(CustomerDomainContext.Shared.Dependency.GetDependencies());
builder.Services.AddTransient(Dependency.GetDependencies());

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
