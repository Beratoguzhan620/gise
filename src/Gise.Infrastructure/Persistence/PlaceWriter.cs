using Gise.Core.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Gise.Infrastructure.Persistence;

/// <summary>Writes an event's places with COPY: a 50,000-seat stadium goes in well under a second.</summary>
public static class PlaceWriter
{
    /// <summary>Runs on the context's connection and inside its current transaction, if there is one.</summary>
    public static async Task WriteAsync(GiseDbContext db, IReadOnlyList<Place> places, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY places (id, event_id, section_key, row, number, category_id, status) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var place in places)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(place.Id, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(place.EventId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(place.SectionKey, NpgsqlDbType.Varchar, ct);
            if (place.Row is null)
            {
                await writer.WriteNullAsync(ct);
            }
            else
            {
                await writer.WriteAsync(place.Row, NpgsqlDbType.Varchar, ct);
            }

            await writer.WriteAsync(place.Number, NpgsqlDbType.Integer, ct);
            await writer.WriteAsync(place.CategoryId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(place.Status.ToString(), NpgsqlDbType.Varchar, ct);
        }

        await writer.CompleteAsync(ct);
    }
}
