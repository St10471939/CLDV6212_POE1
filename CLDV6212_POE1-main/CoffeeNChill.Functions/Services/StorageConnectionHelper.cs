namespace CoffeeNChill.Services;

/// <summary>
/// Resolves Azurite / Azure Storage connection strings for local dev and Docker.
/// When AzureWebJobsStorage is UseDevelopmentStorage=true and AZURITE_HOST is set
/// (e.g. host.docker.internal in Docker), expands to explicit endpoints.
/// </summary>
public static class StorageConnectionHelper
{
    private const string DevStoreAccountKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    public static string GetConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? "UseDevelopmentStorage=true";

        if (!string.Equals(configured, "UseDevelopmentStorage=true", StringComparison.OrdinalIgnoreCase))
            return configured;

        var azuriteHost = Environment.GetEnvironmentVariable("AZURITE_HOST");
        if (string.IsNullOrWhiteSpace(azuriteHost))
            return configured;

        return $"DefaultEndpointsProtocol=http;" +
               $"AccountName=devstoreaccount1;" +
               $"AccountKey={DevStoreAccountKey};" +
               $"BlobEndpoint=http://{azuriteHost}:10000/devstoreaccount1;" +
               $"QueueEndpoint=http://{azuriteHost}:10001/devstoreaccount1;" +
               $"TableEndpoint=http://{azuriteHost}:10002/devstoreaccount1;" +
               $"FileEndpoint=http://{azuriteHost}:10000/devstoreaccount1;";
    }
}
