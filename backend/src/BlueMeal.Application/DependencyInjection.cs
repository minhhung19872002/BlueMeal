using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMeal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        // Use-case services are discovered by convention: public, non-abstract classes named *Service in Application.Services.
        foreach (var t in typeof(DependencyInjection).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && t.Namespace?.StartsWith("BlueMeal.Application.Services") == true && t.Name.EndsWith("Service")))
            services.AddScoped(t);
        return services;
    }
}
