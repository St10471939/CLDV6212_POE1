using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using CoffeeNChill.Models;
using CoffeeNChill.Services;
using System;

namespace CoffeeNChill.Functions
{
    public class MenuFunctions
    {
        private static readonly string ConnectionString =
            Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? "UseDevelopmentStorage=true";

        private static readonly MenuTableService TableService = new MenuTableService(ConnectionString);

        [Function("GetAllMenuItems")]
        public async Task<IActionResult> GetAll(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "menu")] HttpRequest req)
        {
            var items = await TableService.GetAllMenuItemsAsync();
            return new OkObjectResult(items);
        }

        [Function("CreateOrUpdateMenuItem")]
        public async Task<IActionResult> CreateOrUpdate(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "put", Route = "menu")] HttpRequest req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var menuItem = JsonSerializer.Deserialize<MenuItem>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (menuItem == null || string.IsNullOrEmpty(menuItem.Name))
            {
                return new BadRequestObjectResult("Invalid menu item payload.");
            }

            await TableService.AddOrUpdateMenuItemAsync(menuItem);
            return new OkObjectResult(menuItem);
        }

        [Function("DeleteMenuItem")]
        public async Task<IActionResult> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "menu/{category}/{rowKey}")] HttpRequest req,
            string category,
            string rowKey)
        {
            await TableService.DeleteMenuItemAsync(category, rowKey);
            return new OkResult();
        }
    }
}