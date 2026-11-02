using Lms.Application.Services;
using Lms.Domain.Entities;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class StateLicensingReferenceServiceTests
{
    [Fact]
    public void GetStates_ReturnsEveryDevelopmentCatalogState()
    {
        var states = StateLicensingReferenceService.GetStates();

        Assert.Equal(50, states.Count);
        Assert.Equal(DevelopmentCourseCatalog.States.OrderBy(name => name), states.Select(state => state.Name).OrderBy(name => name));
        Assert.Equal(states.Count, states.Select(state => state.Slug).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(states, state =>
        {
            Assert.False(string.IsNullOrWhiteSpace(state.Agency.Name));
            Assert.True(Uri.TryCreate(state.Agency.Url, UriKind.Absolute, out var agencyUri));
            Assert.Equal(Uri.UriSchemeHttps, agencyUri!.Scheme);
        });
    }

    [Theory]
    [InlineData("New Jersey", "New Jersey")]
    [InlineData("new-jersey", "New Jersey")]
    [InlineData("  HAWAII ", "Hawaii")]
    public void FindState_AcceptsStateNameOrSlug(string query, string expectedName)
    {
        var state = StateLicensingReferenceService.FindState(query);

        Assert.NotNull(state);
        Assert.Equal(expectedName, state.Name);
    }

    [Fact]
    public void EveryState_HasSubstantiveCitedAnswersInsteadOfCatalogNotesOrPlaceholders()
    {
        Assert.All(StateLicensingReferenceService.GetStates(), state =>
        {
            Assert.True(state.SourcedAnswerCount >= 5, $"{state.Name} needs at least five source-backed answers.");
            Assert.Equal(state.Questions.Count, state.Questions.Select(question => question.Question).Distinct().Count());
            Assert.All(state.Questions, question =>
            {
                Assert.False(string.IsNullOrWhiteSpace(question.Question));
                Assert.True(question.Answer.Length >= 80, $"{state.Name}: {question.Question} needs a substantive answer.");
                Assert.True(question.IsSourceBacked);
                Assert.Null(question.CatalogCitation);
                Assert.DoesNotContain("has not verified a current answer", question.Answer, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("internal LMS catalog", question.Answer, StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(question.Citations);
                Assert.NotEmpty(question.Citations);
            });
        });
    }

    [Fact]
    public void Citations_ProvideReviewContextAndResolvableSourceReferences()
    {
        var root = FindRepositoryRoot();
        var citations = StateLicensingReferenceService.GetStates()
            .SelectMany(state => state.Questions)
            .SelectMany(question => question.Citations ?? [])
            .ToArray();

        Assert.NotEmpty(citations);
        Assert.All(citations, citation =>
        {
            Assert.False(string.IsNullOrWhiteSpace(citation.Title));
            Assert.False(string.IsNullOrWhiteSpace(citation.CurrencyNote));
            Assert.NotEqual(default, citation.ReviewedOn);
            Assert.True(!string.IsNullOrWhiteSpace(citation.Url) || !string.IsNullOrWhiteSpace(citation.RepositoryPath));
            if (citation.Url is not null)
            {
                Assert.True(Uri.TryCreate(citation.Url, UriKind.Absolute, out var uri), citation.Url);
                Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            }

            if (citation.RepositoryPath is not null)
            {
                Assert.StartsWith("src/docs/", citation.RepositoryPath);
                Assert.DoesNotContain("Development", citation.RepositoryPath, StringComparison.OrdinalIgnoreCase);
                var path = Path.GetFullPath(Path.Combine(root, citation.RepositoryPath.Replace('/', Path.DirectorySeparatorChar)));
                Assert.StartsWith(Path.Combine(root, "src", "docs") + Path.DirectorySeparatorChar, path);
                Assert.True(File.Exists(path), $"Missing cited source: {citation.RepositoryPath}");
            }
        });
    }

    [Fact]
    public void LocalInventory_ContainsOnlyDocumentsActuallyCitedByAnswers()
    {
        Assert.All(StateLicensingReferenceService.GetStates(), state =>
        {
            var citedPaths = state.Questions.SelectMany(question => question.Citations ?? [])
                .Where(citation => citation.RepositoryPath is not null)
                .Select(citation => citation.RepositoryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path)
                .ToArray();
            Assert.Equal(citedPaths, state.Sources.Select(source => source.RepositoryPath).OrderBy(path => path));
        });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-state")]
    public void FindState_InvalidSelectionReturnsNoState(string? query)
    {
        Assert.Null(StateLicensingReferenceService.FindState(query));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Lms.Application")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate the repository for licensing source validation.");
    }
}
