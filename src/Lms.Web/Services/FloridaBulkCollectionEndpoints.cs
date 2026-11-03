using Microsoft.AspNetCore.Http.HttpResults;

namespace Lms.Web.Services;

public static class FloridaBulkCollectionEndpoints
{
    private static readonly HashSet<string> Files = new(StringComparer.Ordinal)
    {
        "REALESTATE2501LICENSE_1.csv", "RealEstateCorpLicense.csv", "RealEstateSchoolLicense.csv",
        "lic64appr.csv", "re_salesperson.csv", "re_appraiser.csv", "manifest.json"
    };

    public static void MapFloridaBulkCollectionExports(this WebApplication app)
    {
        MapExports(app, "Florida", Files);
        MapExports(app, "Texas", new(StringComparer.Ordinal) { "license-holders.csv", "manifest.json" });
        MapExports(app, "California", new(StringComparer.Ordinal) { "CurrList.zip", "broker_associates_list.xls", "manifest.json" });
        MapExports(app, "Colorado", new(StringComparer.Ordinal) { "licensed-real-estate-professionals.csv", "manifest.json" });
        MapExports(app, "Nevada", new(Lms.Application.Services.NevadaNredBulkImportService.Definitions
            .Select(file => file.Name).Append("manifest.json"), StringComparer.Ordinal));
        MapExports(app, "New York", new(StringComparer.Ordinal)
            { Lms.Application.Services.NewYorkDosBulkImportService.FileName, "manifest.json" });
        MapExports(app, "Arizona", new(Lms.Application.Services.ArizonaAdreBulkImportService.Definitions
            .Select(file => file.Name).Append("manifest.json"), StringComparer.Ordinal));
    }

    private static void MapExports(WebApplication app, string state, HashSet<string> files)
    {
        app.MapGet($"/admin/{state.ToLowerInvariant()}-bulk/exports/{{fileName}}",
            async Task<Results<PhysicalFileHttpResult, ProblemHttpResult>> (
                string fileName, IServiceProvider services, HttpContext context,
                ILogger<FloridaBulkCollectionService> logger, CancellationToken ct) =>
            {
                if (!files.Contains(fileName))
                    return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unknown bulk export.");
                BulkCollectionService collection = state switch
                {
                    "California" => services.GetRequiredService<CaliforniaBulkCollectionService>(),
                    "Texas" => services.GetRequiredService<TexasBulkCollectionService>(),
                    "Colorado" => services.GetRequiredService<ColoradoDoraBulkCollectionService>(),
                    "Nevada" => services.GetRequiredService<NevadaBulkCollectionService>(),
                    "New York" => services.GetRequiredService<NewYorkBulkCollectionService>(),
                    "Arizona" => services.GetRequiredService<ArizonaBulkCollectionService>(),
                    _ => services.GetRequiredService<FloridaBulkCollectionService>()
                };
                string? path;
                try
                {
                    path = await collection.GetExportPathAsync(fileName, ct);
                }
                catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
                {
                    logger.LogError(exception, "{State} bulk export {FileName} could not be read", state, fileName);
                    return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Bulk archive is unavailable. Try again later.");
                }
                if (path is null || !File.Exists(path))
                    return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
                        title: "No completed bulk archive is available for this file.");
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.PhysicalFile(path,
                    Path.GetExtension(fileName) switch
                    {
                        ".csv" => "text/csv; charset=utf-8",
                        ".zip" => "application/zip",
                        ".xls" => "application/vnd.ms-excel",
                        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        _ => "application/json"
                    },
                    fileDownloadName: fileName);
            })
            .RequireAuthorization(policy => policy.RequireRole("Admin"))
            .WithName($"Download{state}BulkArchive")
            .WithSummary($"Download a file from the last successful {state} bulk collection.")
            .WithDescription("Admin-only original official CSV, ZIP or XLS source file, or archive manifest. Does not collect, import or activate records.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
