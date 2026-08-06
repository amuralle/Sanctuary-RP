using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Sanctuary.Core.CollectionVolunteers;
using Sanctuary.Database;

namespace Sanctuary.WebAPI.Endpoints;

public static class CollectionVolunteerEndpoints
{
    public static void MapCollectionVolunteerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/volunteers/placements");

        group.MapGet("/", ListHandler);
        group.MapPut("/{id:int}", UpdateHandlerAsync).RequireAuthorization();
        group.MapDelete("/{id:int}", DeleteHandlerAsync).RequireAuthorization();
    }

    private static IResult ListHandler(
        HttpContext context,
        CollectionVolunteerStore store,
        DeployedCollectionPlacementReader deployedPlacementReader)
    {
        var currentUserId = GetClaimedUserId(context.User);
        var isAdmin = context.User.IsInRole("Admin");
        var submissions = store.GetAll();
        var linkedHardPoints = submissions
            .Where(placement => placement.HardPointId.HasValue)
            .Select(placement => (placement.ZoneId, placement.Pool, Id: placement.HardPointId!.Value))
            .ToHashSet();
        var placements = deployedPlacementReader.GetAll()
            .Where(placement => !linkedHardPoints.Contains(
                (placement.ZoneId, placement.Pool, placement.Id)))
            .Select(ToResponse)
            .Concat(submissions
            .OrderByDescending(placement => placement.Id)
            .Select(placement => ToResponse(placement, currentUserId, isAdmin)))
            .ToArray();

        return Results.Ok(placements);
    }

    private static async Task<IResult> UpdateHandlerAsync(
        int id,
        CollectionVolunteerUpdateRequest request,
        HttpContext context,
        CancellationToken cancellationToken,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CollectionVolunteerStore store)
    {
        var actor = await GetActorAsync(context, cancellationToken, dbContextFactory);
        if (actor is null)
            return Results.Unauthorized();

        if (!Enum.TryParse<CollectionVolunteerPlacementStatus>(request.Status, true, out var status))
            return Results.BadRequest(new { error = "Status must be pending, accepted, or rejected." });

        if (!store.TryUpdate(
            id,
            actor.UserId,
            actor.Username,
            actor.IsAdmin,
            request.Pool,
            request.X,
            request.Y,
            request.Z,
            request.Heading,
            request.Note,
            status,
            out var updated) || updated is null)
        {
            return Results.Forbid();
        }

        return Results.Ok(ToResponse(updated, actor.UserId, actor.IsAdmin));
    }

    private static async Task<IResult> DeleteHandlerAsync(
        int id,
        HttpContext context,
        CancellationToken cancellationToken,
        IDbContextFactory<DatabaseContext> dbContextFactory,
        CollectionVolunteerStore store)
    {
        var actor = await GetActorAsync(context, cancellationToken, dbContextFactory);
        if (actor is null)
            return Results.Unauthorized();

        return store.TryDelete(id, actor.UserId, actor.IsAdmin)
            ? Results.NoContent()
            : Results.Forbid();
    }

    private static async Task<Actor?> GetActorAsync(
        HttpContext context,
        CancellationToken cancellationToken,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        var claimedUserId = GetClaimedUserId(context.User);
        if (claimedUserId == 0)
            return null;

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.Id == claimedUserId &&
                (user.LockedUntil == null || user.LockedUntil <= DateTimeOffset.UtcNow))
            .Select(user => new Actor(user.Id, user.Username, user.IsAdmin))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static ulong GetClaimedUserId(ClaimsPrincipal principal)
    {
        return ulong.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var userId)
            ? userId
            : 0;
    }

    private static object ToResponse(
        CollectionVolunteerPlacement placement,
        ulong currentUserId,
        bool isAdmin)
    {
        return new
        {
            key = $"submission:{placement.Id}",
            source = "submission",
            placement.Id,
            placement.ZoneId,
            placement.Pool,
            placement.X,
            placement.Y,
            placement.Z,
            placement.Heading,
            placement.Note,
            submittedBy = placement.SubmittedByCharacterName,
            placement.SubmittedAt,
            status = placement.Status.ToString().ToLowerInvariant(),
            placement.HardPointId,
            reviewedBy = placement.ReviewedByUsername,
            placement.ReviewedAt,
            canEdit = isAdmin ||
                (placement.SubmittedByUserId == currentUserId &&
                    placement.Status == CollectionVolunteerPlacementStatus.Pending),
            canReview = isAdmin
        };
    }

    private static object ToResponse(DeployedCollectionPlacement placement)
    {
        return new
        {
            key = $"deployed:{placement.ZoneId}:{placement.Pool}:{placement.Id}",
            source = "deployed",
            placement.Id,
            placement.ZoneId,
            placement.Pool,
            placement.X,
            placement.Y,
            placement.Z,
            placement.Heading,
            note = (string?)null,
            submittedBy = "Server resource",
            submittedAt = (DateTimeOffset?)null,
            status = "deployed",
            reviewedBy = (string?)null,
            reviewedAt = (DateTimeOffset?)null,
            canEdit = false,
            canReview = false
        };
    }

    private sealed record Actor(ulong UserId, string Username, bool IsAdmin);

    private sealed record CollectionVolunteerUpdateRequest(
        string Pool,
        float X,
        float Y,
        float Z,
        float Heading,
        string? Note,
        string Status);
}
