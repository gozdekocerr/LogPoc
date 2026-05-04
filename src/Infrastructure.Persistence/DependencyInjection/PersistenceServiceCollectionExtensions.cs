using Core.Application.Repositories;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddSingleton<ICustomerRepository,  InMemoryCustomerRepository>();
        return services;
    }
}
