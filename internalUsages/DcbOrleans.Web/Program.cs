using DcbOrleans.Web;
using DcbOrleans.Web.Components;
var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add HttpClient for API calls
builder.Services.AddHttpClient(
    "ApiService",
    client =>
    {
        client.BaseAddress = new Uri("https+http://apiservice");
    });

// Add WeatherApiClient
builder.Services.AddHttpClient<WeatherApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://apiservice");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
