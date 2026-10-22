namespace DotnetScope.IntegrationTests;

public sealed class LocalMetadataServerTests
{
    [Fact]
    public async Task DisposeWhileWaitingForRequest_Completes()
    {
        var server = new LocalMetadataServer();
        await server.AcceptPending;

        server.Dispose();
    }
}
