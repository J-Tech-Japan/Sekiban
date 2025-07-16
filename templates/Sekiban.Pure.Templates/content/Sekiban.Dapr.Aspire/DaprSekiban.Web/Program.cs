using DaprSekiban.Web;
using DaprSekiban.Web.Components;
using DaprSekiban.Web.Services;
using System.Collections;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults for Aspire integration
builder.AddServiceDefaults();

// Add logging for service discovery debugging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HttpClient for API communication
builder.Services.AddHttpClient<WeatherApiClient>((serviceProvider, client) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<WeatherApiClient>>();
    var config = serviceProvider.GetRequiredService<IConfiguration>();
    
    // Log all service configuration for debugging
    logger.LogInformation("=== SERVICE DISCOVERY DEBUG ===");
    foreach (var kvp in config.AsEnumerable().Where(x => x.Key.Contains("services")))
    {
        logger.LogInformation("Config: {Key} = {Value}", kvp.Key, kvp.Value);
    }
    
    // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
    // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
    var baseUrl = "https+http://dapr-sekiban-api";
    logger.LogInformation("Setting HttpClient BaseAddress to: {BaseUrl}", baseUrl);
    client.BaseAddress = new(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.ConfigurePrimaryHttpMessageHandler((serviceProvider) =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<WeatherApiClient>>();
    var handler = new HttpClientHandler();
    
    // For debugging SSL issues in Container Apps
    handler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) =>
    {
        logger.LogWarning("SSL validation called for: {Sender}", sender);
        logger.LogWarning("Certificate subject: {Subject}", cert?.Subject);
        logger.LogWarning("SSL policy errors: {Errors}", sslPolicyErrors);
        return true; // Accept all certificates for now (not for production)
    };
    return handler;
});

var app = builder.Build();

// Log all environment variables for debugging service discovery
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=== ENVIRONMENT VARIABLES DEBUG ===");
foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables())
{
    if (envVar.Key.ToString().Contains("services", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("ENV: {Key} = {Value}", envVar.Key, envVar.Value);
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map default endpoints for Aspire integration
app.MapDefaultEndpoints();

app.Run();
