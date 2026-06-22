using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Cascade.Example.BuildContext.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.Azurite;
using Testcontainers.CosmosDb;
using Testcontainers.ServiceBus;

namespace Cascade.Example.IntegrationTests.Environment;

public class ApiContext : IAsyncLifetime
{
    private readonly AzuriteContainer _azuriteContainer;
    private readonly CosmosDbContainer _cosmosContainer;
    private readonly ServiceBusContainer _serviceBusContainer;

    private WebApplicationFactory<Program> _factory = default!;

    public ApiContext()
    {
        _azuriteContainer = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithInMemoryPersistence()
            .Build();

        _cosmosContainer = new CosmosDbBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest")
            .Build();

        _serviceBusContainer = new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:latest")
            .WithAcceptLicenseAgreement(true)
            .Build();
    }

    public HttpClient Client { get; private set; } = default!;
    public string AzuriteConnectionString { get; private set; } = default!;
    public string CosmosConnectionString { get; private set; } = default!;
    public string ServiceBusConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _azuriteContainer.StartAsync(),
            _cosmosContainer.StartAsync(),
            _serviceBusContainer.StartAsync());

        AzuriteConnectionString = _azuriteContainer.GetConnectionString();
        CosmosConnectionString = _cosmosContainer.GetConnectionString().Replace("http:", "https:");
        ServiceBusConnectionString = _serviceBusContainer.GetConnectionString();

        await SetupAzurite(AzuriteConnectionString);
        await SetupCosmos(CosmosConnectionString);
        
        System.Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        System.Environment.SetEnvironmentVariable("ConnectionStrings__CosmosDb", CosmosConnectionString);
        System.Environment.SetEnvironmentVariable("ConnectionStrings__AzureStorage", AzuriteConnectionString);
        System.Environment.SetEnvironmentVariable("ConnectionStrings__ServiceBus", ServiceBusConnectionString);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll(typeof(CosmosClientOptions));
                    services.AddSingleton(CreateEmulatorClientOptions());
                });
            });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _azuriteContainer.DisposeAsync();
        await _cosmosContainer.DisposeAsync();
        await _serviceBusContainer.DisposeAsync();
    }

    private async Task SetupCosmos(string connectionString)
    {
        using var client = new CosmosClient(connectionString, CreateEmulatorClientOptions());

        const int maxRetries = 10;
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var db = await client.CreateDatabaseIfNotExistsAsync("cascade");
                await db.Database.CreateContainerIfNotExistsAsync(
                    new ContainerProperties(new EventStreamContainer().Name, "/partitionKey"));
                return;
            }
            catch (HttpRequestException) when (i < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
            }
        }
    }

    private static CosmosClientOptions CreateEmulatorClientOptions()
    {
        return new CosmosClientOptions
        {
            HttpClientFactory = () =>
            {
                var innerHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
                return new HttpClient(innerHandler);
            },
            ConnectionMode = ConnectionMode.Gateway,
            LimitToEndpoint = true
        };
    }

    private static async Task SetupAzurite(string connectionString)
    {
        var blobServiceClient = new BlobServiceClient(connectionString);
        await blobServiceClient.CreateBlobContainerAsync("distributed-locks");
        var tabletServiceClient = new TableServiceClient(connectionString);
        await tabletServiceClient.CreateTableAsync("sequences");
    }
}
