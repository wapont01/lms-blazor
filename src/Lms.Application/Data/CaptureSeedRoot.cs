namespace Lms.Application.Data;

/// <summary>
/// Locates the workspace root containing the docs/ capture folders regardless of
/// the process working directory (dotnet run, published output, or tests).
/// </summary>
internal static class CaptureSeedRoot
{
    public static string? Resolve(string? workspaceRoot)
    {
        if (workspaceRoot is not null)
        {
            return workspaceRoot;
        }

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "docs")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }
}
