using CosmosInfrastructure;
using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.WebHelper.Common;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add services to the container.
var controllerItems = new SekibanControllerItems(
    CustomerDependency.GetControllerAggregateTypes().ToList(),
    CustomerDependency.GetTransientDependencies().ToList(),
    CustomerDependency.GetSingleAggregateProjectionTypes().ToList(),
    CustomerDependency.GetMultipleAggregatesProjectionTypes().ToList(),
    CustomerDependency.GetAggregateListQueryFilterTypes().ToList(),
    CustomerDependency.GetSingleAggregateProjectionListQueryFilterTypes().ToList(),
    CustomerDependency.GetProjectionQueryFilterTypes().ToList(),
    CustomerDependency.GetProjectionListQueryFilterTypes().ToList());
builder.Services.AddSingleton<ISekibanControllerItems>(controllerItems);
var controllerOptions = new SekibanControllerOptions();
builder.Services.AddSingleton(controllerOptions);
#if true
builder.Services.AddControllers(
        options =>
        {
            options.Conventions.Add(new SekibanControllerRouteConvention(controllerOptions));
            options.ModelValidatorProviders.Clear();
        })
    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new SekibanControllerFeatureProvider(controllerItems, controllerOptions)));
#else
builder.Services.AddControllers();
#endif

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// プロジェクトの依存
SekibanEventSourcingDependency.Register(builder.Services, CustomerDependency.GetOptions());
builder.Services.AddSekibanCosmosDB();
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
