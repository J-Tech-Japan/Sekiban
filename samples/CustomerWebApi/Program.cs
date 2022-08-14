using CustomerDomainContext.Shared;
using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;
using Sekiban.EventSourcing.WebHelper.Common;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Add services to the container.
var controllerItems = new SekibanControllerItems(Dependency.GetAggregateTypes().ToList(), Dependency.GetDependencies().ToList());
builder.Services.AddSingleton<ISekibanControllerItems>(controllerItems);
var controllerOptions = new SekibanControllerOptions();
builder.Services.AddSingleton(controllerOptions);
#if true
builder.Services.AddControllers(options => options.Conventions.Add(new SekibanControllerRouteConvention(controllerOptions)))
    .ConfigureApplicationPartManager(m => m.FeatureProviders.Add(new SekibanControllerFeatureProvider(controllerItems, controllerOptions)));
#else
builder.Services.AddControllers();
#endif
// 全てのメソッドを許可する、Authorize不要
builder.Services.AddSingleton<IAuthorizeDefinitionCollection>(new AuthorizeDefinitionCollection(new Allow<AllMethod>()));

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// プロジェクトの依存
ESSampleProjectDependency.Dependency.Register(builder.Services);
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
