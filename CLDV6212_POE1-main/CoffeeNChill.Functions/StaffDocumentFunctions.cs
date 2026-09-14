using System.Net;
using System.Text.Json;
using CoffeeNChill.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace CoffeeNChill.Functions;

public class StaffDocumentFunctions
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".txt", ".md"
    };

    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown"
    };

    private readonly StaffFileShareService _fileShareService;
    private readonly ILogger<StaffDocumentFunctions> _logger;

    public StaffDocumentFunctions(StaffFileShareService fileShareService, ILogger<StaffDocumentFunctions> logger)
    {
        _fileShareService = fileShareService;
        _logger = logger;
    }

    [Function("UploadStaffDocument")]
    public async Task<HttpResponseData> UploadStaffDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequestData req)
    {
        try
        {
            if (!req.Headers.TryGetValues("Content-Type", out var contentTypeValues))
                return await BadRequest(req, "Missing Content-Type header.");

            var contentType = contentTypeValues.FirstOrDefault();
            if (string.IsNullOrEmpty(contentType))
                return await BadRequest(req, "Content-Type header was empty.");

            var boundary = GetBoundary(contentType);
            if (string.IsNullOrEmpty(boundary))
                return await BadRequest(req, "Could not determine multipart boundary.");

            var reader = new MultipartReader(boundary, req.Body);
            MultipartSection? section;
            string? fileName = null;
            byte[]? fileBytes = null;

            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                var contentDisposition = section.GetContentDispositionHeader();
                if (contentDisposition == null || !contentDisposition.IsFileDisposition())
                    continue;

                fileName = contentDisposition.FileName.Value?.Trim('"');
                using var memoryStream = new MemoryStream();
                await section.Body.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            if (fileBytes == null || string.IsNullOrWhiteSpace(fileName))
                return await BadRequest(req, "No file found in the request body.");

            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return await BadRequest(req,
                    $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", AllowedExtensions)}.");
            }

            using var uploadStream = new MemoryStream(fileBytes);
            await _fileShareService.UploadFileAsync(fileName, uploadStream, fileBytes.Length);

            _logger.LogInformation("Uploaded file '{FileName}' ({Size} bytes) to staff-docs.", fileName, fileBytes.Length);

            var response = req.CreateResponse(HttpStatusCode.Created);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                message = "File uploaded successfully",
                fileName,
                sizeBytes = fileBytes.Length
            }));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed");
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
            var files = await _fileShareService.ListFilesAsync();

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(JsonSerializer.Serialize(files.Select(f => new
            {
                fileName = f.FileName,
                sizeBytes = f.SizeBytes,
                lastModified = f.LastModified
            })));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List failed");
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
            if (string.IsNullOrWhiteSpace(fileName))
                return await BadRequest(req, "File name is required.");

            var download = await _fileShareService.DownloadFileAsync(fileName);
            if (download == null)
            {
                var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                await notFoundResponse.WriteStringAsync($"File '{fileName}' was not found in staff-docs.");
                return notFoundResponse;
            }

            var extension = Path.GetExtension(fileName);
            var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", mimeType);
            response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");

            await download.Content.CopyToAsync(response.Body);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for {FileName}", fileName);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Download failed: {ex.Message}");
            return errorResponse;
        }
    }

    private static string? GetBoundary(string contentType)
    {
        var elements = contentType.Split(';');
        var boundaryElement = Array.Find(elements, e => e.Trim().StartsWith("boundary=", StringComparison.OrdinalIgnoreCase));
        if (boundaryElement == null)
            return null;

        return boundaryElement.Trim()["boundary=".Length..].Trim('"');
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }
}
