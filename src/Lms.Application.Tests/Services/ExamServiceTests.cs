using Lms.Application.Data;
using Lms.Application.Services;
using Lms.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.Application.Tests.Services;

public class ExamServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentSubmission_EnforcesReplayAndAttemptLimits(bool sameSession)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lms-exam-{Guid.NewGuid():N}.db");
        try
        {
            await using var fixture = await TestFixture.CreateAsync(path);
            var user = await fixture.DbContext.UserAccounts.SingleAsync(x => x.Email == "william.guest@wlr.com");
            var graph = await fixture.CreateCourseGraphWithEnrollmentAsync(user.Id, "Concurrent exam");
            var exam = await fixture.ExamService.GetCourseExamAsync(graph.CourseId);
            var storedExam = await fixture.DbContext.CourseExams.SingleAsync(x => x.Id == exam!.Id);
            storedExam.MaxRetakesPerLearner = 0;
            await fixture.DbContext.SaveChangesAsync();
            var answers = await fixture.ExamService.GetCorrectAnswerMapAsync(graph.CourseId);
            var startedAt = DateTime.UtcNow.AddMinutes(-1);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
            {
                await start.Task;
                await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite($"Data Source={path};Pooling=False;Default Timeout=5").Options);
                try
                {
                    await new ExamService(db, new AuditLogService(db))
                        .SubmitAttemptForExamAsync(exam!.Id, user.Id, answers,
                            sameSession ? startedAt : startedAt.AddSeconds(index));
                    return true;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("already been submitted")
                    || ex.Message.Contains("Retake limit reached"))
                {
                    return false;
                }
            })).ToArray();
            start.SetResult();
            var results = await Task.WhenAll(work);
            Assert.Single(results, value => value);
            Assert.Single(results, value => !value);
            Assert.Equal(1, await fixture.DbContext.ExamAttempts.CountAsync(x => x.CourseExamId == exam!.Id));
            Assert.Equal(answers.Count, await fixture.DbContext.ExamAnswers.CountAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EnsureDefaultExamForCourseAsync_CreatesTenQuestionFinalExam()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Ten Question Course");

        var exam = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);

        Assert.NotNull(exam);
        Assert.Equal(10, exam!.Questions.Count);

        var correctAnswers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        Assert.Equal(10, correctAnswers.Count);
    }

    [Fact]
    public async Task GetCourseMenuExamsAsync_ReturnsExamSummariesForAllCourseExamTypes()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Course Menu Exam Summary Course");
        var unit = await fixture.DbContext.Units.SingleAsync(existing => existing.CourseId == seed.CourseId);
        await fixture.ExamService.GetCourseExamAsync(seed.CourseId);

        fixture.DbContext.CourseExams.AddRange(
            new CourseExam
            {
                CourseId = seed.CourseId,
                UnitId = unit.Id,
                Kind = CourseExamKinds.Standard,
                Title = "Unit Exam"
            },
            new CourseExam
            {
                CourseId = seed.CourseId,
                Kind = CourseExamKinds.Practice,
                Title = "Practice Exam",
                IsRequired = false
            });
        await fixture.DbContext.SaveChangesAsync();

        var exams = await fixture.ExamService.GetCourseMenuExamsAsync(seed.CourseId);

        Assert.Contains(exams, exam => exam.Kind == CourseExamKinds.Standard && exam.UnitId == unit.Id);
        Assert.Contains(exams, exam => exam.Kind == CourseExamKinds.Standard && exam.UnitId is null);
        Assert.Contains(exams, exam => exam.Kind == CourseExamKinds.Practice && exam.UnitId is null);
    }

    [Fact]
    public async Task CreateUnitExamAsync_CreatesEditableRequiredExamForSelectedUnit()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Editable Unit Exam Course");
        var unit = await fixture.DbContext.Units.SingleAsync(existing => existing.CourseId == seed.CourseId);
        await fixture.ExamService.GetCourseExamAsync(seed.CourseId);

        var unitExam = await fixture.ExamService.CreateUnitExamAsync(seed.CourseId, unit.Id, admin.Id, admin.Email);
        Assert.Equal(CourseExamKinds.Standard, unitExam.Kind);
        Assert.Equal(unit.Id, unitExam.UnitId);

        var editor = await fixture.ExamService.GetExamEditorByIdAsync(unitExam.Id);
        Assert.NotNull(editor);
        editor!.Questions.Add(new ExamQuestionEditorItem
        {
            Prompt = "Which answer is correct?",
            OptionA = "A",
            OptionB = "B",
            OptionC = "C",
            OptionD = "D",
            CorrectOption = "C"
        });
        await fixture.ExamService.SaveExamEditorByIdAsync(unitExam.Id, editor, admin.Id, admin.Email);

        var summaries = await fixture.ExamService.GetCourseMenuExamsAsync(seed.CourseId);
        Assert.Contains(summaries, exam => exam.Id == unitExam.Id && exam.QuestionCount == 1);
        Assert.NotNull(await fixture.ExamService.GetCourseExamAsync(seed.CourseId, unit.Id));
    }

    [Fact]
    public async Task EnsureDefaultExamForCourseAsync_PreservesAuthoredFinalExamContent()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Authored Final Exam Course");
        var exam = await fixture.DbContext.CourseExams
            .Include(existing => existing.Questions)
            .SingleAsync(existing => existing.CourseId == seed.CourseId && existing.UnitId == null);
        exam.Questions.First().Prompt = "Course-specific authored question";
        await fixture.DbContext.SaveChangesAsync();

        await fixture.ExamService.EnsureDefaultExamForCourseAsync(seed.CourseId);

        var persistedPrompt = await fixture.DbContext.CourseExams
            .Where(existing => existing.Id == exam.Id)
            .SelectMany(existing => existing.Questions.OrderBy(question => question.OrderIndex).Take(1))
            .Select(question => question.Prompt)
            .SingleAsync();
        Assert.Equal("Course-specific authored question", persistedPrompt);
    }

    [Fact]
    public async Task CourseExamContentSeed_CreatesPracticeAndIntermediateExams_IdempotentlyAndSupportsWrongThenCorrectAnswers()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Assistance Animals And Fair Housing");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.DurationHours = 4;
        var firstUnit = await fixture.DbContext.Units.SingleAsync(unit => unit.CourseId == seed.CourseId);
        firstUnit.Title = "Course Introduction";
        fixture.DbContext.Units.AddRange(
            new Unit { CourseId = seed.CourseId, Title = "Unit 1", OrderIndex = 2 },
            new Unit { CourseId = seed.CourseId, Title = "Unit 2", OrderIndex = 3 },
            new Unit { CourseId = seed.CourseId, Title = "Unit 3", OrderIndex = 4 });
        await fixture.DbContext.SaveChangesAsync();
        var finalExam = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(finalExam);

        await CourseExamContentSeed.SeedAsync(fixture.DbContext);
        await CourseExamContentSeed.SeedAsync(fixture.DbContext);

        var seededExams = await fixture.DbContext.CourseExams
            .Include(exam => exam.Questions)
            .Where(exam => exam.CourseId == seed.CourseId && exam.Kind != CourseExamKinds.Standard)
            .ToListAsync();
        var practiceExam = Assert.Single(seededExams, exam => exam.Kind == CourseExamKinds.Practice);
        var intermediateExam = Assert.Single(seededExams, exam => exam.Kind == CourseExamKinds.Intermediate);
        Assert.False(practiceExam.IsRequired);
        Assert.Equal(finalExam!.Questions.Select(question => question.Prompt),
            practiceExam.Questions.OrderBy(question => question.OrderIndex).Select(question => question.Prompt));
        Assert.True(intermediateExam.IsRequired);
        Assert.True(intermediateExam.UnitId.HasValue);
        Assert.NotEmpty(intermediateExam.Questions);

        var wrongAnswers = intermediateExam.Questions.ToDictionary(
            question => question.Id,
            question => question.CorrectOption == "A" ? "B" : "A");
        var failedAttempt = await fixture.ExamService.SubmitAttemptForExamAsync(intermediateExam.Id, learner.Id, wrongAnswers);
        Assert.False(failedAttempt.Passed);
        Assert.True(failedAttempt.CorrectAnswers < failedAttempt.TotalQuestions);

        var correctAnswers = await fixture.ExamService.GetCorrectAnswerMapForExamAsync(intermediateExam.Id);
        var passedAttempt = await fixture.ExamService.SubmitAttemptForExamAsync(intermediateExam.Id, learner.Id, correctAnswers);
        Assert.True(passedAttempt.Passed);
        Assert.Equal(passedAttempt.TotalQuestions, passedAttempt.CorrectAnswers);
    }

    [Fact]
    public async Task CreatePracticeExamAsync_CopiesFinalQuestionsAndAllowsUnlimitedUnproctoredAttempts()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Practice Exam Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.RequiresProctoredExam = true;
        await fixture.DbContext.SaveChangesAsync();

        var finalExam = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        var practice = await fixture.ExamService.CreateSupplementalExamAsync(
            seed.CourseId,
            CourseExamKinds.Practice,
            null,
            null,
            admin.Id,
            admin.Email);
        var practiceExam = await fixture.ExamService.GetExamViewAsync(practice.Id);

        Assert.NotNull(finalExam);
        Assert.NotNull(practiceExam);
        Assert.Equal(finalExam!.PassPercent, practiceExam!.PassPercent);
        Assert.False(practiceExam.IsRequired);
        Assert.Equal(finalExam!.Questions.Count, practiceExam!.Questions.Count);
        Assert.Equal(
            finalExam.Questions.Select(question => (question.Prompt, question.OptionA, question.OptionB, question.OptionC, question.OptionD, question.HintText)),
            practiceExam.Questions.Select(question => (question.Prompt, question.OptionA, question.OptionB, question.OptionC, question.OptionD, question.HintText)));
        var finalAnswers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        var practiceAnswers = await fixture.ExamService.GetCorrectAnswerMapForExamAsync(practice.Id);
        Assert.Equal(
            finalExam.Questions.OrderBy(question => question.OrderIndex).Select(question => finalAnswers[question.Id]),
            practiceExam.Questions.OrderBy(question => question.OrderIndex).Select(question => practiceAnswers[question.Id]));

        var eligibility = await fixture.ExamService.GetEligibilityForExamAsync(practice.Id, learner.Id);
        Assert.True(eligibility.HasUnlimitedAttempts);
        Assert.False(eligibility.RequiresProctorVerification);

        var correctAnswers = await fixture.ExamService.GetCorrectAnswerMapForExamAsync(practice.Id);
        Assert.True((await fixture.ExamService.SubmitAttemptForExamAsync(practice.Id, learner.Id, correctAnswers)).Passed);
        Assert.True((await fixture.ExamService.SubmitAttemptForExamAsync(practice.Id, learner.Id, correctAnswers)).Passed);
        Assert.False(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));
    }

    [Fact]
    public async Task IntermediateExam_IsUnitScopedAndRequiredForCourseCompletion()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Intermediate Exam Course");
        var unit = new Unit
        {
            CourseId = seed.CourseId,
            Title = "Middle Unit",
            OrderIndex = 1
        };
        fixture.DbContext.Units.Add(unit);
        await fixture.DbContext.SaveChangesAsync();

        var exam = await fixture.ExamService.CreateSupplementalExamAsync(
            seed.CourseId,
            CourseExamKinds.Intermediate,
            unit.Id,
            null,
            admin.Id,
            admin.Email);
        var editor = await fixture.ExamService.GetExamEditorByIdAsync(exam.Id);
        Assert.NotNull(editor);
        editor!.Questions.Add(new ExamQuestionEditorItem
        {
            Prompt = "Which answer is correct?",
            OptionA = "A",
            OptionB = "B",
            OptionC = "C",
            OptionD = "D",
            CorrectOption = "C"
        });
        await fixture.ExamService.SaveExamEditorByIdAsync(exam.Id, editor, admin.Id, admin.Email);

        var savedExam = await fixture.ExamService.GetExamViewAsync(exam.Id);
        Assert.NotNull(savedExam);
        var finalAnswers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        Assert.True((await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, finalAnswers)).Passed);
        Assert.False(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));

        var answer = savedExam!.Questions.ToDictionary(question => question.Id, _ => "C");
        Assert.True((await fixture.ExamService.SubmitAttemptForExamAsync(exam.Id, learner.Id, answer)).Passed);
        Assert.True(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));
    }

    [Fact]
    public async Task UnitExamAttempts_AreIndependent_AndAllRequiredExamsMustPass()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Unit Exam Course");
        var unit = new Unit
        {
            CourseId = seed.CourseId,
            Title = "Unit 2",
            OrderIndex = 1
        };
        var unitExam = new CourseExam
        {
            CourseId = seed.CourseId,
            UnitId = unit.Id,
            Title = "Unit 2 Exam",
            PassPercent = 80m,
            IsRequired = true
        };
        unitExam.Questions.Add(new ExamQuestion
        {
            Prompt = "Which answer is correct?",
            OptionA = "A",
            OptionB = "B",
            OptionC = "C",
            OptionD = "D",
            CorrectOption = "C",
            OrderIndex = 1
        });
        fixture.DbContext.Units.Add(unit);
        fixture.DbContext.CourseExams.Add(unitExam);
        await fixture.DbContext.SaveChangesAsync();

        var unitView = await fixture.ExamService.GetCourseExamAsync(seed.CourseId, unit.Id);
        var finalView = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.Equal("Unit 2 Exam", unitView!.Title);
        Assert.NotEqual(unitView.Id, finalView!.Id);

        var unitAnswers = unitView.Questions.ToDictionary(question => question.Id, _ => "C");
        var unitAttempt = await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, unitAnswers, unit.Id);
        Assert.True(unitAttempt.Passed);
        Assert.False(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));

        var finalAnswers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        var finalAttempt = await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, finalAnswers);
        Assert.True(finalAttempt.Passed);
        Assert.True(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));
    }

    [Fact]
    public async Task SaveExamEditorAsync_PreservesAuditHistoryAndResetsCurrentVersionAttempts()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Editable Exam Course");
        await fixture.PassExamAsync(seed.CourseId, learner.Id);
        await fixture.CompleteAllRequiredLessonsAsync(learner.Id, seed.RequiredLessonIds);

        var beforeCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.NotNull(beforeCertificate);
        Assert.False(beforeCertificate!.IsRevoked);

        var editor = await fixture.ExamService.GetExamEditorAsync(seed.CourseId);
        Assert.NotNull(editor);

        editor!.Title = "Updated Final Exam";
        editor.PassPercent = 90m;
        editor.Questions[0].Prompt = "Updated prompt";

        await fixture.ExamService.SaveExamEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var attemptCount = await fixture.DbContext.ExamAttempts
            .CountAsync(attempt => attempt.UserAccountId == learner.Id);
        Assert.Equal(1, attemptCount);

        var eligibility = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.Equal(0, eligibility.AttemptsUsed);
        Assert.False(eligibility.HasPassed);
        var auditHistory = await fixture.ExamService.GetAttemptHistoryForCourseAsync(seed.CourseId, learner.Id);
        Assert.Single(auditHistory);
        Assert.Equal("Editable Exam Course Final Exam", auditHistory[0].ExamTitle);
        Assert.NotEqual("Updated prompt", auditHistory[0].Answers[0].Prompt);

        var afterCertificate = await fixture.DbContext.CompletionCertificates
            .AsNoTracking()
            .SingleAsync(certificate => certificate.EnrollmentId == seed.EnrollmentId);

        Assert.True(afterCertificate.IsRevoked);
        Assert.Contains("Exam updated", afterCertificate.RevocationReason ?? string.Empty);
    }

    [Fact]
    public async Task SubmitAttemptAsync_EnforcesPolicyLimit_AndGrantAllowsAdditionalAttempt()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Retake Limits Course");

        var editor = await fixture.ExamService.GetExamEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 0;
        await fixture.ExamService.SaveExamEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));

        await fixture.ExamService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var fourthAttempt = await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        Assert.False(fourthAttempt.Passed);
    }

    [Fact]
    public async Task SubmitAttemptAsync_PrelicensingAllowsOneRetakeByDefault()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Prelicensing Retake Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.ComplianceType = CourseComplianceTypes.Prelicensing;
        await fixture.DbContext.SaveChangesAsync();

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");

        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));
    }

    [Fact]
    public async Task SubmitAttemptAsync_PostlicensingAllowsUnlimitedRetakesByDefault()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Postlicensing Retake Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.ComplianceType = CourseComplianceTypes.Postlicensing;
        await fixture.DbContext.SaveChangesAsync();

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        }

        var eligibility = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(eligibility.HasUnlimitedAttempts);
        Assert.Equal(5, eligibility.AttemptsUsed);
    }

    [Fact]
    public async Task GrantRetakeAsync_ResetCooldownTimer_AllowsImmediateRetry()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");

        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Cooldown Reset Course");

        var editor = await fixture.ExamService.GetExamEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 30;
        await fixture.ExamService.SaveExamEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers));

        await fixture.ExamService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, true, admin.Id, admin.Email);

        var retryAttempt = await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        Assert.False(retryAttempt.Passed);
    }

    [Fact]
    public async Task GetEligibilityAsync_AfterGrant_IncreasesAttemptsAllowed_AndClearsRetakeLimitBlock()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Eligibility Retake Grant Course");

        var editor = await fixture.ExamService.GetExamEditorAsync(seed.CourseId);
        Assert.NotNull(editor);
        editor!.RetakeCooldownMinutes = 0;
        await fixture.ExamService.SaveExamEditorAsync(seed.CourseId, editor, admin.Id, admin.Email);

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(view);

        var failingAnswers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, failingAnswers);

        var beforeGrant = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(beforeGrant.RequiresExam);
        Assert.False(beforeGrant.HasPassed);
        Assert.Equal(2, beforeGrant.AttemptsUsed);
        Assert.Equal(2, beforeGrant.AttemptsAllowed);
        Assert.Equal("Retake limit reached. Contact an admin for additional attempts.", beforeGrant.BlockingReason);

        await fixture.ExamService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var afterGrant = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(afterGrant.RequiresExam);
        Assert.False(afterGrant.HasPassed);
        Assert.True(afterGrant.HasRetakeGrantOverride);
        Assert.Equal(2, afterGrant.AttemptsUsed);
        Assert.Equal(3, afterGrant.AttemptsAllowed);
        Assert.NotEqual("Retake limit reached. Contact an admin for additional attempts.", afterGrant.BlockingReason);
    }

    [Fact]
    public async Task GetEligibilityAsync_WhenRetakeGrantExists_OverridesHistoricalPassAndAllowsRetryAccess()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Retake Override Course");

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(view);

        var correctAnswers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        var passingAnswers = view!.Questions.ToDictionary(question => question.Id, question => correctAnswers[question.Id]);
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, passingAnswers);
        var historicalPass = await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id);
        Assert.True(historicalPass);

        await fixture.ExamService.GrantRetakeAsync(seed.CourseId, learner.Id, 1, false, admin.Id, admin.Email);

        var eligibility = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.True(eligibility.HasRetakeGrantOverride);
        Assert.False(eligibility.HasPassed);
        Assert.False(await fixture.ExamService.HasPassedRequiredExamAsync(seed.CourseId, learner.Id));
        Assert.NotNull(eligibility.BlockingReason);
    }

    [Fact]
    public async Task GetAttemptHistoryAsync_ReturnsAttemptsWithAnswerReview()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "History Course");

        var view = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(view);

        var answers = view!.Questions.ToDictionary(question => question.Id, _ => "D");
        await fixture.ExamService.SubmitAttemptAsync(seed.CourseId, learner.Id, answers);

        var history = await fixture.ExamService.GetAttemptHistoryAsync(seed.CourseId, learner.Id);

        Assert.Single(history);
        Assert.Equal(1, history[0].AttemptNumber);
        Assert.True(history[0].Answers.Count > 0);
    }

    [Fact]
    public async Task ExamAttemptAudit_RecordsElapsedTimeAndReturnsScopedCourseHistory()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Attempt Audit Course");
        var exam = await fixture.ExamService.GetCourseExamAsync(seed.CourseId);
        Assert.NotNull(exam);
        var answers = await fixture.ExamService.GetCorrectAnswerMapAsync(seed.CourseId);
        var startedAt = DateTime.UtcNow.AddMinutes(-17);

        var result = await fixture.ExamService.SubmitAttemptForExamAsync(exam!.Id, learner.Id, answers, startedAt);
        var history = await fixture.ExamService.GetAttemptHistoryForCourseAsync(seed.CourseId, learner.Id);
        var audit = await fixture.ExamService.GetAttemptAuditAsync(seed.CourseId, learner.Id);

        Assert.True(result.Passed);
        Assert.True(result.Duration.TotalMinutes >= 16);
        var historyItem = Assert.Single(history);
        Assert.Equal(exam.Id, historyItem.ExamId);
        Assert.Equal(startedAt, historyItem.StartedAt);
        Assert.Equal("Attempt Audit Course Final Exam", historyItem.ExamTitle);
        Assert.Equal(1, historyItem.ExamVersion);
        Assert.NotEqual("-", historyItem.Answers[0].CorrectOption);

        var auditRow = Assert.Single(audit);
        Assert.Equal(seed.CourseId, auditRow.CourseId);
        Assert.Equal(learner.Id, auditRow.LearnerUserId);
        Assert.True(auditRow.Passed);
        Assert.Equal(1, auditRow.AttemptNumber);
        Assert.True(auditRow.Duration.TotalMinutes >= 16);
    }

    [Fact]
    public async Task RetakeGrantSupport_CanListUpdateAndRevoke()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Grant Support Course");

        await fixture.ExamService.GrantRetakeAsync(seed.CourseId, learner.Id, 2, false, admin.Id, admin.Email);

        var grants = await fixture.ExamService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Single(grants);
        Assert.Equal(2, grants[0].GrantedAttempts);

        await fixture.ExamService.SetRetakeGrantAttemptsAsync(seed.CourseId, learner.Id, 5, false, admin.Id, admin.Email);

        grants = await fixture.ExamService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Single(grants);
        Assert.Equal(5, grants[0].GrantedAttempts);

        await fixture.ExamService.RevokeRetakeGrantAsync(seed.CourseId, learner.Id, admin.Id, admin.Email);

        grants = await fixture.ExamService.GetRetakeGrantsAsync(seed.CourseId);
        Assert.Empty(grants);
    }

    [Fact]
    public async Task ProctoringSessionLifecycle_ControlsExamEligibility()
    {
        await using var fixture = await TestFixture.CreateAsync();

        var learner = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "william.guest@wlr.com");
        var admin = await fixture.DbContext.UserAccounts.SingleAsync(user => user.Email == "admin@wlr.com");
        var seed = await fixture.CreateCourseGraphWithEnrollmentAsync(learner.Id, "Proctored Course");
        var course = await fixture.DbContext.Courses.SingleAsync(existing => existing.Id == seed.CourseId);
        course.RequiresProctoredExam = true;
        await fixture.DbContext.SaveChangesAsync();

        var blocked = await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id);
        Assert.Contains("proctoring session", blocked.BlockingReason, StringComparison.OrdinalIgnoreCase);
        Assert.True(blocked.RequiresProctorVerification);

        var firstSession = await fixture.ExamService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, null, 120, admin.Id, admin.Email);
        Assert.Equal(ProctoringDefaults.CompanyName, firstSession.ProctorName);
        Assert.NotNull(await fixture.ExamService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));
        Assert.False((await fixture.ExamService.GetEligibilityAsync(seed.CourseId, learner.Id)).RequiresProctorVerification);

        var replacement = await fixture.ExamService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, "replacement", 120, admin.Id, admin.Email);
        var expiredFirst = await fixture.DbContext.ExamProctoringSessions.AsNoTracking().SingleAsync(session => session.Id == firstSession.Id);
        Assert.True(expiredFirst.ExpiresAtUtc <= replacement.IdentityVerifiedAtUtc);

        await fixture.ExamService.ReportProctoringIncidentAsync(replacement.Id, "Unauthorized material observed.", admin.Id, admin.Email);
        Assert.Null(await fixture.ExamService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));

        var finalSession = await fixture.ExamService.RegisterProctoringSessionAsync(
            seed.CourseId, learner.Id, ProctoringDefaults.CompanyName, null, 120, admin.Id, admin.Email);
        fixture.DbContext.ExamProctoringSessions.Add(new ExamProctoringSession
        {
            CourseId = seed.CourseId,
            UserAccountId = learner.Id,
            ProctorName = ProctoringDefaults.CompanyName,
            IdentityVerifiedAtUtc = DateTime.UtcNow,
            ClosedBookConfirmed = true,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(120)
        });
        await fixture.DbContext.SaveChangesAsync();
        await fixture.ExamService.ExpireProctoringSessionAsync(finalSession.Id, admin.Id, admin.Email);
        Assert.Null(await fixture.ExamService.GetActiveProctoringSessionAsync(seed.CourseId, learner.Id));
    }

    [Fact]
    public void GetCompletionTimestampUtc_PrefersPersistedExamTimestamp()
    {
        var persistedSubmissionUtc = new DateTime(2026, 08, 21, 17, 30, 00, DateTimeKind.Utc);
        var queryTimestampUtc = new DateTime(2026, 08, 21, 18, 05, 00, DateTimeKind.Utc);
        var fallbackUtc = new DateTime(2026, 08, 21, 18, 30, 00, DateTimeKind.Utc);

        var resolvedUtc = ExamCompletionTimestampResolver.GetCompletionTimestampUtc(
            persistedSubmissionUtc,
            queryTimestampUtc,
            fallbackUtc);

        Assert.Equal(persistedSubmissionUtc, resolvedUtc);
    }

    private sealed class TestFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext DbContext { get; }
        public IEnrollmentService EnrollmentService { get; }
        public IExamService ExamService { get; }

        private TestFixture(SqliteConnection connection, ApplicationDbContext dbContext, IEnrollmentService enrollmentService, IExamService examService)
        {
            _connection = connection;
            DbContext = dbContext;
            EnrollmentService = enrollmentService;
            ExamService = examService;
        }

        public static async Task<TestFixture> CreateAsync(string? databaseFile = null)
        {
            var connection = new SqliteConnection(databaseFile is null ? "DataSource=:memory:"
                : $"Data Source={databaseFile};Pooling=False;Default Timeout=5");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            var auditLogService = new AuditLogService(dbContext);
            var userAccountService = new UserAccountService(dbContext, auditLogService);
            await userAccountService.EnsureSeedUsersAsync();

            var examService = new ExamService(dbContext, auditLogService);
            var enrollmentService = new EnrollmentService(dbContext, auditLogService, examService, new SchoolProfileService(dbContext, auditLogService));
            return new TestFixture(connection, dbContext, enrollmentService, examService);
        }

        public async Task<(Guid CourseId, Guid EnrollmentId, List<Guid> RequiredLessonIds)> CreateCourseGraphWithEnrollmentAsync(Guid learnerId, string title)
        {
            var course = new Course
            {
                Title = title,
                Slug = Guid.NewGuid().ToString("N"),
                Description = "Test course",
                Level = "Beginner",
                DurationHours = 2,
                CreditHours = 2,
                Price = 0,
                IsPublished = true
            };

            DbContext.Courses.Add(course);

            var unit = new Unit
            {
                Course = course,
                Title = "Unit 1",
                OrderIndex = 1
            };

            DbContext.Units.Add(unit);

            var lessonA = new Lesson
            {
                Unit = unit,
                Title = "Lesson A",
                ContentType = "Text",
                TextContent = "A",
                DurationMinutes = 10,
                OrderIndex = 1,
                IsRequired = true
            };

            var lessonB = new Lesson
            {
                Unit = unit,
                Title = "Lesson B",
                ContentType = "Text",
                TextContent = "B",
                DurationMinutes = 10,
                OrderIndex = 2,
                IsRequired = true
            };

            DbContext.Lessons.AddRange(lessonA, lessonB);

            var enrollment = new Enrollment
            {
                UserAccountId = learnerId,
                Course = course,
                EnrollmentSource = "LearnerPurchase",
                ConsentStatus = "NotRequired",
                ProgressPercent = 0,
                Completed = false,
                EnrolledAt = DateTime.UtcNow
            };

            DbContext.Enrollments.Add(enrollment);
            await DbContext.SaveChangesAsync();

            await ExamService.EnsureDefaultExamForCourseAsync(course.Id);

            return (course.Id, enrollment.Id, [lessonA.Id, lessonB.Id]);
        }

        public async Task CompleteAllRequiredLessonsAsync(Guid learnerId, List<Guid> lessonIds)
        {
            foreach (var lessonId in lessonIds)
            {
                await EnrollmentService.SetLessonCompletionAsync(learnerId, lessonId, true);
            }
        }

        public async Task PassExamAsync(Guid courseId, Guid learnerId)
        {
            await ExamService.EnsureDefaultExamForCourseAsync(courseId);

            var exam = await DbContext.CourseExams
                .Include(existing => existing.Questions)
                .FirstAsync(existing => existing.CourseId == courseId && existing.IsRequired);

            var answers = exam.Questions.ToDictionary(question => question.Id, question => question.CorrectOption);
            await ExamService.SubmitAttemptAsync(courseId, learnerId, answers);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
