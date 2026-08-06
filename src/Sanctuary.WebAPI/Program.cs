using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;

using Sanctuary.Core.CollectionVolunteers;
using Sanctuary.Core.Extensions;
using Sanctuary.Database;
using Sanctuary.WebAPI.Endpoints;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Services;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls();

// Proxy Server / Load Balancer
var forwardedHeaderSection = builder.Configuration.GetSection("ForwardedHeadersOptions");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

if (forwardedHeaderSection.Exists())
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeaderSection);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy(AuthEndpoints.RegisterRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy(ModelCatalogEndpoints.LoginRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

});

// Options
builder.Services.AddOptionsWithValidateOnStart<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptionsWithValidateOnStart<WebAPIOptions>()
    .BindConfiguration(WebAPIOptions.Section)
    .ValidateOnStart();

builder.Services.AddOptionsWithValidateOnStart<ModelCatalogOptions>()
    .BindConfiguration(ModelCatalogOptions.Section)
    .Validate(options => options.MaximumUploadBytes > 0)
    .Validate(options => options.MaximumStoredBytes > 0)
    .Validate(options => options.MaximumSourcePixels > 0)
    .Validate(options => options.MaximumWidth > 0 && options.MaximumHeight > 0)
    .ValidateOnStart();

var modelCatalogOptions = builder.Configuration
    .GetSection(ModelCatalogOptions.Section)
    .Get<ModelCatalogOptions>() ?? new ModelCatalogOptions();

Directory.CreateDirectory(modelCatalogOptions.DataProtectionPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(modelCatalogOptions.DataProtectionPath))
    .SetApplicationName("Sanctuary.ModelCatalog");

builder.Services.AddAuthentication(ModelCatalogEndpoints.AuthenticationScheme)
    .AddCookie(ModelCatalogEndpoints.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "osfr_rp_catalog";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return System.Threading.Tasks.Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return System.Threading.Tasks.Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = modelCatalogOptions.MaximumUploadBytes + 64 * 1024);
builder.Services.AddSingleton<ModelCatalogStore>();
builder.Services.AddSingleton(new CollectionVolunteerStore(
    builder.Configuration["CollectionVolunteers:StoragePath"] ??
    Path.Combine("Data", "CollectionVolunteers", "placements.json")));
builder.Services.AddSingleton(new DeployedCollectionPlacementReader(
    Path.Combine(AppContext.BaseDirectory, "CollectionNodeSpawns")));

// Database
builder.Services.AddDatabase(builder.Configuration);

// Logging
builder.Logging.ClearProviders();

#if DEBUG

builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

#endif

var nlogConfigFile = builder.Environment.IsDevelopment()
    ? "NLog.Development.config"
    : "NLog.config";

builder.Logging.AddNLog(nlogConfigFile);

var app = builder.Build();

#if DEBUG

app.UseHttpLogging();

#endif

// Configure the HTTP request pipeline.

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapPortraitEndpoints();
app.MapStatsEndpoints();
app.MapModelCatalogEndpoints();
app.MapCollectionVolunteerEndpoints();

app.Run();
