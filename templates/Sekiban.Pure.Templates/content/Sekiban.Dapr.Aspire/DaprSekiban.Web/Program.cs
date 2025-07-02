using DaprSekiban.Web;
using DaprSekiban.Web.Components;
using DaprSekiban.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults for Aspire integration
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add HttpClient for API communication
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    // The service name will be resolved by Aspire service discovery
    client.BaseAddress = new Uri("http://dapr-sekiban-api");
});

var app = builder.Build();

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
