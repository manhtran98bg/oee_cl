using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rostek.Gateway.Application.Oee;
using Rostek.Gateway.Application.Ports;
using Rostek.Gateway.Infrastructure.Oee;
using Rostek.Gateway.Infrastructure.Persistence;
using Rostek.Gateway.Infrastructure.Repositories;

namespace Rostek.Gateway.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, string configConnectionString, string oeeConnectionString)
    {
        services.AddDbContext<GatewayDbContext>(options => options.UseSqlite(configConnectionString));
        services.AddDbContext<OeeDbContext>(options => options.UseSqlite(oeeConnectionString));
        services.AddScoped<GatewayDbInitializer>();
        services.AddScoped<OeeDbInitializer>();
        services.AddScoped<IConfigRepository, EfCoreConfigRepository>();
        services.AddScoped<IOeeLocalRepository, EfCoreOeeLocalRepository>();
        return services;
    }
}
