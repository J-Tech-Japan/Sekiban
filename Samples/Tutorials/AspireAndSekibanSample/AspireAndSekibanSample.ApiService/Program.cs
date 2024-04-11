using AspireAndSekibanSample.Domain;
using Microsoft.Azure.Cosmos;
using Sekiban.Aspire.Infrastructure.Cosmos;
using Sekiban.Core.Dependency;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Infrastructure.Postgres;
using Sekiban.Web.Dependency;
using Sekiban.Web.OpenApi.Extensions;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

builder.AddSekibanWithDependency(new AspireAndSekibanSampleDomainDependency());

builder.AddSekibanCosmosDb(
//    optionsFunc: options => {
//    var o = new SekibanCosmosClientOptions()
//    {
//        ClientOptions = new Microsoft.Azure.Cosmos.CosmosClientOptions()
//        {
//            HttpClientFactory = () =>
//            {
//                HttpMessageHandler httpMessageHandler = new HttpClientHandler()
//                {
//                    ServerCertificateCustomValidationCallback = (req, cert, chain, errors) => true
//                };
//                return new HttpClient(httpMessageHandler);
//            },
//            ConnectionMode = ConnectionMode.Gateway,
//            LimitToEndpoint = true
//        }
//    };
//    return options;
//}
).AddSekibanCosmosAspire("SekibanAspireCosmos").AddSekibanBlobAspire("SekibanAspireBlob");

// Sekiban Web Setting
builder.AddSekibanWebFromDomainDependency<AspireAndSekibanSampleDomainDependency>();
builder.Services.AddSwaggerGen(options => options.ConfigureForSekibanWeb());

var app = builder.Build();


if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


// Configure the HTTP request pipeline.
app.UseExceptionHandler();


app.MapDefaultEndpoints();

// need this to use sekiban.web
app.MapControllers();
app.Run();

