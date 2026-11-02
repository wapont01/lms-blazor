using System.Text.Json;
using Lms.Application.Services;

namespace Lms.Web.Services;

public sealed class ColoradoDoraDisplaySelectionStore : IColoradoDoraLicenseTypeSelection
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string filePath;
    private readonly object sync = new();
    private string[]? selectedTypes;

    public ColoradoDoraDisplaySelectionStore(IWebHostEnvironment environment)
        : this(Path.Combine(environment.ContentRootPath, "App_Data", "colorado-dora-display-types.json"))
    {
    }

    public ColoradoDoraDisplaySelectionStore(string filePath)
    {
        this.filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(this.filePath)!);
    }

    public IReadOnlySet<string> GetSelected()
    {
        lock (sync)
            return new HashSet<string>(Read(), StringComparer.Ordinal);
    }

    public void Save(IEnumerable<string> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var values = selected.Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var selectable = ColoradoDoraLicenseTypes.All
            .Where(option => option.IsRealEstate).Select(option => option.Name).ToHashSet(StringComparer.Ordinal);
        if (values.Any(value => !selectable.Contains(value)))
            throw new ArgumentException("Only Colorado DORA real-estate license types can be enabled.", nameof(selected));

        lock (sync)
        {
            var nextPath = filePath + ".next";
            File.WriteAllText(nextPath, JsonSerializer.Serialize(values, JsonOptions));
            File.Move(nextPath, filePath, true);
            selectedTypes = values;
        }
    }

    private string[] Read()
    {
        if (selectedTypes is not null) return selectedTypes;
        if (!File.Exists(filePath))
            return selectedTypes = ColoradoDoraLicenseTypes.DefaultEnabled.ToArray();

        var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new InvalidDataException("Colorado DORA display-type configuration is empty.");
        var known = ColoradoDoraLicenseTypes.All.Select(option => option.Name).ToHashSet(StringComparer.Ordinal);
        if (values.Any(value => !known.Contains(value)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Colorado DORA display-type configuration contains unknown or duplicate values.");
        selectedTypes = ColoradoDoraLicenseTypes.EnabledRealEstateTypes(values);
        return selectedTypes;
    }
}
