using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MiniValidation;

using Sanctuary.Database;
using Sanctuary.WebAPI.Models;
using Sanctuary.WebAPI.Options;
using Sanctuary.WebAPI.Services;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using BC = BCrypt.Net.BCrypt;

namespace Sanctuary.WebAPI.Endpoints;

public static class ModelCatalogEndpoints
{
    public const string AuthenticationScheme = "ModelCatalog";
    public const string LoginRateLimitPolicy = "catalog-login";

    private static ILogger _logger = null!;

    public static void MapModelCatalogEndpoints(this WebApplication app)
    {
        _logger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(nameof(ModelCatalogEndpoints));

        var group = app.MapGroup("/catalog");

        group.MapPost("/login", LoginHandlerAsync)
            .RequireRateLimiting(LoginRateLimitPolicy);
        group.MapPost("/logout", LogoutHandlerAsync)
            .RequireAuthorization();
        group.MapGet("/session", SessionHandler);
        group.MapGet("/models", ModelsHandler);
        group.MapGet("/models/{modelId:int}/image", ImageHandler);
        group.MapPost("/models/{modelId:int}/image", UploadHandlerAsync)
            .RequireAuthorization()
            .DisableAntiforgery();
    }

    private static async Task<IResult> LoginHandlerAsync(
        ModelCatalogLoginRequest request,
        HttpContext context,
        CancellationToken cancellationToken,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        if (!MiniValidator.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dbUser = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Username == request.Username, cancellationToken);

        if (dbUser is null || !BC.Verify(request.Password, dbUser.Password))
        {
            _logger.LogWarning("Catalog login failed for username: {Username}", request.Username);
            return Results.Unauthorized();
        }

        if (dbUser.LockedUntil > DateTimeOffset.UtcNow)
            return Results.Unauthorized();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, dbUser.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, dbUser.Username),
            new Claim(ClaimTypes.Role, dbUser.IsAdmin ? "Admin" : "Player")
        };
        var identity = new ClaimsIdentity(claims, AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await context.SignInAsync(
            AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        return Results.Ok(new { username = dbUser.Username, isAdmin = dbUser.IsAdmin });
    }

    private static async Task LogoutHandlerAsync(HttpContext context)
    {
        await context.SignOutAsync(AuthenticationScheme);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static IResult SessionHandler(HttpContext context)
    {
        return context.User.Identity?.IsAuthenticated == true
            ? Results.Ok(new
            {
                username = context.User.Identity.Name,
                isAdmin = context.User.IsInRole("Admin")
            })
            : Results.Unauthorized();
    }

    private static IResult ModelsHandler(ModelCatalogStore store)
    {
        return Results.Ok(store.GetEntries().Select(ToResponse));
    }

    private static IResult ImageHandler(
        int modelId,
        HttpContext context,
        ModelCatalogStore store)
    {
        if (!store.TryGetEntry(modelId, out var entry) || entry is null)
            return Results.NotFound();

        var imagePath = store.GetImagePath(modelId);
        if (!File.Exists(imagePath))
            return Results.NotFound();

        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return Results.File(
            imagePath,
            "image/webp",
            lastModified: entry.UpdatedAt,
            enableRangeProcessing: false);
    }

    private static async Task<IResult> UploadHandlerAsync(
        int modelId,
        HttpContext context,
        CancellationToken cancellationToken,
        IOptions<ModelCatalogOptions> options,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        ModelCatalogStore store)
    {
        if (!store.IsValidModel(modelId))
            return Results.NotFound();

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ulong.TryParse(userIdValue, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            return Results.Unauthorized();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var dbUser = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

        if (dbUser is null || dbUser.LockedUntil > DateTimeOffset.UtcNow)
            return Results.Unauthorized();

        if (!context.Request.HasFormContentType)
            return Results.BadRequest(new { error = "Upload an image using multipart form data." });

        var maximumUploadBytes = options.Value.MaximumUploadBytes;
        if (context.Request.ContentLength > maximumUploadBytes)
            return Results.BadRequest(new { error = $"Images must be {FormatBytes(maximumUploadBytes)} or smaller." });

        IFormCollection form;

        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "The uploaded form is invalid." });
        }

        var file = form.Files.GetFile("image");
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "Choose an image to upload." });

        if (file.Length > maximumUploadBytes)
            return Results.BadRequest(new { error = $"Images must be {FormatBytes(maximumUploadBytes)} or smaller." });

        if (file.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            return Results.BadRequest(new { error = "Use a JPEG, PNG, or WebP image." });

        var normalized = await NormalizeImageAsync(file, options.Value, cancellationToken);
        if (normalized.Error is not null)
            return Results.BadRequest(new { error = normalized.Error });

        var entry = await store.ReplaceAsync(
            modelId,
            normalized.Bytes!,
            normalized.Width,
            normalized.Height,
            dbUser.Id,
            dbUser.Username,
            cancellationToken);

        _logger.LogInformation(
            "Catalog image for model {ModelId} was updated by {Username} ({UserId}).",
            modelId,
            dbUser.Username,
            dbUser.Id);

        return Results.Ok(ToResponse(entry));
    }

    private static async Task<NormalizedImage> NormalizeImageAsync(
        IFormFile file,
        ModelCatalogOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var identifyStream = file.OpenReadStream();
            var imageInfo = await Image.IdentifyAsync(identifyStream, cancellationToken);

            if (imageInfo is null)
                return NormalizedImage.Failed("The file is not a readable image.");

            var format = imageInfo.Metadata.DecodedImageFormat;
            if (format is not JpegFormat and not PngFormat and not WebpFormat)
                return NormalizedImage.Failed("Use a JPEG, PNG, or WebP image.");

            if ((long)imageInfo.Width * imageInfo.Height > options.MaximumSourcePixels)
                return NormalizedImage.Failed("The image dimensions are too large.");

            await using var imageStream = file.OpenReadStream();
            using var image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);

            image.Mutate(operation => operation.AutoOrient());

            if (image.Width > options.MaximumWidth || image.Height > options.MaximumHeight)
            {
                image.Mutate(operation => operation.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(options.MaximumWidth, options.MaximumHeight)
                }));
            }

            for (var sizeAttempt = 0; sizeAttempt < 4; sizeAttempt++)
            {
                foreach (var quality in new[] { 82, 72, 62, 52 })
                {
                    await using var output = new MemoryStream();
                    await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = quality }, cancellationToken);

                    if (output.Length <= options.MaximumStoredBytes)
                        return new NormalizedImage(output.ToArray(), image.Width, image.Height, null);
                }

                image.Mutate(operation => operation.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(
                        Math.Max(1, (int)(image.Width * 0.8)),
                        Math.Max(1, (int)(image.Height * 0.8)))
                }));
            }

            return NormalizedImage.Failed(
                $"The optimized image is still larger than {FormatBytes(options.MaximumStoredBytes)}.");
        }
        catch (UnknownImageFormatException)
        {
            return NormalizedImage.Failed("The file is not a readable JPEG, PNG, or WebP image.");
        }
        catch (InvalidImageContentException)
        {
            return NormalizedImage.Failed("The image file is damaged or incomplete.");
        }
    }

    private static object ToResponse(ModelCatalogEntry entry)
    {
        return new
        {
            entry.ModelId,
            imageUrl = $"catalog/models/{entry.ModelId}/image?v={entry.UpdatedAt.ToUnixTimeSeconds()}",
            updatedBy = entry.UpdatedByUsername,
            entry.UpdatedAt,
            entry.Width,
            entry.Height,
            entry.StoredBytes
        };
    }

    private static string FormatBytes(long bytes)
    {
        return $"{bytes / (1024 * 1024d):0.#} MB";
    }

    private sealed record NormalizedImage(byte[]? Bytes, int Width, int Height, string? Error)
    {
        public static NormalizedImage Failed(string error)
        {
            return new NormalizedImage(null, 0, 0, error);
        }
    }
}
