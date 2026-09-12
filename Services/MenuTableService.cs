using Azure;
using Azure.Data.Tables;
using CoffeeNChill.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CoffeeNChill.Services
{
    public class MenuTableService
    {
        private readonly TableClient _tableClient;

        public MenuTableService(string connectionString, string tableName = "MenuItems")
        {
            var serviceClient = new TableServiceClient(connectionString);
            _tableClient = serviceClient.GetTableClient(tableName);
            _tableClient.CreateIfNotExists();
        }

        public async Task AddOrUpdateMenuItemAsync(MenuItem menuItem)
        {
            await _tableClient.UpsertEntityAsync(menuItem, TableUpdateMode.Replace);
        }

        public async Task<List<MenuItem>> GetAllMenuItemsAsync()
        {
            var menuItems = new List<MenuItem>();
            AsyncPageable<MenuItem> queryResults = _tableClient.QueryAsync<MenuItem>();

            await foreach (var item in queryResults)
            {
                menuItems.Add(item);
            }

            return menuItems;
        }

        public async Task<MenuItem?> GetMenuItemAsync(string category, string rowKey)
        {
            try
            {
                Response<MenuItem> response = await _tableClient.GetEntityAsync<MenuItem>(category, rowKey);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteMenuItemAsync(string category, string rowKey)
        {
            await _tableClient.DeleteEntityAsync(category, rowKey);
        }
    }
}