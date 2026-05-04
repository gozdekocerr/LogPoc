using Core.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ICustomerService, CustomerService>();
        return services;
    }
}
