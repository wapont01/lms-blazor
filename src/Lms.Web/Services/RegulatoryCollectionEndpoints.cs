using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Lms.Web.Services;

public static class RegulatoryCollectionEndpoints
{
    public static void MapRegulatoryCollectionExports(this WebApplication app)
    {
        app.MapGet("/admin/regulatory-statistics/exports/{state}/{resource}",
            async Task<Results<PushStreamHttpResult, ProblemHttpResult>> (
                string state, string resource, IRegulatoryCollectionClient collection,
                HttpContext context, ILogger<RegulatoryCollectionClient> logger, CancellationToken ct) =>
            {
                if (!collection.IsConfigured(state) || resource is not ("licenses.csv" or "licenses.json" or "entries.json"))
                    return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Collection export not found.");
                HttpResponseMessage response;
                try
                {
                    response = await collection.OpenExportAsync(state, resource, ct);
                }
                catch (Exception exception) when (exception is HttpRequestException
                    || exception is OperationCanceledException && !ct.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Collection export is unavailable for {State}", state);
                    return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Collection service is unavailable. Try again later.");
                }
                if (!response.IsSuccessStatusCode)
                {
                    var status = response.StatusCode;
                    response.Dispose();
                    logger.LogWarning("Collection export for {State} returned HTTP {Status}", state, (int)status);
                    return TypedResults.Problem(statusCode: status == HttpStatusCode.NotFound
                        ? StatusCodes.Status404NotFound : StatusCodes.Status502BadGateway,
                        title: status == HttpStatusCode.NotFound ? "Export is not available yet." : "Collection export could not be retrieved.");
                }
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.Stream(async output =>
                {
                    using (response)
                        await response.Content.CopyToAsync(output, ct);
                }, resource.EndsWith(".csv", StringComparison.Ordinal) ? "text/csv; charset=utf-8" : "application/json",
                    $"{state}-{resource}");
            })
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .WithSummary("Download the collector's latest exported license CSV, license JSON, or raw entries JSON.")
            .WithDescription("Admin-only streaming export. Files reflect the last export batch, not necessarily live counters. Does not approve or publish records.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}
