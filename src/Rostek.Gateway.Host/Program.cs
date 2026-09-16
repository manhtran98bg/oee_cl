using Rostek.Gateway.Application.Configurations;
using Rostek.Gateway.Application.Dashboard;
using Rostek.Gateway.Application.MachineGroups;
using Rostek.Gateway.Application.Machines;
using Rostek.Gateway.Application.MachineTemplates;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Contracts.Configuration;
using Rostek.Gateway.Contracts.Runtime;
using Rostek.Gateway.Host.BackgroundServices;
using Rostek.Gateway.Host.Configuration;
using Rostek.Gateway.Host.Options;
using Rostek.Gateway.Infrastructure;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Runtime;
using ConfigurationBuilderPort = Rostek.Gateway.Application.Configurations.IConfigurationBuilder;
using GatewayConfigurationBuilder = Rostek.Gateway.Application.Configurations.ConfigurationBuilder;

var externalAppSettings = ExternalAppSettings.Ensure();
var builder = WebApplication.CreateBuilder(args);
builder.Configuration
    .AddJsonFile(externalAppSettings.ConfigPath, optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection("Gateway"));
builder.Services.PostConfigure<GatewayOptions>(options =>
{
    options.DataDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, options.DataDirectory, "data");
    options.BackupDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, options.BackupDirectory, "backups");
    options.ExportDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, options.ExportDirectory, "exports");
});
builder.Services.Configure<RuntimeOptions>(builder.Configuration.GetSection("Runtime"));
builder.Services.Configure<MesSyncOptions>(builder.Configuration.GetSection("MesSync"));
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery();
builder.Services.AddHealthChecks();

builder.Services.AddScoped<IMachineGroupService, MachineGroupService>();
builder.Services.AddScoped<IMachineTemplateService, MachineTemplateService>();
builder.Services.AddScoped<IMachineService, MachineService>();
builder.Services.AddScoped<ISignalOverrideService, SignalOverrideService>();
builder.Services.AddScoped<ConfigurationBuilderPort, GatewayConfigurationBuilder>();
builder.Services.AddScoped<IConfigurationValidator, ConfigurationValidator>();
builder.Services.AddScoped<IConfigurationApplyService, ConfigurationApplyService>();
builder.Services.AddScoped<IConfigurationVersionService, ConfigurationVersionService>();
builder.Services.AddScoped<IConfigurationImportExportService, ConfigurationImportExportService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRawDataCaptureService, RawDataCaptureService>();
builder.Services.AddSingleton<IGatewayOeeTimerService, GatewayOeeTimerService>();
builder.Services.AddScoped<IRealtimeSnapshotBuilder, RealtimeSnapshotBuilder>();
builder.Services.AddScoped<IMachineStateEventBuilder, MachineStateEventBuilder>();
builder.Services.AddScoped<IProductionMetricBuilder, ProductionMetricBuilder>();
builder.Services.AddScoped<IOeeLocalProcessingService, OeeLocalProcessingService>();
builder.Services.AddScoped<IRealtimeSnapshotSyncService, RealtimeSnapshotSyncService>();
builder.Services.AddScoped<ISyncOutboxDispatcher, MesSyncOutboxDispatcher>();
builder.Services.AddSingleton<IProductionContextCache, ProductionContextCache>();
builder.Services.AddSingleton<IRealtimeSnapshotSyncStatusStore, RealtimeSnapshotSyncStatusStore>();
builder.Services.AddScoped<IProductionCommandService, ProductionCommandService>();
builder.Services.AddScoped<IMesEquipmentCatalogService, MesEquipmentCatalogService>();
builder.Services.AddHostedService<MesSyncHostedService>();

var gatewayOptions = builder.Configuration.GetSection("Gateway").Get<GatewayOptions>() ?? new GatewayOptions();
gatewayOptions.DataDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, gatewayOptions.DataDirectory, "data");
gatewayOptions.BackupDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, gatewayOptions.BackupDirectory, "backups");
gatewayOptions.ExportDirectory = ResolveGatewayPath(externalAppSettings.GatewayHomePath, gatewayOptions.ExportDirectory, "exports");
Directory.CreateDirectory(gatewayOptions.DataDirectory);
Directory.CreateDirectory(gatewayOptions.BackupDirectory);
Directory.CreateDirectory(gatewayOptions.ExportDirectory);

var configDatabasePath = Path.Combine(gatewayOptions.DataDirectory, SanitizeDatabaseFileName(gatewayOptions.ConfigDatabaseFileName, "config.db"));
var oeeDatabasePath = Path.Combine(gatewayOptions.DataDirectory, SanitizeDatabaseFileName(gatewayOptions.OeeDatabaseFileName, "oee.db"));
builder.Services.AddGatewayInfrastructure($"Data Source={configDatabasePath}", $"Data Source={oeeDatabasePath}");
builder.Services.AddGatewayRuntime();

var app = builder.Build();
var startupLogger = app.Logger;
startupLogger.LogInformation(
    "Starting Rostek Gateway {GatewayId}. DataDirectory={DataDirectory}, ConfigDatabasePath={ConfigDatabasePath}, OeeDatabasePath={OeeDatabasePath}, BackupDirectory={BackupDirectory}, ExportDirectory={ExportDirectory}, GatewayHomePath={GatewayHomePath}, ExternalAppSettingsPath={ExternalAppSettingsPath}, ExternalAppSettingsCreated={ExternalAppSettingsCreated}",
    gatewayOptions.GatewayId,
    gatewayOptions.DataDirectory,
    configDatabasePath,
    oeeDatabasePath,
    gatewayOptions.BackupDirectory,
    gatewayOptions.ExportDirectory,
    externalAppSettings.GatewayHomePath,
    externalAppSettings.ConfigPath,
    externalAppSettings.Created);

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<GatewayDbInitializer>();
    startupLogger.LogInformation("Initializing configuration database at {DatabasePath}", configDatabasePath);
    await initializer.InitializeAsync(CancellationToken.None);
    startupLogger.LogInformation("Configuration database initialized");

    var oeeInitializer = scope.ServiceProvider.GetRequiredService<OeeDbInitializer>();
    startupLogger.LogInformation("Initializing OEE database at {DatabasePath}", oeeDatabasePath);
    await oeeInitializer.InitializeAsync(CancellationToken.None);
    startupLogger.LogInformation("OEE database initialized");

    var oeeRepository = scope.ServiceProvider.GetRequiredService<IOeeLocalRepository>();
    var productionContextCache = scope.ServiceProvider.GetRequiredService<IProductionContextCache>();
    var productionContexts = await oeeRepository.ListProductionContextsAsync(CancellationToken.None);
    productionContextCache.Replace(productionContexts);
    startupLogger.LogInformation("Loaded {ProductionContextCount} production contexts into OEE memory store", productionContexts.Count);
    foreach (var context in productionContexts.OrderBy(context => context.Machine, StringComparer.OrdinalIgnoreCase).ThenBy(context => context.OrderId, StringComparer.OrdinalIgnoreCase))
    {
        startupLogger.LogInformation(
            "Loaded production context. Machine={Machine}, Status={Status}, OrderId={OrderId}, SessionId={SessionId}, ActivePeriodStartAt={ActivePeriodStartAt}, PlcPeriodIndex={PlcPeriodIndex}, BaselineRawId={BaselineRawId}, BaselineCapturedAt={BaselineCapturedAt}, ProductsJson={ProductsJson}",
            context.Machine,
            context.Status,
            context.OrderId,
            context.SessionId,
            context.ActivePeriodStartAt,
            context.CurrentPlcPeriodIndex,
            context.BaselineRawId,
            context.BaselineCapturedAt,
            context.ProductsJson);
    }

    var versionService = scope.ServiceProvider.GetRequiredService<IConfigurationVersionService>();
    var active = await versionService.GetActiveAsync(CancellationToken.None);
    if (active is not null)
    {
        startupLogger.LogInformation("Loading active configuration version {Version} with {MachineCount} machines", active.Version, active.Machines.Count);
        var runtimeProvider = scope.ServiceProvider.GetRequiredService<IRuntimeConfigurationProvider>();
        var runtimeManager = scope.ServiceProvider.GetRequiredService<IMachineRuntimeManager>();
        await runtimeProvider.ReplaceAsync(active, CancellationToken.None);
        await runtimeManager.ApplyConfigurationAsync(RuntimeConfiguration.Empty, active, CancellationToken.None);
        startupLogger.LogInformation("Active configuration version {Version} loaded into runtime", active.Version);
    }
    else
    {
        startupLogger.LogInformation("No active configuration found. Runtime starts empty");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

app.MapGet("/health/live", () => Results.Ok(new { status = "OK" }));
app.MapGet("/health/ready", (IRuntimeConfigurationProvider provider) => Results.Ok(new
{
    status = "OK",
    activeVersion = provider.Current.Version,
    runtime = provider.Current.Version == 0 ? "empty runtime" : "active"
}));

app.MapGet("/api/v1/status", (IRuntimeConfigurationProvider provider, IRuntimeStatusReader statusReader) => Results.Ok(new
{
    activeVersion = provider.Current.Version,
    machineCount = provider.Current.Machines.Count,
    statuses = statusReader.GetStatuses()
}));

app.MapGet("/api/v1/dashboard", async (IDashboardService dashboardService, CancellationToken cancellationToken) =>
    Results.Ok(await dashboardService.GetAsync(cancellationToken)));

app.MapGet("/api/v1/machines", async (IMachineService machines, CancellationToken cancellationToken) =>
    Results.Ok(await machines.ListAsync(new(null, null, null, null, null, 1, 100), cancellationToken)));

app.MapGet("/api/v1/machines/{machineCode}", async (string machineCode, ConfigurationBuilderPort configurationBuilder, CancellationToken cancellationToken) =>
{
    var draft = await configurationBuilder.BuildDraftAsync(cancellationToken);
    return draft.Machines.TryGetValue(machineCode, out var machine) ? Results.Ok(machine) : Results.NotFound();
});

app.MapGet("/api/v1/machines/{machineCode}/runtime-status", (string machineCode, IRuntimeStatusReader statusReader) =>
    statusReader.GetStatus(machineCode) is { } status ? Results.Ok(status) : Results.NotFound());

app.MapGet("/api/v1/machines/{machineCode}/values", (string machineCode, IMachineValueReader valueReader) =>
    valueReader.GetSnapshot(machineCode) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapGet("/api/v1/mes/equipment/molding-machines", async (IMesEquipmentCatalogService catalogService, CancellationToken cancellationToken) =>
{
    var result = await catalogService.FetchMoldingMachinesAsync(cancellationToken);
    return result.Succeeded ? Results.Ok(new { items = result.Value }) : Results.Problem(result.ErrorMessage, statusCode: StatusCodes.Status502BadGateway);
});

app.MapPost("/api/v1/gateway/oee/production-commands", async (ProductionCommandBatchRequest request, IProductionCommandService commandService, CancellationToken cancellationToken) =>
{
    if (request.SchemaVersion != 1 || request.Items is not { Count: > 0 })
    {
        return Results.BadRequest(new
        {
            accepted = false,
            schema_version = request.SchemaVersion,
            gateway_id = request.GatewayId,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            accepted_count = 0,
            rejected_count = request.Items?.Count ?? 0,
            items = Array.Empty<object>(),
            message = "schema_version must be 1 and items is required."
        });
    }

    return Results.Ok(await commandService.HandleBatchAsync(request, cancellationToken));
});

app.MapPost("/api/v1/mes/production-commands", async (ProductionCommandRequest request, IProductionCommandService commandService, CancellationToken cancellationToken) =>
{
    var response = await commandService.HandleAsync(request, cancellationToken);
    return response.Accepted ? Results.Ok(response) : Results.BadRequest(response);
});

app.MapGet("/api/v1/mes-sync/status", (IRealtimeSnapshotSyncStatusStore statusStore, Microsoft.Extensions.Options.IOptions<MesSyncOptions> options) =>
{
    var status = statusStore.Current;
    return Results.Ok(new MesSyncStatusDto(
        options.Value.Enabled,
        options.Value.RealtimeSnapshotsEnabled,
        options.Value.MachineStateEventsEnabled,
        options.Value.ProductionMetricsEnabled,
        status.LastSuccessUnixTimeSeconds,
        status.LastError,
        status.LastItemCount,
        status.DroppedBatchCount));
});

app.MapPost("/api/v1/configuration/validate", async (ConfigurationBuilderPort builderService, IConfigurationValidator validator, CancellationToken cancellationToken) =>
    Results.Ok(await validator.ValidateAsync(await builderService.BuildDraftAsync(cancellationToken), cancellationToken)));

app.MapPost("/api/v1/configuration/apply", async (IConfigurationApplyService applyService, CancellationToken cancellationToken) =>
    Results.Ok(await applyService.ApplyDraftAsync(null, null, cancellationToken)));

app.MapGet("/api/v1/configuration/active", async (IConfigurationVersionService versionService, CancellationToken cancellationToken) =>
    await versionService.GetActiveAsync(cancellationToken) is { } active ? Results.Ok(active) : Results.NotFound());

app.MapGet("/api/v1/configuration/versions", async (IConfigurationVersionService versionService, CancellationToken cancellationToken) =>
    Results.Ok(await versionService.ListAsync(cancellationToken)));

app.MapGet("/api/v1/configuration/versions/{version:long}", async (long version, IConfigurationVersionService versionService, CancellationToken cancellationToken) =>
    await versionService.GetVersionAsync(version, cancellationToken) is { } snapshot ? Results.Ok(snapshot) : Results.NotFound());

app.MapPost("/api/v1/configuration/versions/{version:long}/rollback", async (long version, IConfigurationApplyService applyService, CancellationToken cancellationToken) =>
    Results.Ok(await applyService.RollbackAsync(version, null, null, cancellationToken)));

app.MapGet("/api/v1/configuration/export", async (IConfigurationImportExportService importExport, CancellationToken cancellationToken) =>
    Results.Text(await importExport.ExportActiveJsonAsync(cancellationToken), "application/json"));

app.Run();

static string ResolveGatewayPath(string gatewayHomePath, string? configuredPath, string defaultLeaf)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (string.IsNullOrWhiteSpace(home))
    {
        home = Environment.GetEnvironmentVariable("HOME") ?? AppContext.BaseDirectory;
    }

    var path = string.IsNullOrWhiteSpace(configuredPath) ? defaultLeaf : configuredPath.Trim();
    if (path == "~")
    {
        return home;
    }

    if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
    {
        return Path.Combine(home, path[2..]);
    }

    if (Path.IsPathRooted(path))
    {
        return path;
    }

    return Path.Combine(gatewayHomePath, path);
}

static string SanitizeDatabaseFileName(string? configuredFileName, string defaultFileName)
{
    if (string.IsNullOrWhiteSpace(configuredFileName))
    {
        return defaultFileName;
    }

    var fileName = Path.GetFileName(configuredFileName);
    return string.IsNullOrWhiteSpace(fileName) ? defaultFileName : fileName;
}

public partial class Program;
