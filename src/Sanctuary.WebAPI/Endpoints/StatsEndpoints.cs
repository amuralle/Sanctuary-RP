using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;

namespace Sanctuary.WebAPI.Endpoints;

public static class StatsEndpoints
{
    public static void MapStatsEndpoints(this WebApplication app)
    {
        app.MapGet("/stats", StatsHandlerAsync);
    }

    private static async Task<IResult> StatsHandlerAsync(
        HttpContext context,
        CancellationToken cancellationToken,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var registeredAccounts = await dbContext.Users.CountAsync(cancellationToken);
        var createdCharacters = await dbContext.Characters.CountAsync(cancellationToken);

        context.Response.Headers.CacheControl = "public, max-age=30";

        return Results.Ok(new
        {
            registeredAccounts,
            createdCharacters
        });
    }
}
