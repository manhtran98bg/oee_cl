using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rostek.Gateway.Application.History;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Infrastructure.History;
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
        services.AddSingleton<IDeviceSampleWriter, PostgresDeviceSampleWriter>();
        return services;
    }
}
