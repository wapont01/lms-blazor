using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public sealed class RosterRegistrationServiceTests
{
    [Fact]
    public async Task ColoradoDoraSearchHonorsEnabledIndividualTypesAndRejectsUnavailableSelections()
    {
        await using var f = await Fixture.CreateAsync();
        f.Snapshot.State = "Colorado";
        f.Snapshot.ParserVersion = ColoradoDoraBulkImportService.ParserVersion;
        f.Record.State = "Colorado";
        f.Record.LicenseNumber = "FA-001";
        f.Record.LicenseType = "FA";
        f.Record.SourceLicenseType = "Associate Level Real Estate Broker";
        var company = new RegulatoryLicensee
        {
            SnapshotId = f.Snapshot.Id, State = "Colorado", LicenseNumber = "EC-002",
            Name = "Synthetic Realty Company", LicenseType = "EC",
            SourceLicenseType = "Real Estate Company - Employing"
        };
        var mortgageOriginator = new RegulatoryLicensee
        {
            SnapshotId = f.Snapshot.Id, State = "Colorado", LicenseNumber = "MLO-003",
            Name = "Synthetic Mortgage Originator", LicenseType = "MLO",
            SourceLicenseType = "Mortgage Loan Originator"
        };
        f.Db.RegulatoryLicensees.AddRange(company, mortgageOriginator);
        await f.Db.SaveChangesAsync();

        var audit = new AuditLogService(f.Db);
        var service = new RosterRegistrationService(f.Db,
            new UserAccountService(f.Db, audit, emailJourney: f.Options), f.Options, audit,
            coloradoDoraTypeSelection: new TestColoradoTypeSelection(
                "Associate Level Real Estate Broker", "Real Estate Company - Employing", "Mortgage Loan Originator"));

        var results = await service.SearchAsync(new RosterSearch("Colorado", Name: "Synthetic"));
        Assert.Equal(new[] { "EC-002", "FA-001" }, results.Items.Select(item => item.LicenseNumber).Order());
        var coloradoOptions = await service.GetSearchOptionsAsync("Colorado");
        Assert.Equal(new[] { "Associate Level Real Estate Broker", "Real Estate Company - Employing" },
            coloradoOptions.LicenseTypes);
        Assert.Equal(new[] { "Mortgage Loan Originator" }, coloradoOptions.ComingLaterLicenseTypes);
        Assert.Single((await service.SearchAsync(new RosterSearch("Colorado", Name: "Synthetic",
            LicenseType: "Associate Level Real Estate Broker"))).Items);
        Assert.Empty((await service.SearchAsync(new RosterSearch("Colorado", Name: "Synthetic",
            LicenseType: "Mortgage Loan Originator"))).Items);
        Assert.Contains("Professional type: Associate Level Real Estate Broker",
            (await service.GetSelectionAsync(f.Record.Id)).Details.Select(detail => $"{detail.Label}: {detail.Value}"));
        await Assert.ThrowsAsync<RosterRequestException>(() => service.GetSelectionAsync(company.Id));
        await Assert.ThrowsAsync<RosterRequestException>(() => service.GetSelectionAsync(mortgageOriginator.Id));
    }

    [Fact]
    public async Task NevadaNredSearchExposesOnlyRealEstateTypesAndRejectsOtherProfessions()
    {
        await using var f = await Fixture.CreateAsync();
        f.Snapshot.State = "Nevada";
        f.Snapshot.ParserVersion = NevadaNredBulkImportService.ParserVersion;
        f.Record.State = "Nevada";
        f.Record.LicenseNumber = "B.1001";
        f.Record.Name = "Synthetic Nevada Broker";
        var appraiser = new RegulatoryLicensee
        {
            SnapshotId = f.Snapshot.Id,
            State = "Nevada",
            LicenseNumber = "A.2002",
            Name = "Synthetic Nevada Appraiser",
            LicenseType = "Appraiser"
        };
        f.Db.RegulatoryLicensees.Add(appraiser);
        await f.Db.SaveChangesAsync();

        var options = await f.Service.GetSearchOptionsAsync("Nevada");
        Assert.Equal(new[] { "Broker" }, options.LicenseTypes);
        Assert.Equal(new[] { "Appraiser" }, options.ComingLaterLicenseTypes);
        Assert.Equal("B.1001", Assert.Single((await f.Service.SearchAsync(
            new RosterSearch("Nevada", Name: "Synthetic"), captchaToken: "synthetic")).Items).LicenseNumber);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Nevada", Name: "Synthetic",
            LicenseType: "Appraiser"), captchaToken: "synthetic")).Items);
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.GetSelectionAsync(appraiser.Id));
    }

    [Theory]
    [InlineData("B.", "Broker")]
    [InlineData("BS.", "Broker-Salesperson")]
    [InlineData("S.", "Salesperson")]
    public async Task NevadaNumberSearchAcceptsDottedPrefixesButPreservesFullNumber(string prefix, string type)
    {
        await using var f = await Fixture.CreateAsync();
        f.Snapshot.State = "Nevada";
        f.Snapshot.ParserVersion = NevadaNredBulkImportService.ParserVersion;
        f.Record.State = "Nevada";
        f.Record.LicenseNumber = prefix + "0099001";
        f.Record.LicenseType = type;
        await f.Db.SaveChangesAsync();
        Assert.Equal(f.Record.Id, Assert.Single((await f.Service.SearchAsync(
            new RosterSearch("Nevada", "0099001"))).Items).Id);
        Assert.Single((await f.Service.SearchAsync(new RosterSearch("Nevada", (prefix + "0099001").ToLowerInvariant()))).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Nevada", "99001"))).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Nevada", "009900"), captchaToken: "synthetic")).Items);
    }

    [Theory]
    [InlineData("Real Estate Instructor", "Current, Active", false, false, true)]
    [InlineData("Real Estate Instructor", "Current, Inactive", false, false, false)]
    [InlineData("Real Estate Instructor", "Current, Active", true, false, false)]
    [InlineData("Real Estate Instructor", "Current, Active", false, true, false)]
    [InlineData("Broker", "Current, Active", false, false, false)]
    public async Task InstructorContinuityUsesCurrentTypeStatusRestrictionAndExpiration(
        string type, string status, bool restricted, bool expired, bool allowed)
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        f.Record.LicenseType = "Real Estate Instructor";
        f.Record.LicenseStatus = "Active";
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var newer = new RegulatoryDataSnapshot
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = FloridaBulkImportService.ParserVersion,
            ActivatedAtUtc = DateTime.UtcNow.AddMinutes(1), RetrievedAtUtc = DateTime.UtcNow
        };
        f.Db.RegulatoryLicensees.Add(new()
        {
            Snapshot = newer, State = "Florida", LicenseNumber = f.Record.LicenseNumber,
            Name = "Current licensee", LicenseType = type, LicenseStatus = status,
            IsRestricted = restricted, LicenseRenewalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(expired ? -1 : 365)
        });
        await f.Db.SaveChangesAsync();
        Assert.Equal(allowed, await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        Assert.Equal(f.Record.Id, (await f.Db.RosterAccountRequests.SingleAsync()).RegulatoryLicenseeId);
    }

    [Fact]
    public async Task BulkRegistrationAndInstructorAccessSurviveRefreshAndRestoreWithoutRewritingLinks()
    {
        await using var f = await Fixture.CreateAsync();
        using var archive = await BulkTestArchive.CreateAsync();
        var importer = new FloridaBulkImportService(f.Db);
        var firstId = await importer.ImportArchiveAsync(archive.Path);
        var selection = Assert.Single((await f.Service.SearchAsync(new RosterSearch("Florida", "ZH12"))).Items);
        Assert.Equal("Real Estate Instructor", selection.LicenseType);
        var user = await f.Service.RegisterAsync(selection.Id, f.Options.TestRecipient, "Learner123!");
        Assert.Null(user.EmailVerifiedAtUtc);
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        var email = new VerificationEmail();
        var journey = new EmailJourneyService(f.Db, email, f.Options, new AuditLogService(f.Db), TimeProvider.System);
        var verificationId = await journey.SendVerificationAsync(user);
        var token = System.Text.RegularExpressions.Regex.Match(email.Body!, "token=([A-F0-9]+)").Groups[1].Value;
        Assert.NotNull(await journey.ConsumeAsync(verificationId!.Value, EmailJourneyService.VerifyPurpose, token));
        f.Db.ChangeTracker.Clear();
        user = await f.Db.UserAccounts.SingleAsync(account => account.Id == user.Id);
        Assert.True(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        var confirmation = await f.Db.RosterAccountRequests.SingleAsync(request => request.UserAccountId == user.Id);
        var next = new RegulatoryDataSnapshot
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved,
            ParserVersion = FloridaBulkImportService.ParserVersion, ActivatedAtUtc = DateTime.UtcNow.AddSeconds(1),
            RetrievedAtUtc = DateTime.UtcNow
        };
        var instructor = new RegulatoryLicensee
        {
            Snapshot = next, State = "Florida", LicenseNumber = "ZH12", Name = "Refreshed instructor",
            LicenseType = "Real Estate Instructor", LicenseStatus = "Current, Active",
            LicenseRenewalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)
        };
        f.Db.RegulatoryLicensees.Add(instructor);
        await f.Db.SaveChangesAsync();
        Assert.True(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        Assert.Equal(instructor.Id, Assert.Single((await f.Service.SearchAsync(new RosterSearch("Florida", "ZH12"))).Items).Id);
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.GetSelectionAsync(selection.Id));
        instructor.LicenseStatus = "Current, Inactive";
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        // Use an activation in the past so the restore's real UTC timestamp becomes the newest.
        next.ActivatedAtUtc = DateTime.UtcNow.AddMilliseconds(-1);
        await f.Db.SaveChangesAsync();
        var directory = new RegulatoryDirectoryService(f.Db, new AuditLogService(f.Db));
        await directory.RestoreFloridaRosterAsync(firstId, next.Id, f.Admin.Id, "Synthetic weekly recovery");
        Assert.True(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        Assert.Equal(selection.Id, confirmation.RegulatoryLicenseeId);
        Assert.Equal("Confirmed", confirmation.Status);
        Assert.Equal("Learner", user.Role);
        Assert.False(await f.Db.SchoolStaffMembers.AnyAsync(member => member.UserAccountId == user.Id));
    }

    [Fact]
    public async Task SearchCaptcha_FirstThreeFreeThenEverySearchIncludingLegacyAndPagingIsVerified()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.GetAvailableStatesAsync();
        await f.Service.GetSearchOptionsAsync("Florida");
        var search = new RosterSearch("Florida", "FL-1");
        for (var index = 0; index < 3; index++)
            Assert.Single((await f.Service.SearchAsync(search)).Items);
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(search));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(search with { Page = 2 }));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync("Florida", "FL-1"));
        Assert.Single((await f.Service.SearchAsync(search, captchaToken: "synthetic")).Items);
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(search));
        Assert.Single((await f.Service.SearchAsync(search, captchaToken: "synthetic")).Items);
    }

    [Fact]
    public async Task SearchAndSelection_ReturnPublishedDetailsWithoutContactEmail()
    {
        await using var f = await Fixture.CreateAsync();
        f.Record.LicenseIssuedDate = new DateOnly(2020, 1, 2);
        f.Record.OriginalLicenseDate = new DateOnly(2018, 3, 4);
        f.Record.LicenseRenewalDate = new DateOnly(2027, 6, 30);
        f.Record.RelatedLicenseNumber = "RELATED-01";
        var firm = new RegulatoryFirm
        {
            SnapshotId = f.Snapshot.Id, State = "Florida", LicenseNumber = "CQ1",
            LegalName = "Example Realty", Address = "Synthetic firm address"
        };
        f.Db.RegulatoryFirms.Add(firm);
        f.Db.RegulatoryFirmLicenseeAffiliations.Add(new RegulatoryFirmLicenseeAffiliation
        {
            SnapshotId = f.Snapshot.Id, FirmId = firm.Id, LicenseeId = f.Record.Id,
            RelationshipEffectiveDate = new DateOnly(2021, 2, 3),
            ContactEmail = "private@example.invalid"
        });
        await f.Db.SaveChangesAsync();
        var search = Assert.Single((await f.Service.SearchAsync(new RosterSearch("Florida", "FL-1"))).Items);
        var selection = await f.Service.GetSelectionAsync(f.Record.Id);
        Assert.Equal(search.Details, selection.Details);
        Assert.Contains(new RosterDetail("Issued", "2020-01-02"), search.Details);
        Assert.Contains(new RosterDetail("Original license date", "2018-03-04"), search.Details);
        Assert.Contains(new RosterDetail("Renewal / expiration", "2027-06-30"), search.Details);
        Assert.Contains(new RosterDetail("Related license number", "RELATED-01"), search.Details);
        Assert.Contains(new RosterDetail("Brokerage / firm", "Example Realty"), search.Details);
        Assert.Contains(new RosterDetail("Firm address", "Synthetic firm address"), search.Details);
        Assert.DoesNotContain(search.Details, detail => detail.Value.Contains("@"));
        Assert.DoesNotContain(search.Details, detail => detail.Label == "Restriction");
        var user = await f.AddVerifiedUserAsync();
        var originalDisplayName = user.DisplayName;
        var originalLicenseNumber = user.LicenseNumber;
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        f.Db.ChangeTracker.Clear();
        var saved = Assert.Single(await f.Service.GetMineAsync(user.Id));
        Assert.Equal("Confirmed", saved.Status);
        Assert.Equal(selection.Details, saved.Selection.Details);
        Assert.Empty(await f.Service.GetMineAsync(f.Admin.Id));
        var unchangedUser = await f.Db.UserAccounts.SingleAsync(item => item.Id == user.Id);
        Assert.Equal(originalDisplayName, unchangedUser.DisplayName);
        Assert.Equal(originalLicenseNumber, unchangedUser.LicenseNumber);
        firm = await f.Db.RegulatoryFirms.SingleAsync(item => item.Id == firm.Id);
        firm.Address = "09/30/2027";
        await f.Db.SaveChangesAsync();
        var invalidAddress = await f.Service.GetSelectionAsync(f.Record.Id);
        Assert.Contains(new RosterDetail("Firm address", "Unavailable: address field contains a date."), invalidAddress.Details);
        Assert.DoesNotContain(invalidAddress.Details, detail => detail.Value == "09/30/2027");
    }

    [Theory]
    [InlineData("Florida")]
    [InlineData("North Carolina")]
    [InlineData("Texas")]
    [InlineData("Puerto Rico")]
    public async Task NumberSearch_AllowsOmittedPrefixInEveryStateButNotPartialNumbers(string state)
    {
        await using var f = await Fixture.CreateAsync();
        f.Snapshot.State = state;
        f.Db.RegulatoryLicensees.AddRange(
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = state, Name = "First", LicenseNumber = "BK0012345", LicenseType = "Broker" },
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = state, Name = "Second", LicenseNumber = "sl0012345", LicenseType = "Salesperson" },
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = state, Name = "Numeric", LicenseNumber = "90012345" },
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = state, Name = "Mixed prefix", LicenseNumber = "B90012345" });
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, (await f.Service.SearchAsync(new RosterSearch(state, "0012345"))).Total);
        Assert.Single((await f.Service.SearchAsync(new RosterSearch(state, " bk0012345 "))).Items);
        Assert.Single((await f.Service.SearchAsync(new RosterSearch(state, "0012345", LicenseType: "Salesperson"))).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch(state, "12345"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch(state, "00123"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch(state, "XX0012345"), captchaToken: "synthetic")).Items);
        Assert.Equal(2, (await f.Service.SearchAsync(state, "0012345", captchaToken: "synthetic")).Count);
    }

    [Fact]
    public async Task TexasNumberSearchPreservesLeadingZerosAndAcceptsSourceSuffixSpacingWithoutPartialMatches()
    {
        await using var f = await Fixture.CreateAsync();
        f.Snapshot.State = "Texas";
        f.Db.RegulatoryLicensees.AddRange(
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = "Texas", Name = "Texas broker", LicenseNumber = "001234-B", LicenseType = "Broker" },
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = "Texas", Name = "Texas agent", LicenseNumber = "001234 -SA", LicenseType = "Sales Agent" },
            new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = "Texas", Name = "Different numeric identity", LicenseNumber = "9001234-SA", LicenseType = "Sales Agent" });
        await f.Db.SaveChangesAsync();
        Assert.Equal(2, (await f.Service.SearchAsync(new RosterSearch("Texas", "001234"))).Total);
        Assert.Equal("001234 -SA", Assert.Single((await f.Service.SearchAsync(new RosterSearch("Texas", "001234-sa"))).Items).LicenseNumber);
        Assert.Equal("001234-B", Assert.Single((await f.Service.SearchAsync(new RosterSearch("Texas", "001234-B"))).Items).LicenseNumber);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Texas", "1234"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Texas", "234-SA"), captchaToken: "synthetic")).Items);
    }

    [Fact]
    public async Task AvailableStatesAndFilters_FollowApprovedImportedRosters()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Equal("Florida", Assert.Single(await f.Service.GetAvailableStatesAsync()));
        var texas = new RegulatoryDataSnapshot
        {
            State = "Texas", Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow
        };
        var empty = new RegulatoryDataSnapshot { State = "Alaska", Status = RegulatorySnapshotStatuses.Approved };
        var pending = new RegulatoryDataSnapshot { State = "Georgia", Status = RegulatorySnapshotStatuses.PendingReview };
        f.Db.RegulatoryDataSnapshots.AddRange(texas, empty, pending);
        f.Db.RegulatoryLicensees.AddRange(
            new RegulatoryLicensee { SnapshotId = texas.Id, State = "Texas", Name = "Example Student", LicenseNumber = "TX123", LicenseType = "Sales Agent" },
            new RegulatoryLicensee { SnapshotId = pending.Id, State = "Georgia", Name = "Pending Student", LicenseNumber = "GA123" });
        await f.Db.SaveChangesAsync();
        Assert.Equal(new[] { "Florida", "Texas" }, await f.Service.GetAvailableStatesAsync());
        Assert.Equal("Sales Agent", Assert.Single((await f.Service.GetSearchOptionsAsync("Texas")).LicenseTypes));
        Assert.Single((await f.Service.SearchAsync(new RosterSearch("Texas", Name: "example", LicenseType: "Sales Agent"))).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Texas", Name: "example", LicenseType: "Broker"))).Items);
        f.Options.Enabled = false;
        Assert.Empty(await f.Service.GetAvailableStatesAsync());
    }

    [Fact]
    public async Task NameSearch_UsesWordPrefixIndexKeptInSyncWithRoster()
    {
        await using var f = await Fixture.CreateAsync();
        var nona = new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = "Florida", LicenseNumber = "IX-1", Name = "LISITZA, NONA", LicenseType = "Broker" };
        var jose = new RegulatoryLicensee { SnapshotId = f.Snapshot.Id, State = "Florida", LicenseNumber = "IX-2", Name = "José O'Brien-Núñez", LicenseType = "Broker" };
        f.Db.RegulatoryLicensees.AddRange(nona, jose);
        await f.Db.SaveChangesAsync();

        async Task<int> Count(string name) =>
            (await f.Service.SearchAsync(new RosterSearch("Florida", Name: name), captchaToken: "synthetic")).Total;

        Assert.Equal(1, await Count("Nona Lisitza"));
        Assert.Equal(1, await Count("lisit no"));
        Assert.Equal(1, await Count("Lisitza, Nona"));
        Assert.Equal(0, await Count("sitza"));
        Assert.Equal(0, await Count("Nona Smith"));
        Assert.Equal(1, await Count("jose brien"));
        Assert.Equal(1, await Count("Jose O'Brien Nunez"));
        Assert.Equal(0, await Count("\"nona\" OR*"));
        Assert.Equal(0, await Count("Unapproved"));
        Assert.Equal(0, await Count("Old Old"));

        nona.Name = "LISITZA-SMITH, NONA";
        await f.Db.SaveChangesAsync();
        Assert.Equal(1, await Count("Nona Smith"));
        f.Db.RegulatoryLicensees.Remove(nona);
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, await Count("Nona"));
    }

    [Fact]
    public void NameMatchExpression_KeepsOnlyQuotedWordPrefixes()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Assert.Equal("Name:\"NONA\"* Name:\"LISITZA\"* Scope:\"S0F8FAD5BD9CB469FA16570867728950E\"",
            RegulatoryLicenseeNameIndex.BuildMatchExpression("  nona, Lisitza ", id));
        Assert.StartsWith("Name:\"O\"* Name:\"BRIEN\"* Scope:", RegulatoryLicenseeNameIndex.BuildMatchExpression("O'Brien \"*", id));
        Assert.Null(RegulatoryLicenseeNameIndex.BuildMatchExpression("%%% -- ()", id));
    }

    [Fact]
    public async Task ExtendedSearch_IsStateGatedCombinedLiteralAndPaged()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(new RosterSearch("", Name: "Synthetic")));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(new RosterSearch("Florida", Name: "Sy")));
        for (var i = 0; i < 24; i++)
            f.Db.RegulatoryLicensees.Add(new RegulatoryLicensee
            {
                SnapshotId = f.Snapshot.Id, State = "Florida", LicenseNumber = $"EX-{i:D2}",
                Name = $"Synthetic Person {i:D2}", LicenseType = "SL", LicenseStatus = "Inactive"
            });
        await f.Db.SaveChangesAsync();
        var first = await f.Service.SearchAsync(new RosterSearch("Florida", Name: "synthetic"));
        var second = await f.Service.SearchAsync(new RosterSearch("Florida", Name: "synthetic", Page: 2));
        Assert.Equal(25, first.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.Equal(2, (await f.Service.SearchAsync(new RosterSearch("Florida", Name: "Synthetic", Page: 99))).Page);
        Assert.Equal(24, (await f.Service.SearchAsync(new RosterSearch("Florida", Name: "Synthetic", LicenseType: "SL"), captchaToken: "synthetic")).Total);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Florida", "FL-1", "Person"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Florida", Name: "%%%"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("North Carolina", Name: "Synthetic"), captchaToken: "synthetic")).Items);
        Assert.Null((await f.Service.SearchAsync(new RosterSearch("Florida", "FL-1"), captchaToken: "synthetic")).Items.Single().LicenseStatus);
    }

    [Fact]
    public async Task SearchOptionsAndFirmFilter_UseOnlyCurrentStateSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var firm = new RegulatoryFirm
        {
            SnapshotId = f.Snapshot.Id, State = "Florida", LicenseNumber = "CQ1",
            LegalName = "Example Realty", TradeName = "Coastal Homes"
        };
        f.Db.RegulatoryFirms.Add(firm);
        f.Db.RegulatoryFirmLicenseeAffiliations.Add(new RegulatoryFirmLicenseeAffiliation
        {
            SnapshotId = f.Snapshot.Id, FirmId = firm.Id, LicenseeId = f.Record.Id
        });
        var nc = new RegulatoryDataSnapshot
        {
            State = "North Carolina", Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow
        };
        f.Db.RegulatoryDataSnapshots.Add(nc);
        f.Db.RegulatoryLicensees.Add(new RegulatoryLicensee
        {
            SnapshotId = nc.Id, State = "North Carolina", Name = "Synthetic NC", LicenseNumber = "00123", LicenseType = "BROKER"
        });
        await f.Db.SaveChangesAsync();
        var flOptions = await f.Service.GetSearchOptionsAsync("Florida");
        Assert.True(flOptions.HasFirmAffiliations);
        Assert.Equal("Broker", Assert.Single(flOptions.LicenseTypes));
        var ncOptions = await f.Service.GetSearchOptionsAsync("North Carolina");
        Assert.False(ncOptions.HasFirmAffiliations);
        Assert.Equal("BROKER", Assert.Single(ncOptions.LicenseTypes));
        Assert.Single((await f.Service.SearchAsync(new RosterSearch("Florida", Name: "Synthetic", Firm: "coastal"))).Items);
        Assert.Single((await f.Service.SearchAsync(new RosterSearch("Florida", Name: "Synthetic", Firm: "realty"))).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("Florida", Name: "Synthetic", Firm: "missing"))).Items);
        Assert.Single((await f.Service.SearchAsync(new RosterSearch("North Carolina", "00123"), captchaToken: "synthetic")).Items);
        Assert.Empty((await f.Service.SearchAsync(new RosterSearch("North Carolina", "123"), captchaToken: "synthetic")).Items);
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.SearchAsync(new RosterSearch("North Carolina", Name: "Synthetic", Firm: "realty")));
        f.Options.Enabled = false;
        Assert.Empty((await f.Service.GetSearchOptionsAsync("Florida")).LicenseTypes);
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SearchAsync(new RosterSearch("Florida", "FL-1")));
    }

    [Fact]
    public async Task Search_RequiresStateAndExactLicense_UsesLatestApprovedSnapshotAndSafeSource()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Empty(await f.Service.SearchAsync("", "FL-1"));
        Assert.Empty(await f.Service.SearchAsync("Florida", ""));
        Assert.Empty(await f.Service.SearchAsync("Florida", "FL"));
        Assert.Empty(await f.Service.SearchAsync("North Carolina", "FL-1"));
        var found = Assert.Single(await f.Service.SearchAsync("Florida", " fl-1 ", captchaToken: "synthetic"));
        Assert.Equal(f.Record.Id, found.Id);
        Assert.Equal("https://example.invalid/roster", found.SourceUrl);
        f.Snapshot.SourceUrl = "javascript:alert(1)";
        await f.Db.SaveChangesAsync();
        Assert.Null(Assert.Single(await f.Service.SearchAsync("Florida", "FL-1", captchaToken: "synthetic")).SourceUrl);
        f.Options.Enabled = false;
        Assert.Empty(await f.Service.SearchAsync("Florida", "FL-1", captchaToken: "synthetic"));
    }

    [Fact]
    public async Task Registration_PersistsPendingRequestWithoutCopyingRosterIdentityOrLinking()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.Service.RegisterAsync(f.Record.Id, f.Options.TestRecipient, "SyntheticAccount123!");
        Assert.True(user.EmailTwoStepEnabled);
        Assert.Null(user.EmailVerifiedAtUtc);
        Assert.Null(user.LicenseNumber);
        Assert.Null(user.State);
        Assert.NotEqual(f.Record.Name, user.DisplayName);
        var request = Assert.Single(await f.Service.GetMineAsync(user.Id));
        Assert.Equal("Pending", request.Status);
        Assert.False(request.EmailVerified);
        Assert.Empty(await f.Db.UserRegulatoryLicenseLinks.ToListAsync());
        Assert.Empty(await f.Db.CompletionCertificates.ToListAsync());
        Assert.Empty(await f.Service.GetMineAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.RegisterAsync(f.Record.Id, f.Options.TestRecipient, "SyntheticAccount123!"));
        Assert.Single(await f.Db.RosterAccountRequests.ToListAsync());
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.ReviewAsync(f.Admin.Id, request.Id, true, "Test review."));
    }

    [Fact]
    public async Task InvalidSelectionOrPassword_DoesNotLeaveAnAccountOrRequest()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.RegisterAsync(Guid.NewGuid(), f.Options.TestRecipient, "SyntheticAccount123!"));
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.RegisterAsync(f.Record.Id, f.Options.TestRecipient, "short"));
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.RegisterAsync(f.Record.Id, "someone@example.invalid", "SyntheticAccount123!"));
        Assert.False(await f.Db.UserAccounts.AnyAsync(item => item.Email == f.Options.TestRecipient));
        Assert.Empty(await f.Db.RosterAccountRequests.ToListAsync());
    }

    [Fact]
    public async Task VerifiedSubmission_IsConfirmedWithoutAdminApproval_AndDoesNotGrantOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var request = Assert.Single(await f.Service.GetMineAsync(user.Id));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SubmitAsync(user.Id, f.Record.Id));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.GetReviewPageAsync(user.Id, "Florida", "", 1));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.ReviewAsync(user.Id, request.Id, true, "Not authorized."));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.ReviewAsync(f.Admin.Id, request.Id, true, " "));
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.ReviewAsync(f.Admin.Id, request.Id, true, "Routine approval is no longer required."));
        Assert.Equal("Confirmed", Assert.Single(await f.Service.GetMineAsync(user.Id)).Status);
        Assert.Empty(await f.Db.UserRegulatoryLicenseLinks.ToListAsync());
        Assert.Null((await f.Db.UserAccounts.AsNoTracking().SingleAsync(item => item.Id == user.Id)).LicenseNumber);
        var persisted = await f.Db.RosterAccountRequests.AsNoTracking().SingleAsync();
        Assert.Null(persisted.ReviewedByUserId);
        Assert.Null(persisted.ReviewedAtUtc);
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), item => item.Action == "roster-request.confirmed");
    }

    [Fact]
    public async Task RestrictedStaleAndUnverifiedSelectionsAreRejected_AndAdminMayRejectRequest()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        f.Record.IsRestricted = true;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SubmitAsync(user.Id, f.Record.Id));
        f.Record.IsRestricted = false;
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var request = Assert.Single(await f.Service.GetMineAsync(user.Id));
        f.Snapshot.Status = RegulatorySnapshotStatuses.Rejected;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.ReviewAsync(f.Admin.Id, request.Id, true, "Snapshot invalid."));
        await f.Service.ReviewAsync(f.Admin.Id, request.Id, false, "Snapshot no longer approved.");
        Assert.Equal("Rejected", Assert.Single(await f.Service.GetMineAsync(user.Id)).Status);
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.ReviewAsync(f.Admin.Id, request.Id, false, "Duplicate rejection."));
        Assert.Contains(await f.Db.AuditLogs.ToListAsync(), item => item.Action == "roster-request.rejected");
    }

    [Fact]
    public async Task AssociationList_IsStateGatedFilteredAndBoundedToTwenty()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        for (var i = 0; i < 25; i++)
        {
            var record = new RegulatoryLicensee
            {
                SnapshotId = f.Snapshot.Id, State = "Florida", Name = $"Synthetic {i}", LicenseNumber = $"B{i}", LicenseType = "Broker"
            };
            f.Db.RegulatoryLicensees.Add(record);
            f.Db.RosterAccountRequests.Add(new RosterAccountRequest { UserAccountId = user.Id, RegulatoryLicenseeId = record.Id, Status = "Confirmed" });
        }
        await f.Db.SaveChangesAsync();
        Assert.Empty((await f.Service.GetReviewPageAsync(f.Admin.Id, "", "", 1)).Items);
        var first = await f.Service.GetReviewPageAsync(f.Admin.Id, "Florida", "Confirmed", 1);
        var last = await f.Service.GetReviewPageAsync(f.Admin.Id, "Florida", "Confirmed", 99);
        Assert.Equal(25, first.Total);
        Assert.Equal(20, first.Items.Count);
        Assert.Equal(5, last.Items.Count);
        Assert.Equal(2, last.Page);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(last.Items.Select(item => item.Id)));
        Assert.Empty((await f.Service.GetReviewPageAsync(f.Admin.Id, "North Carolina", "", 1)).Items);
        Assert.Empty((await f.Service.GetReviewPageAsync(f.Admin.Id, "Florida", "Rejected", 1)).Items);
    }

    [Fact]
    public async Task PolicyMigration_ConfirmsExistingVerifiedSelectionsButPreservesUnverifiedAndRejected()
    {
        await using var f = await Fixture.CreateAsync();
        var verified = await f.AddVerifiedUserAsync();
        var unverified = new UserAccount { Email = "unverified@example.invalid", Role = "Learner", IsActive = true };
        f.Db.UserAccounts.Add(unverified);
        var requests = new[]
        {
            new RosterAccountRequest { UserAccountId = verified.Id, RegulatoryLicenseeId = f.Record.Id },
            new RosterAccountRequest { UserAccountId = verified.Id, RegulatoryLicenseeId = f.Record.Id, Status = "Approved" },
            new RosterAccountRequest { UserAccountId = verified.Id, RegulatoryLicenseeId = f.Record.Id, Status = "Rejected" },
            new RosterAccountRequest { UserAccountId = unverified.Id, RegulatoryLicenseeId = f.Record.Id }
        };
        // Use distinct credentials to preserve the unique student/record constraint.
        foreach (var request in requests)
        {
            var record = new RegulatoryLicensee
            {
                SnapshotId = f.Snapshot.Id, State = "Florida", Name = "Synthetic",
                LicenseNumber = Guid.NewGuid().ToString(), LicenseType = "Broker"
            };
            f.Db.RegulatoryLicensees.Add(record);
            request.RegulatoryLicenseeId = record.Id;
        }
        f.Db.RosterAccountRequests.AddRange(requests);
        await f.Db.SaveChangesAsync();
        var migration = new Lms.Application.Data.Migrations.ConfirmStudentRosterAssociations();
        var operation = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            Assert.Single(migration.UpOperations));
        await f.Db.Database.ExecuteSqlRawAsync(operation.Sql);
        f.Db.ChangeTracker.Clear();
        var rows = await f.Db.RosterAccountRequests.ToDictionaryAsync(item => item.Id);
        Assert.Equal("Confirmed", rows[requests[0].Id].Status);
        Assert.Equal("Confirmed", rows[requests[1].Id].Status);
        Assert.Equal("Rejected", rows[requests[2].Id].Status);
        Assert.Equal("Pending", rows[requests[3].Id].Status);
        Assert.Empty(await f.Db.UserRegulatoryLicenseLinks.ToListAsync());
    }

    [Fact]
    public async Task ExistingVerifiedStudent_CanConfirmWithoutPilotInboxAndRetainsFullDetails()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Email = "existing-student@example.invalid";
        f.Record.LicenseIssuedDate = new DateOnly(2020, 1, 2);
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        f.Db.ChangeTracker.Clear();
        var saved = Assert.Single(await f.Service.GetMineAsync(user.Id));
        Assert.Equal("Confirmed", saved.Status);
        Assert.Contains(new RosterDetail("Issued", "2020-01-02"), saved.Selection.Details);
        Assert.Null((await f.Db.RosterAccountRequests.SingleAsync()).ReviewedAtUtc);
    }

    [Fact]
    public async Task VerifiedInstructor_CanConfirmLicenseWithoutChangingRoleOrGrantingFacultyOwnership()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Role = "Instructor";
        user.Email = "existing-instructor@example.invalid";
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var saved = Assert.Single(await f.Service.GetMineAsync(user.Id));
        Assert.Equal("Confirmed", saved.Status);
        Assert.True(saved.EmailVerified);
        Assert.Equal("Instructor", (await f.Db.UserAccounts.SingleAsync(u => u.Id == user.Id)).Role);
        Assert.False(await f.Db.SchoolStaffMembers.AnyAsync(m => m.UserAccountId == user.Id));
    }

    [Fact]
    public async Task UnverifiedInstructor_CannotConfirmLicense()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Role = "Instructor";
        user.EmailVerifiedAtUtc = null;
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SubmitAsync(user.Id, f.Record.Id));
        Assert.Empty(await f.Db.RosterAccountRequests.ToListAsync());
    }

    [Theory]
    [InlineData("Broker", "Real Estate Instructor", "Current,Active")]
    [InlineData("Broker", "ZH", "Current, Active")]
    [InlineData("Learner", "Instructor", "Active")]
    public async Task ConfirmInstructorLicense_AddsAccessWithoutReplacingExistingRoleOrLicense(
        string role, string licenseType, string status)
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Role = role;
        user.LicenseNumber = "ORIGINAL-BROKERAGE-LICENSE";
        f.Record.LicenseType = licenseType;
        f.Record.LicenseStatus = status;
        f.Record.LicenseRenewalDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));

        await f.Service.SubmitAsync(user.Id, f.Record.Id);

        Assert.True(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        var account = await f.Db.UserAccounts.AsNoTracking().SingleAsync(account => account.Id == user.Id);
        Assert.Equal(role, account.Role);
        Assert.Equal("ORIGINAL-BROKERAGE-LICENSE", account.LicenseNumber);
        Assert.Equal("Confirmed", Assert.Single(await f.Service.GetMineAsync(user.Id)).Status);
        Assert.False(await f.Db.SchoolStaffMembers.AnyAsync(member => member.UserAccountId == user.Id));

        var request = await f.Db.RosterAccountRequests.SingleAsync();
        await f.Service.ReviewAsync(f.Admin.Id, request.Id, false, "Wrong license selected.");
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        Assert.Equal(role, (await f.Db.UserAccounts.FindAsync(user.Id))!.Role);
    }

    [Theory]
    [InlineData("Broker", "Active", "Confirmed", false, false)]
    [InlineData("Instructor", "Inactive", "Confirmed", false, false)]
    [InlineData("Instructor", "Current,Inactive", "Confirmed", false, false)]
    [InlineData("Instructor", null, "Confirmed", false, false)]
    [InlineData("Instructor applicant", "Active", "Confirmed", false, false)]
    [InlineData("Instructor", "Active", "Pending", false, false)]
    [InlineData("Instructor", "Active", "Rejected", false, false)]
    [InlineData("Instructor", "Active", "Confirmed", true, false)]
    [InlineData("Instructor", "Active", "Confirmed", false, true)]
    public async Task NonqualifyingLicense_DoesNotEnableInstructorTools(
        string type, string? status, string association, bool restricted, bool expired)
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Role = "Broker";
        f.Record.LicenseType = type;
        f.Record.LicenseStatus = status;
        f.Record.IsRestricted = restricted;
        f.Record.LicenseRenewalDate = expired ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1) : null;
        f.Db.RosterAccountRequests.Add(new()
        {
            UserAccountId = user.Id, RegulatoryLicenseeId = f.Record.Id, Status = association
        });
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
    }

    [Fact]
    public async Task SupplementalInstructorAccess_RequiresVerifiedEmailAndCurrentApprovedSnapshot()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Role = "Broker";
        f.Record.LicenseType = "Instructor";
        f.Record.LicenseStatus = "Active";
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        Assert.True(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        user.EmailVerifiedAtUtc = null;
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        f.Snapshot.Status = RegulatorySnapshotStatuses.Rejected;
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
        f.Snapshot.Status = RegulatorySnapshotStatuses.Approved;
        f.Db.RegulatoryDataSnapshots.Add(new()
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = f.Snapshot.RetrievedAtUtc.AddDays(1)
        });
        await f.Db.SaveChangesAsync();
        Assert.False(await InstructorAccess.Accounts(f.Db).AnyAsync(account => account.Id == user.Id));
    }

    [Fact]
    public async Task UnverifiedStudent_CannotSubmitUntilEmailVerified_ThenConfirmsWithoutApproval()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        user.Email = "pending-student@example.invalid";
        user.EmailVerifiedAtUtc = null;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.SubmitAsync(user.Id, f.Record.Id));
        Assert.Equal("Verify your account email before saving a license record.", error.Message);
        Assert.Empty(await f.Service.GetMineAsync(user.Id));
        Assert.Empty(await f.Db.RosterAccountRequests.ToListAsync());
        Assert.Null((await f.Db.UserAccounts.AsNoTracking().SingleAsync(item => item.Id == user.Id)).EmailVerifiedAtUtc);
        user.EmailVerifiedAtUtc = DateTime.UtcNow;
        await f.Db.SaveChangesAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var saved = Assert.Single(await f.Service.GetMineAsync(user.Id));
        Assert.Equal("Confirmed", saved.Status);
        Assert.True(saved.EmailVerified);
        Assert.Null((await f.Db.RosterAccountRequests.SingleAsync()).ReviewedByUserId);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("inactive")]
    [InlineData("non-learner")]
    public async Task Submission_RejectsIneligibleLearnerWithoutSavingRequest(string reason)
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        switch (reason)
        {
            case "disabled": f.Options.Enabled = false; break;
            case "inactive": user.IsActive = false; break;
            case "non-learner": user.Role = "Admin"; break;
        }
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SubmitAsync(user.Id, f.Record.Id));
        Assert.Empty(await f.Db.RosterAccountRequests.ToListAsync());
    }

    [Fact]
    public async Task NewApprovedSnapshot_PreventsSubmissionAndApprovalOfOldSelection()
    {
        await using var f = await Fixture.CreateAsync();
        var user = await f.AddVerifiedUserAsync();
        await f.Service.SubmitAsync(user.Id, f.Record.Id);
        var request = Assert.Single(await f.Service.GetMineAsync(user.Id));
        f.Db.RegulatoryDataSnapshots.Add(new RegulatoryDataSnapshot
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved,
            RetrievedAtUtc = f.Snapshot.RetrievedAtUtc.AddDays(1),
            SourceName = "Replacement synthetic roster"
        });
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Service.SearchAsync("Florida", f.Record.LicenseNumber));
        await Assert.ThrowsAsync<RosterRequestException>(() => f.Service.SubmitAsync(user.Id, f.Record.Id));
        await Assert.ThrowsAsync<RosterRequestException>(() =>
            f.Service.ReviewAsync(f.Admin.Id, request.Id, true, "Old snapshot."));
        Assert.Equal("Confirmed", Assert.Single(await f.Service.GetMineAsync(user.Id)).Status);
    }

    private sealed class VerificationEmail : IEmailService
    {
        public string? Body { get; private set; }
        public Task<bool> SendAsync(EmailMessage message) { Body = message.HtmlBody; return Task.FromResult(true); }
        public Task<bool> SendReceiptAsync(string recipientEmail, string invoiceNumber, decimal amount, List<string> courseNames) =>
            throw new NotSupportedException();
    }

    private sealed class TestColoradoTypeSelection(params string[] selected) : IColoradoDoraLicenseTypeSelection
    {
        public IReadOnlySet<string> GetSelected() => selected.ToHashSet(StringComparer.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public ApplicationDbContext Db { get; }
        public EmailJourneyOptions Options { get; } = new() { Enabled = true, TestRecipient = "roster-test@example.invalid" };
        public UserAccount Admin { get; } = new() { Email = "admin@example.invalid", Role = "Admin", DisplayName = "Admin", IsActive = true };
        public RegulatoryDataSnapshot Snapshot { get; } = new()
        {
            State = "Florida", Status = RegulatorySnapshotStatuses.Approved, SourceName = "Synthetic source",
            SourceUrl = "https://example.invalid/roster", RetrievedAtUtc = DateTime.UtcNow
        };
        public RegulatoryLicensee Record { get; }
        public RosterRegistrationService Service { get; }
        private Fixture(SqliteConnection connection, ApplicationDbContext db)
        {
            _connection = connection; Db = db;
            Record = new() { SnapshotId = Snapshot.Id, State = "Florida", Name = "Synthetic Roster Identity", LicenseNumber = "FL-1", LicenseType = "Broker" };
            var audit = new AuditLogService(db);
            Service = new(db, new UserAccountService(db, audit, emailJourney: Options), Options, audit,
                new RosterSearchProtection(visitorLimit: 100, globalLimit: 200),
                new RosterSearchSession(new SyntheticVerifier()));
        }
        private sealed class SyntheticVerifier : IRosterCaptchaVerifier
        {
            public Task VerifyAsync(string? token, CancellationToken cancellationToken)
            {
                if (token != "synthetic") throw new RosterRequestException("Missing synthetic CAPTCHA.");
                return Task.CompletedTask;
            }
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            await RegulatoryLicenseeNameIndex.EnsureAsync(db);
            var fixture = new Fixture(connection, db);
            db.UserAccounts.Add(fixture.Admin);
            db.RegulatoryDataSnapshots.Add(fixture.Snapshot);
            db.RegulatoryLicensees.Add(fixture.Record);
            var older = new RegulatoryDataSnapshot
            {
                State = "Florida", Status = RegulatorySnapshotStatuses.Approved, RetrievedAtUtc = DateTime.UtcNow.AddDays(-1)
            };
            var pending = new RegulatoryDataSnapshot { State = "Florida", RetrievedAtUtc = DateTime.UtcNow.AddDays(1) };
            db.RegulatoryDataSnapshots.AddRange(older, pending);
            db.RegulatoryLicensees.AddRange(
                new RegulatoryLicensee { SnapshotId = older.Id, State = "Florida", LicenseNumber = "FL-1", Name = "Old", LicenseType = "Broker" },
                new RegulatoryLicensee { SnapshotId = pending.Id, State = "Florida", LicenseNumber = "FL-1", Name = "Unapproved", LicenseType = "Broker" });
            await db.SaveChangesAsync();
            return fixture;
        }
        public async Task<UserAccount> AddVerifiedUserAsync()
        {
            var user = new UserAccount
            {
                Email = Options.TestRecipient, DisplayName = "Synthetic Learner", EmailVerifiedAtUtc = DateTime.UtcNow, EmailTwoStepEnabled = true
            };
            Db.UserAccounts.Add(user);
            await Db.SaveChangesAsync();
            return user;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
