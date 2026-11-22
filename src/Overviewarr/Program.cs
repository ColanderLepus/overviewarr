using Overviewarr;
using Overviewarr.Components;
using Overviewarr.Releases;
using Overviewarr.Search;
using Overviewarr.Settings;
using Overviewarr.Sonarr;
using Overviewarr.Trash;
using Radzen;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(AppPaths.FromConfiguration(builder.Configuration, builder.Environment.ContentRootPath));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<SonarrClient>();
builder.Services.AddSingleton(services => TrashDefinitions.Load(
    services.GetRequiredService<AppPaths>().TrashDirectory,
    services.GetRequiredService<ILogger<TrashDefinitions>>()));
builder.Services.AddSingleton<ReleaseNormalizer>();
builder.Services.AddSingleton<IReleaseSearcher, SonarrReleaseSearcher>();
builder.Services.AddSingleton<SearchQueue>();
builder.Services.AddSingleton<ISearchQueue>(services => services.GetRequiredService<SearchQueue>());
builder.Services.AddHostedService(services => services.GetRequiredService<SearchQueue>());
builder.Services.AddSingleton<SearchCache>();
builder.Services.AddHostedService<CacheEvictionService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();

var app = builder.Build();

if (!StartupChecks.Run(app))
{
    return 1;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
return 0;
