using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderFlow.Api.Middleware;
using OrderFlow.Application;
using OrderFlow.Infrastructure;
using OrderFlow.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "orderflow-api")
    .WriteTo.Console()
    .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5351"));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Opt-in: apply EF Core migrations at startup (local/compose). In real deployments run them as a separate step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

// Opt-in: insert sample products into an empty catalog (local development only).
if (app.Configuration.GetValue<bool>("Database:SeedDemoData"))
{
    await app.Services.SeedDemoDataAsync();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapControllers();

// Liveness: the process is up (no dependency checks). Readiness: dependencies are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(OrderFlow.Infrastructure.DependencyInjection.ReadyTag)
});

app.Run();
