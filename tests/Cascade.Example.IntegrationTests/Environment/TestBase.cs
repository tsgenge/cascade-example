using Xunit.Abstractions;

namespace Cascade.Example.IntegrationTests.Environment;

[Collection("IntegrationTests")]
public abstract class TestBase
{
    protected readonly ApiContext Environment;
    protected readonly ITestOutputHelper Output;
    protected readonly HttpClient Client;

    protected TestBase(ITestOutputHelper output, ApiContext environment)
    {
        Output = output;
        Environment = environment;
        Client = environment.Client;
    }
}
