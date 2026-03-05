using DcbOrleans.Catchup.Functions;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddHostedService<ColdExportTimerService>();

var app = builder.Build();
app.Run();
