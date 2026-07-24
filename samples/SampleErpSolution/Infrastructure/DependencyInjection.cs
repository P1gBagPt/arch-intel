using Microsoft.Extensions.DependencyInjection;
using SampleErp.Domain;

namespace SampleErp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddHostedService<OrderSyncWorker>();
        services.AddScoped<EmailSender>();
        return services;
    }
}
