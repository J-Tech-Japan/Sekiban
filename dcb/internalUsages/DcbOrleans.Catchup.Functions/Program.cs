var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddHttpClient();
builder.Services.AddHostedService<ColdExportTimerService>();

var app = builder.Build();
app.Run();
