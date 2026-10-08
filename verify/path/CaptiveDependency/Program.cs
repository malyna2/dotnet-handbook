// Prove it: a singleton that takes a scoped service keeps ONE instance of it for every scope; ValidateScopes refuses it.
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddScoped<AppDb>()                    // one per request, like a DbContext
    .AddSingleton<PriceCache>();           // one per process, and it asks for an AppDb

using (var root = services.BuildServiceProvider())     // ValidateScopes = false: the default outside Development
{
    for (int request = 1; request <= 2; request++)
    {
        using var scope = root.CreateScope();          // ASP.NET Core opens one scope per request
        AppDb own = scope.ServiceProvider.GetRequiredService<AppDb>();
        AppDb captured = scope.ServiceProvider.GetRequiredService<PriceCache>().Db;
        Console.WriteLine($"request {request}: its own AppDb #{own.Id}, the singleton's AppDb #{captured.Id}");
    }
}

using (var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
using (var scope = root.CreateScope())
{
    try { scope.ServiceProvider.GetRequiredService<PriceCache>(); }
    catch (InvalidOperationException e) { Console.WriteLine($"ValidateScopes = true: {e.GetType().Name}: {e.Message}"); }
}

sealed class AppDb { private static int s_created; public int Id { get; } = ++s_created; }
sealed class PriceCache(AppDb db) { public AppDb Db { get; } = db; }
