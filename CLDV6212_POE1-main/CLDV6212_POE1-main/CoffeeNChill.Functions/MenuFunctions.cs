using System.Text.Json;
using CoffeeNChill.Models;
using CoffeeNChill.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CoffeeNChill.Functions;

public class MenuFunctions
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hot Drinks", "Cold Drinks", "Pastries", "Sandwiches"
    };

    private readonly MenuTableService _tableService;
    private readonly ILogger<MenuFunctions> _logger;

    public MenuFunctions(MenuTableService tableService, ILogger<MenuFunctions> logger)
    {
        _tableService = tableService;
        _logger = logger;
    }

    [Function("CreateMenuItem")]
    public async Task<IActionResult> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "menu")] HttpRequest req)
    {
        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        MenuItem? menuItem;
        try
        {
            menuItem = JsonSerializer.Deserialize<MenuItem>(requestBody,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult("Malformed JSON body.");
        }

        if (menuItem == null)
            return new BadRequestObjectResult("Request body is required.");

        if (string.IsNullOrWhiteSpace(menuItem.Name))
            return new BadRequestObjectResult("Name is required.");

        if (string.IsNullOrWhiteSpace(menuItem.PartitionKey))
            return new BadRequestObjectResult("Category (partitionKey) is required.");

        if (!ValidCategories.Contains(menuItem.PartitionKey))
            return new BadRequestObjectResult($"Invalid category. Allowed values: {string.Join(", ", ValidCategories)}.");

        if (menuItem.Price < 0)
            return new BadRequestObjectResult("Price cannot be negative.");

        if (string.IsNullOrWhiteSpace(menuItem.RowKey))
            menuItem.RowKey = $"SKU-{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        try
        {
            await _tableService.AddOrUpdateMenuItemAsync(menuItem);
            _logger.LogInformation("Created menu item {RowKey} in category {Category}", menuItem.RowKey, menuItem.PartitionKey);
            return new CreatedResult($"/api/menu/{menuItem.PartitionKey}/{menuItem.RowKey}", menuItem);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create menu item");
            return new ObjectResult("Failed to create menu item.") { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }

    [Function("GetAllMenuItems")]
    public async Task<IActionResult> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequest req)
    {
        var items = await _tableService.GetAllMenuItemsAsync();
        return new OkObjectResult(items);
    }

    [Function("GetMenuItemsByCategory")]
    public async Task<IActionResult> GetByCategory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu/category/{category}")] HttpRequest req,
        string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return new BadRequestObjectResult("Category is required.");

        var filtered = await _tableService.GetMenuItemsByCategoryAsync(category);
        return new OkObjectResult(filtered);
    }

    [Function("UpdateMenuItem")]
    public async Task<IActionResult> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "menu/{category}/{id}")] HttpRequest req,
        string category, string id)
    {
        var existing = await _tableService.GetMenuItemAsync(category, id);
        if (existing == null)
            return new NotFoundObjectResult($"Menu item '{id}' in category '{category}' was not found.");

        string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(requestBody))
            return new BadRequestObjectResult("Request body is required.");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(requestBody); }
        catch (JsonException) { return new BadRequestObjectResult("Malformed JSON body."); }

        var root = doc.RootElement;
        var hasUpdate = false;

        if (root.TryGetProperty("price", out var priceEl) && priceEl.TryGetDouble(out var newPrice))
        {
            if (newPrice < 0) return new BadRequestObjectResult("Price cannot be negative.");
            existing.Price = newPrice;
            hasUpdate = true;
        }

        if (root.TryGetProperty("isAvailable", out var availEl) &&
            (availEl.ValueKind == JsonValueKind.True || availEl.ValueKind == JsonValueKind.False))
        {
            existing.IsAvailable = availEl.GetBoolean();
            hasUpdate = true;
        }

        if (root.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
        {
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return new BadRequestObjectResult("Name cannot be empty.");
            existing.Name = name;
            hasUpdate = true;
        }

        if (root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
        {
            existing.Description = descEl.GetString() ?? existing.Description;
            hasUpdate = true;
        }

        if (!hasUpdate)
            return new BadRequestObjectResult("No valid fields to update. Supported fields: price, isAvailable, name, description.");

        await _tableService.AddOrUpdateMenuItemAsync(existing);
        return new OkObjectResult(existing);
    }

    [Function("DeleteMenuItem")]
    public async Task<IActionResult> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{id}")] HttpRequest req,
        string category, string id)
    {
        var existing = await _tableService.GetMenuItemAsync(category, id);
        if (existing == null)
            return new NotFoundObjectResult($"Menu item '{id}' in category '{category}' was not found.");

        await _tableService.DeleteMenuItemAsync(category, id);
        return new NoContentResult();
    }
}
