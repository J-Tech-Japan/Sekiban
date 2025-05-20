using SekibanDocumentMcpSse;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<SekibanDocumentTools>();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<SekibanDocumentService>();

var app = builder.Build();

app.MapMcp();

app.Run();