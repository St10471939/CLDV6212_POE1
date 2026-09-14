using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace CoffeeNChill.Services;

public class StaffDocumentInfo
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastModified { get; set; }
}

public class StaffFileShareService
{
    private const string ContainerName = "staff-docs";
    private readonly BlobContainerClient _containerClient;

    public StaffFileShareService(string connectionString)
    {
        _containerClient = new BlobContainerClient(connectionString, ContainerName);
        _containerClient.CreateIfNotExists();
    }

    public async Task UploadFileAsync(string fileName, Stream content, long length, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(fileName);
        await blobClient.UploadAsync(content, overwrite: true, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<StaffDocumentInfo>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var files = new List<StaffDocumentInfo>();

        await foreach (var blobItem in _containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            files.Add(new StaffDocumentInfo
            {
                FileName = blobItem.Name,
                SizeBytes = blobItem.Properties.ContentLength ?? 0,
                LastModified = blobItem.Properties.LastModified ?? DateTimeOffset.MinValue
            });
        }

        return files;
    }

    public async Task<BlobDownloadInfo?> DownloadFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(fileName);

        if (!await blobClient.ExistsAsync(cancellationToken))
            return null;

        var download = await blobClient.DownloadAsync(cancellationToken);
        return download.Value;
    }

    public async Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(fileName);
        return await blobClient.ExistsAsync(cancellationToken);
    }
}