using Azure;
using Azure.Storage.Files.Shares;
using Azure.Storage.Files.Shares.Models;

namespace CoffeeNChill.Services;

public class StaffDocumentInfo
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset LastModified { get; set; }
}

public class StaffFileShareService
{
    private const string ShareName = "staff-docs";
    private readonly ShareClient _shareClient;

    public StaffFileShareService(string connectionString)
    {
        _shareClient = new ShareClient(connectionString, ShareName);
        _shareClient.CreateIfNotExists();
    }

    public async Task UploadFileAsync(string fileName, Stream content, long length, CancellationToken cancellationToken = default)
    {
        var rootDirectory = _shareClient.GetRootDirectoryClient();
        var fileClient = rootDirectory.GetFileClient(fileName);

        await fileClient.CreateAsync(length, cancellationToken: cancellationToken);
        await fileClient.UploadRangeAsync(new HttpRange(0, length), content, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<StaffDocumentInfo>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var files = new List<StaffDocumentInfo>();
        var rootDirectory = _shareClient.GetRootDirectoryClient();

        await foreach (var item in rootDirectory.GetFilesAndDirectoriesAsync(cancellationToken: cancellationToken))
        {
            if (item.IsDirectory)
                continue;

            var fileClient = rootDirectory.GetFileClient(item.Name);
            var properties = await fileClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            files.Add(new StaffDocumentInfo
            {
                FileName = item.Name,
                SizeBytes = properties.Value.ContentLength,
                LastModified = properties.Value.LastModified
            });
        }

        return files;
    }

    public async Task<ShareFileDownloadInfo?> DownloadFileAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var rootDirectory = _shareClient.GetRootDirectoryClient();
        var fileClient = rootDirectory.GetFileClient(fileName);

        if (!await fileClient.ExistsAsync(cancellationToken))
            return null;

        var download = await fileClient.DownloadAsync(cancellationToken: cancellationToken);
        return download.Value;
    }

    public async Task<bool> FileExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var rootDirectory = _shareClient.GetRootDirectoryClient();
        return await rootDirectory.GetFileClient(fileName).ExistsAsync(cancellationToken);
    }
}
