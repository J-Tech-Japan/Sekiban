using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Sekiban.Infrastructure.IndexedDb;
using Sekiban.Net10.IndexedDbBrowserGate;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// This deliberately uses the non-generic facade: it is the production
// WebAssemblyHostBuilder route whose default runtime is BlazorJsRuntime.
builder.Services.AddSekibanIndexedDb(builder.Configuration);

await builder.Build().RunAsync();
