using API.HealthChecks;
using API.Logging;
using API.Middleware;
using Core.Application.DependencyInjection;
using Infrastructure.Persistence.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var env = builder.Environment.EnvironmentName;

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Host.UseSerilog((context, services, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

builder.Services.AddPersistence();
builder.Services.AddApplication();
builder.Services.Configure<LoggingPolicyOptions>(
    builder.Configuration.GetSection(LoggingPolicyOptions.SectionName));

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API çalışıyor."))
    .AddCheck<LogFileHealthCheck>("log-file");

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new()
    {
        Title       = "Dynamic Log Level Filtering PoC",
        Version     = "v1",
        Description = $"Ortam: {builder.Environment.EnvironmentName} | " +
                      "appsettings → Serilog:MinimumLevel:Default ile aktif log seviyesini yönetin."
    }));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseSerilogEnrichment();
app.UseSerilogMiddleware();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
