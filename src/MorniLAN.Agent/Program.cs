using Microsoft.Extensions.Options;
using MorniLAN.Agent;
using MorniLAN.Agent.Connection;
using MorniLAN.Agent.Platform;
using MorniLAN.Shared;
using Serilog;
using Serilog.Events;

// Nur ein Agent pro PC: zwei Instanzen teilen sich Zertifikat und Pin und stören sich beim Pairing.
using var singleInstance = new Mutex(initiallyOwned: false, @"Global\MorniLAN.Agent");
try
{
    if (!singleInstance.WaitOne(TimeSpan.Zero))
    {
        Console.Error.WriteLine("MorniLAN Agent läuft auf diesem PC bereits. Dieses Fenster kann geschlossen werden.");
        return 1;
    }
}
catch (AbandonedMutexException)
{
    // Vorige Instanz ist abgestürzt, der Mutex gehört jetzt uns.
}

Directory.CreateDirectory(AgentPaths.Logs);

var builder = Host.CreateApplicationBuilder(args);

// Einstellungen des Installers (z. B. Adresse des Admin-PCs) liegen außerhalb des Programmordners,
// damit sie Updates überstehen. Befehlszeilenargumente haben weiterhin Vorrang.
builder.Configuration.AddJsonFile(AgentPaths.SettingsFile, optional: true, reloadOnChange: false);
builder.Configuration.AddCommandLine(args);

builder.Services.AddWindowsService(options => options.ServiceName = MorniLanConstants.AgentServiceName);

builder.Services.AddSerilog((_, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(AgentPaths.Logs, "agent-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

builder.Services.Configure<AgentConnectionOptions>(builder.Configuration.GetSection(AgentConnectionOptions.Section));
builder.Services.AddSingleton(sp =>
{
    var dataDirectory = sp.GetRequiredService<IOptions<AgentConnectionOptions>>().Value.DataDirectory;
    AgentPaths.EnsureDataDirectory(dataDirectory);
    return new AgentStateStore(dataDirectory);
});
builder.Services.AddSingleton(sp => AgentIdentity.LoadOrCreate(
    sp.GetRequiredService<IOptions<AgentConnectionOptions>>().Value.DataDirectory,
    sp.GetRequiredService<ILogger<AgentIdentity>>()));
builder.Services.AddSingleton<SystemStatusCollector>();
builder.Services.AddSingleton<DiscoveryListener>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DiscoveryListener>());

builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddSingleton<AdminConnectionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AdminConnectionService>());
builder.Services.AddHostedService<LocalStatusServer>();

var host = builder.Build();
host.Run();
return 0;
