using Microsoft.Extensions.Hosting;
using OrderFlow.Inventory;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "orderflow-inventory")
    .WriteTo.Console()
    .WriteTo.Seq(builder.Configuration["Seq:Url"] ?? "http://localhost:5351"));

builder.Services.AddInventoryService(builder.Configuration);

await builder.Build().RunAsync();
