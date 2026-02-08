using CopilotUIWebAssembly;
using CopilotUIWebAssembly.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register application services
builder.Services.AddScoped<LocalStorageService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddSingleton<UIStateService>();
builder.Services.AddSingleton<ChatService>();
builder.Services.AddSingleton<ContextService>();

await builder.Build().RunAsync();
