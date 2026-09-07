using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rostek.Gateway.Application.MesSync;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Infrastructure.MesSync;
using Rostek.Gateway.Infrastructure.Oee;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;

namespace Rostek.Gateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<GatewayDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<GatewayDbInitializer>();
        services.AddScoped<IConfigRepository, EfCoreConfigRepository>();
        services.AddScoped<IMesSyncOutboxRepository, EfCoreMesSyncOutboxRepository>();
        services.AddScoped<IOeeLocalRepository, EfCoreOeeRawIntervalRepository>();
        services.AddHttpClient();
        services.AddScoped<IMesServerClient, MesServerClient>();
        return services;
    }
}
