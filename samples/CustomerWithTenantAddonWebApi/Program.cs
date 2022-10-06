using CosmosInfrastructure;
using CustomerWithTenantAddonDomainContext.Shared;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.WebHelper.Common;
using Sekiban.EventSourcing.WebHelper.SwashbuckleHelpers;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add services to the container.
var controllerItems = SekibanControllerItems.FromDependencies(new CustomerWithTenantAddonDependency());
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
builder.Services.AddSwaggerGen(
    config =>
    {
        config.CustomSchemaIds(x => x.FullName);
        config.SchemaFilter<NamespaceSchemaFilter>();
    });

// プロジェクトの依存
SekibanEventSourcingDependency.Register(builder.Services, new CustomerWithTenantAddonDependency());
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
