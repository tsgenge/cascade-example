using Cascade.Example.BuildContext.Domain;
using Cascade.Example.BuildContext.Domain.Doors.Commands;
using Cascade.Example.BuildContext.Domain.Doors.Events;
using CascadeEsdm.WriteModel.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cascade.Example.Api.Extensions;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddCascadeEsdm(c =>
            c.WithInfrastructure(i =>
                i
                    .UsingCosmosDbStorage(s => s
                        .WithConnectionString(configuration.GetConnectionString("CosmosDb")!)
                        .WithDatabaseName("cascade")
                        .WithEventStreamContainer<EventStreamContainer>()
                    )
                    .UsingAzureTableStorage(s => s
                        .WithConnectionString(configuration.GetConnectionString("AzureStorage"))
                )
            )
            .WithWriteModel(w => 
                w
                .UsingExecutors(x => 
                    x.AddCommandsFromAssembly<AddDoor>())
                .UsingAppliers(x => 
                    x.AddEventAppliersFromAssembly<DoorAdded>()))
            );
        
        // Add application services here
        return services;
    }
}