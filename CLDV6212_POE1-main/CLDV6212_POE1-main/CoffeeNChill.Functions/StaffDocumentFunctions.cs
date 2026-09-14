using System.Text;
using CoffeeNChill.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
    public async Task<IActionResult> UploadStaffDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "documents/upload")] HttpRequest req)
    {
        try
        {
            if (!req.HasFormContentType)
                return new BadRequestObjectResult("Expected multipart/form-data.");

            var form = await req.ReadFormAsync();
            var uploadedFile = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();

            string? fileName = form["fileName"].ToString();
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = uploadedFile?.FileName;

            byte[] fileBytes;
            if (uploadedFile is { Length: > 0 })
            {
                using var memoryStream = new MemoryStream();
                await uploadedFile.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }
            else if (!string.IsNullOrWhiteSpace(form["file"]))
            {
                fileBytes = Encoding.UTF8.GetBytes(form["file"].ToString());
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "sample-recipe.txt";
            }
            else
            {
                return new BadRequestObjectResult("No file found in the request body.");
            }

            fileName = Path.GetFileName(fileName?.Trim('"'));
            if (string.IsNullOrWhiteSpace(fileName))
                return new BadRequestObjectResult("A file name is required.");

            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                return new BadRequestObjectResult(
                    $"Unsupported file type '{extension}'. Allowed: {string.Join(", ", AllowedExtensions)}.");
            }

            using var uploadStream = new MemoryStream(fileBytes);
            await _fileShareService.UploadFileAsync(fileName, uploadStream, fileBytes.Length);

            _logger.LogInformation("Uploaded file '{FileName}' ({Size} bytes) to staff-docs.", fileName, fileBytes.Length);

            return new ObjectResult(new
            {
                message = "File uploaded successfully",
                fileName,
                sizeBytes = fileBytes.Length
            })
            {
                StatusCode = StatusCodes.Status201Created
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload failed");
            return new ObjectResult($"Upload failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    [Function("ListStaffDocuments")]
    public async Task<IActionResult> ListStaffDocuments(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents")] HttpRequest req)
    {
        try
        {
            var files = await _fileShareService.ListFilesAsync();
            return new OkObjectResult(files.Select(f => new
            {
                fileName = f.FileName,
                sizeBytes = f.SizeBytes,
                lastModified = f.LastModified
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List failed");
            return new ObjectResult($"List failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    [Function("DownloadStaffDocument")]
    public async Task<IActionResult> DownloadStaffDocument(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "documents/download/{fileName}")] HttpRequest req,
        string fileName)
    {
        try
        {
            fileName = Uri.UnescapeDataString(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName))
                return new BadRequestObjectResult("File name is required.");

            var download = await _fileShareService.DownloadFileAsync(fileName);
            if (download == null)
                return new NotFoundObjectResult($"File '{fileName}' was not found in staff-docs.");

            var extension = Path.GetExtension(fileName);
            var mimeType = MimeTypes.GetValueOrDefault(extension, "application/octet-stream");

            var memoryStream = new MemoryStream();
            await download.Content.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            return new FileStreamResult(memoryStream, mimeType)
            {
                FileDownloadName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed for {FileName}", fileName);
            return new ObjectResult($"Download failed: {ex.Message}")
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}
