namespace Lms.Application.Services;

internal static partial class StateLicensingContent
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<StateLicensingFaq>> Questions =
        FirstHalf().Concat(SecondHalf()).ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<StateLicensingFaq>)Array.AsReadOnly(entry.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<StateLicensingFaq> GetQuestions(string state) => Questions[state];
}
