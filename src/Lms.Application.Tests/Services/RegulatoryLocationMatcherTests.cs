using Lms.Application.Services;
using Lms.Domain.Entities;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class RegulatoryLocationMatcherTests
{
    private static readonly RegulatoryLocationMatcher Matcher = new(
    [
        new("1", "Patterson Schwartz Real Estate", "HOCKESSIN"),
        new("2", "Berkshire Hathaway HomeServices Fox and Roach", "HOCKESSIN"),
        new("3", "Mann & Sons Inc.", "REHOBOTH BEACH"),
        new("4", "Lus Realty LLC", "LEWES"),
        new("5", "Century 21 Emerald - Wilmington", "WILMINGTON"),
        new("6", "4 RIVERS REALTY, INC", "BEND"),
        new("7", "Taylor & Taylor Realty Co", "DOVER"),
        new("8", "White Robbins Property Management", "WILMINGTON", IsActive: false),
        new("9", "White Robbins Property Management", "WILMINGTON")
    ]);

    [Theory]
    [InlineData("Patterson Schwartz & Associates, Inc", "1")]
    [InlineData("Berkshire Hathaway Home Services Fox and Roach", "2")]
    [InlineData("White Robbins Inc.", "9")]
    public void Match_LinksNearlyIdenticalOrDistinctiveWordNamesForReview(string branch, string firm)
    {
        Assert.Equal(new RegulatoryLocationMatch(firm, RegulatoryLocationMatchMethods.ClosestName), Matcher.Match(branch, "NEWARK"));
    }

    [Theory]
    [InlineData("Gilpin VanTrump & Montg")]
    [InlineData("Lewes Realty Inc.")]
    [InlineData("Century 21 Emerald - Millsboro")]
    [InlineData("3 RIVERS REALTY, INC.")]
    [InlineData("Taylor Group Realty")]
    public void Match_LeavesWeakNameSimilarityUnlinked(string branch)
    {
        Assert.Equal(new RegulatoryLocationMatch(null, RegulatoryLocationMatchMethods.NoMatch), Matcher.Match(branch, "LEWES"));
    }
}
