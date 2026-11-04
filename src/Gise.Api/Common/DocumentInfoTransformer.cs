using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Gise.Api.Common;

/// <summary>The landing text of the API reference.</summary>
internal sealed class DocumentInfoTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Gişe",
            Version = "1.0",
            Description = """
                Ticketing for organizers: venues with seat maps, events with price categories, a waiting room for busy
                sales, seats held while a buyer pays, signed QR tickets and gate check-in that keeps working offline.

                **Getting started**

                1. Create an account at `POST /api/auth/register` and paste the access token into *Authentication*.
                2. Create an organizer at `POST /api/organizations`, a venue with a floor plan, then an event.
                3. Publish the event; it shows up at `GET /api/events` for everyone.

                Organizer teams have three roles: staff check tickets at the gate, managers also run venues, events
                and refunds, owners also manage the team.

                **Errors** follow RFC 9457 problem details with a stable `code`, for example `validation_failed`
                or `place_unavailable`. Every response carries an `X-Correlation-Id`.
                """,
            License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://github.com/Beratoguzhan620/gise/blob/main/LICENSE") },
        };
        return Task.CompletedTask;
    }
}
