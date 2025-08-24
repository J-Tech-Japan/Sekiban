using SekibanDocumentMcpSse;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMcpServer().WithHttpTransport().WithTools<SekibanDocumentTools>();

// Add services
builder.Services.AddHttpClient();

// Add document configuration options
builder.Services.Configure<DocumentationOptions>(builder.Configuration.GetSection(DocumentationOptions.SectionName));
builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName));

// Add document services
builder.Services.AddSingleton<SekibanDocumentService>();
builder.Services.AddSingleton<AzureOpenAIService>();

var app = builder.Build();

app.MapMcp();

app.Run();
