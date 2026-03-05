using Dcb.Domain.WithoutResult;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Postgres;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSekibanDcbPostgresWithAspire();
builder.Services.AddSekibanDcbColdExport(
    builder.Configuration,
    builder.Environment.ContentRootPath);

var app = builder.Build();
app.Run();
