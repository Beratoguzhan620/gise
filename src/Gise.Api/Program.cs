using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using Gise.Api.Common;
using Gise.Api.Features.Auth;
using Gise.Api.Features.Events;
using Gise.Api.Features.Organizations;
using Gise.Api.Features.Venues;
using Gise.Api.Logging;
using Gise.Infrastructure;
using Gise.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Gise");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Gise is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Gise environment variable.");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddGisePersistence(connectionString);
builder.Services.AddGiseAuth(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Sign-up and sign-in per address: plenty for a person, slow for guessing.
    var perMinute = builder.Configuration.GetValue("Auth:AttemptsPerMinute", 10);
    options.AddPolicy(AuthEndpoints.RateLimit, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await TypedResults.Problem(
            title: "Too many requests",
            detail: "Slow down and try again in a minute.",
            statusCode: StatusCodes.Status429TooManyRequests,
            extensions: new Dictionary<string, object?> { ["code"] = Gise.Core.ErrorCodes.RateLimited })
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<GiseDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Gişe")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .AddPreferredSecuritySchemes(SecuritySchemesTransformer.Scheme)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());

app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapAuthEndpoints();
app.MapOrganizationEndpoints();
app.MapMemberEndpoints();
app.MapVenueEndpoints();
app.MapEventEndpoints();
app.MapCatalogEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await DependencyInjection.MigrateAsync(app.Services);
}

await app.RunAsync();

public partial class Program;
