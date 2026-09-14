using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CoffeeNChill.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(_ =>
    new MenuTableService(StorageConnectionHelper.GetConnectionString()));

builder.Services.AddSingleton(_ =>
    new StaffFileShareService(StorageConnectionHelper.GetConnectionString()));

builder.Build().Run();
