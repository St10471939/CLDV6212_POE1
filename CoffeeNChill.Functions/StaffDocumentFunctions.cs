using Azure.Storage.Files.Shares;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace CoffeeNChill.Functions
{
    public class StaffDocumentFunctions
    {
        private readonly ILogger _logger;
        private const string ShareName = "staff-docs";

        public StaffDocumentFunctions(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<StaffDocumentFunctions>();
        }

        [Function("UploadStaffDocument")]
        public async Task<HttpResponseData> UploadStaffDocument(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
        {
            try
            {
                // 1. Get the content type boundary so we can parse the multipart body
                if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
                {
                    return await BadRequest(req, "Missing Content-Type header.");
                }

                var contentType = contentTypeValues.FirstOrDefault();

                if (string.IsNullOrEmpty(contentType))
                {
                    return await BadRequest(req, "Content-Type header was empty.");
                }
                var boundary = GetBoundary(contentType);

                if (string.IsNullOrEmpty(boundary))
                {
                    return await BadRequest(req, "Could not determine multipart boundary.");
                }

                // 2. Parse the multipart/form-data body
                var reader = new MultipartReader(boundary, req.Body);
                MultipartSection section;
                string fileName = null;
                byte[] fileBytes = null;

                while ((section = await reader.ReadNextSectionAsync()) != null)
                {
                    var contentDisposition = section.GetContentDispositionHeader();

                    if (contentDisposition != null && contentDisposition.IsFileDisposition())
                    {
                        fileName = contentDisposition.FileName.Value;

                        using var memoryStream = new MemoryStream();
                        await section.Body.CopyToAsync(memoryStream);
                        fileBytes = memoryStream.ToArray();
                    }
                }

                if (fileBytes == null || string.IsNullOrEmpty(fileName))
                {
                    return await BadRequest(req, "No file found in the request body.");
                }

                // 3. Connect to the real Azure File Share (NOT Azurite, since Azurite doesn't support Files)
                var connectionString = Environment.GetEnvironmentVariable("StaffDocsStorage");
                var shareClient = new ShareClient(connectionString, ShareName);
                await shareClient.CreateIfNotExistsAsync();

                var rootDirectory = shareClient.GetRootDirectoryClient();
                var fileClient = rootDirectory.GetFileClient(fileName);

                // 4. Upload the file, streaming it in
                using var uploadStream = new MemoryStream(fileBytes);
                await fileClient.CreateAsync(uploadStream.Length);
                await fileClient.UploadRangeAsync(
                    new Azure.HttpRange(0, uploadStream.Length),
                    uploadStream);

                _logger.LogInformation($"Uploaded file '{fileName}' ({fileBytes.Length} bytes) to staff-docs.");

                // 5. Return success
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    message = "File uploaded successfully",
                    fileName = fileName,
                    sizeBytes = fileBytes.Length
                }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Upload failed: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Upload failed: {ex.Message}");
                return errorResponse;
            }
        }
        [Function("ListStaffDocuments")]
        public async Task<HttpResponseData> ListStaffDocuments(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequestData req)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("StaffDocsStorage");
                var shareClient = new ShareClient(connectionString, ShareName);
                var rootDirectory = shareClient.GetRootDirectoryClient();

                var files = new List<object>();

                await foreach (var item in rootDirectory.GetFilesAndDirectoriesAsync())
                {
                    if (!item.IsDirectory)
                    {
                        var fileClient = rootDirectory.GetFileClient(item.Name);
                        var properties = await fileClient.GetPropertiesAsync();

                        files.Add(new
                        {
                            fileName = item.Name,
                            sizeBytes = properties.Value.ContentLength,
                            lastModified = properties.Value.LastModified
                        });
                    }
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonSerializer.Serialize(files));
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"List failed: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"List failed: {ex.Message}");
                return errorResponse;
            }
        }
        [Function("DownloadStaffDocument")]
        public async Task<HttpResponseData> DownloadStaffDocument(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/download/{fileName}")] HttpRequestData req,
    string fileName)
        {
            try
            {
                var connectionString = Environment.GetEnvironmentVariable("StaffDocsStorage");
                var shareClient = new ShareClient(connectionString, ShareName);
                var rootDirectory = shareClient.GetRootDirectoryClient();
                var fileClient = rootDirectory.GetFileClient(fileName);

                if (!await fileClient.ExistsAsync())
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteStringAsync($"File '{fileName}' was not found in staff-docs.");
                    return notFoundResponse;
                }

                var download = await fileClient.DownloadAsync();

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/octet-stream");
                response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

                await download.Value.Content.CopyToAsync(response.Body);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Download failed: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"Download failed: {ex.Message}");
                return errorResponse;
            }
        }
        private static string GetBoundary(string contentType)
        {
            var elements = contentType.Split(';');
            var boundaryElement = Array.Find(elements, e => e.Trim().StartsWith("boundary="));
            if (boundaryElement == null) return null;

            var boundary = boundaryElement.Trim().Substring("boundary=".Length).Trim('"');
            return boundary;
        }

        private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteStringAsync(message);
            return response;
        }
    }
}