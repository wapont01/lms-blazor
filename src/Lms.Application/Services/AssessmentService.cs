using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public interface IAssessmentService
{
    Task EnsureDefaultAssessmentForCourseAsync(Guid courseId);
    Task<CourseAssessmentView?> GetCourseAssessmentAsync(Guid courseId);
    Task<Dictionary<Guid, string>> GetCorrectAnswerMapAsync(Guid courseId);
    Task<AssessmentEditorModel?> GetAssessmentEditorAsync(Guid courseId);
    Task SaveAssessmentEditorAsync(Guid courseId, AssessmentEditorModel editor, Guid actorUserId, string actorEmail);
    Task<AssessmentEligibility> GetEligibilityAsync(Guid courseId, Guid userId);
    Task<AssessmentAttemptSummary> SubmitAttemptAsync(Guid courseId, Guid userId, Dictionary<Guid, string> answersByQuestionId);
    Task<bool> HasPassedRequiredAssessmentAsync(Guid courseId, Guid userId);
    Task<List<AssessmentAttemptHistoryItem>> GetAttemptHistoryAsync(Guid courseId, Guid userId);
    Task GrantRetakeAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail);
    Task<List<RetakeGrantView>> GetRetakeGrantsAsync(Guid courseId);
    Task SetRetakeGrantAttemptsAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail);
    Task RevokeRetakeGrantAsync(Guid courseId, Guid learnerUserId, Guid adminUserId, string adminEmail);
}

public sealed record AssessmentQuestionView(Guid Id, string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, int OrderIndex, string? HintText);
public sealed record CourseAssessmentView(Guid Id, string Title, decimal PassPercent, bool IsRequired, List<AssessmentQuestionView> Questions);
public sealed record AssessmentAttemptSummary(DateTime SubmittedAt, decimal ScorePercent, bool Passed, int TotalQuestions, int CorrectAnswers);
public sealed record AssessmentEligibility(
    bool RequiresAssessment,
    bool HasPassed,
    AssessmentAttemptSummary? LatestAttempt,
    string? BlockingReason,
    bool HasRetakeGrantOverride,
    int AttemptsUsed,
    int AttemptsAllowed);
public sealed record AssessmentAttemptAnswerReview(string Prompt, string SelectedOption, string CorrectOption, bool IsCorrect, string? FeedbackText);
public sealed record AssessmentAttemptHistoryItem(int AttemptNumber, DateTime SubmittedAt, decimal ScorePercent, bool Passed, List<AssessmentAttemptAnswerReview> Answers, string? FeedbackSummary);
public sealed record RetakeGrantView(Guid LearnerUserId, string LearnerDisplay, int GrantedAttempts, DateTime GrantedAt, Guid GrantedByAdminId, string GrantedByAdminDisplay);

public sealed class AssessmentEditorModel
{
    public Guid? AssessmentId { get; set; }
    public string Title { get; set; } = "Final Assessment";
    public decimal PassPercent { get; set; } = 80m;
    public int? MaxRetakesPerLearner { get; set; }
    public int RetakeCooldownMinutes { get; set; }
    public List<AssessmentQuestionEditorItem> Questions { get; set; } = new();
}

public sealed class AssessmentQuestionEditorItem
{
    public Guid? QuestionId { get; set; }
    public int OrderIndex { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string OptionC { get; set; } = string.Empty;
    public string OptionD { get; set; } = string.Empty;
    public string CorrectOption { get; set; } = "A";
    public string? FeedbackText { get; set; }
}

public class AssessmentService : IAssessmentService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    private static readonly DefaultAssessmentQuestionSpec[] DefaultAssessmentQuestionSpecs =
    [
        new("What should you review first when planning a license path?", "Office branding", "State requirements and education hours", "Social media templates", "Desk layout", "B"),
        new("Why is brokerage supervision important in early practice?", "It replaces study", "It provides required oversight and authorization", "It removes disclosure rules", "It shortens the exam", "B"),
        new("What should happen when a policy update changes a procedure?", "Ignore it until next year", "Apply it promptly and document the change", "Wait for a client complaint", "Ask a peer to guess", "B"),
        new("Which practice best supports recordkeeping and accountability?", "Keep clear, dated files", "Rely only on memory", "Delete drafts immediately", "Use only verbal confirmation", "A"),
        new("How should you respond to a disclosure request?", "Delay until closing", "Provide it promptly and document delivery", "Avoid the subject", "Wait for the other party to ask again", "B"),
        new("Before working a first client file, what should be confirmed?", "The office paint color", "Supervision, forms, and approval steps", "The marketing slogan", "The break room schedule", "B"),
        new("What is the main purpose of contingencies in a contract?", "Remove all risk", "Define conditions and deadlines", "Replace signatures", "Guarantee commission", "B"),
        new("What should you do if you are unsure about a compliance decision?", "Escalate to a supervisor or responsible party", "Proceed silently", "Randomize the process", "Wait until quarter-end", "A"),
        new("What is the best way to prepare for the exam?", "Memorize one chapter only", "Study by topic and use practice checkpoints", "Skip review after one pass", "Avoid feedback", "B"),
        new("What belongs in a first-30-day transaction plan?", "Lead intake scripts, disclosure timing, and supervision touchpoints", "Holiday decorations", "Personal slogans", "Office renovation notes", "A")
    ];

    public AssessmentService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    public async Task EnsureDefaultAssessmentForCourseAsync(Guid courseId)
    {
        var defaultTitle = await _dbContext.Courses
            .Where(course => course.Id == courseId)
            .Select(course => $"{course.Title} Final Assessment")
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(defaultTitle))
        {
            return;
        }

        var assessment = await _dbContext.CourseAssessments
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired);

        if (assessment is null)
        {
            assessment = new CourseAssessment
            {
                CourseId = courseId,
                Title = defaultTitle,
                PassPercent = 80m,
                IsRequired = true
            };

            _dbContext.CourseAssessments.Add(assessment);
        }

        var shouldSeedDefaultContent = string.Equals(assessment.Title, defaultTitle, StringComparison.OrdinalIgnoreCase)
            && assessment.Questions.Count != DefaultAssessmentQuestionSpecs.Length;

        if (assessment.Questions.Count > 0 && !shouldSeedDefaultContent)
        {
            return;
        }

        assessment.Title = defaultTitle;
        assessment.PassPercent = 80m;
        assessment.IsRequired = true;
        assessment.MaxRetakesPerLearner = null;
        assessment.RetakeCooldownMinutes = 0;

        var orderedQuestions = assessment.Questions.OrderBy(question => question.OrderIndex).ToList();
        for (var index = 0; index < DefaultAssessmentQuestionSpecs.Length; index++)
        {
            var spec = DefaultAssessmentQuestionSpecs[index];
            if (index < orderedQuestions.Count)
            {
                var question = orderedQuestions[index];
                question.Prompt = spec.Prompt;
                question.OptionA = spec.OptionA;
                question.OptionB = spec.OptionB;
                question.OptionC = spec.OptionC;
                question.OptionD = spec.OptionD;
                question.CorrectOption = spec.CorrectOption;
                question.OrderIndex = index + 1;
                continue;
            }

            assessment.Questions.Add(new AssessmentQuestion
            {
                Prompt = spec.Prompt,
                OptionA = spec.OptionA,
                OptionB = spec.OptionB,
                OptionC = spec.OptionC,
                OptionD = spec.OptionD,
                CorrectOption = spec.CorrectOption,
                OrderIndex = index + 1
            });
        }

        if (orderedQuestions.Count > DefaultAssessmentQuestionSpecs.Length)
        {
            _dbContext.AssessmentQuestions.RemoveRange(orderedQuestions.Skip(DefaultAssessmentQuestionSpecs.Length));
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
        }
    }

    public async Task<CourseAssessmentView?> GetCourseAssessmentAsync(Guid courseId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .OrderBy(existing => existing.Title)
            .FirstOrDefaultAsync();

        if (assessment is null)
        {
            return null;
        }

        return new CourseAssessmentView(
            assessment.Id,
            assessment.Title,
            assessment.PassPercent,
            assessment.IsRequired,
            assessment.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new AssessmentQuestionView(
                    question.Id,
                    question.Prompt,
                    question.OptionA,
                    question.OptionB,
                    question.OptionC,
                    question.OptionD,
                    question.OrderIndex,
                    question.FeedbackText))
                .ToList());
    }

    public async Task<Dictionary<Guid, string>> GetCorrectAnswerMapAsync(Guid courseId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired);

        if (assessment is null)
        {
            return new Dictionary<Guid, string>();
        }

        return assessment.Questions
            .OrderBy(question => question.OrderIndex)
            .ToDictionary(question => question.Id, question => question.CorrectOption);
    }

    public async Task<AssessmentEditorModel?> GetAssessmentEditorAsync(Guid courseId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired);

        if (assessment is null)
        {
            return null;
        }

        return new AssessmentEditorModel
        {
            AssessmentId = assessment.Id,
            Title = assessment.Title,
            PassPercent = assessment.PassPercent,
            MaxRetakesPerLearner = assessment.MaxRetakesPerLearner,
            RetakeCooldownMinutes = assessment.RetakeCooldownMinutes,
            Questions = assessment.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new AssessmentQuestionEditorItem
                {
                    QuestionId = question.Id,
                    OrderIndex = question.OrderIndex,
                    Prompt = question.Prompt,
                    OptionA = question.OptionA,
                    OptionB = question.OptionB,
                    OptionC = question.OptionC,
                    OptionD = question.OptionD,
                    CorrectOption = question.CorrectOption,
                    FeedbackText = question.FeedbackText
                })
                .ToList()
        };
    }

    public async Task SaveAssessmentEditorAsync(Guid courseId, AssessmentEditorModel editor, Guid actorUserId, string actorEmail)
    {
        if (editor.Questions.Count == 0)
        {
            throw new InvalidOperationException("Assessment must include at least one question.");
        }

        var assessment = await _dbContext.CourseAssessments
            .Include(existing => existing.Questions)
            .Include(existing => existing.Attempts)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired);

        if (assessment is null)
        {
            assessment = new CourseAssessment
            {
                CourseId = courseId,
                IsRequired = true
            };

            _dbContext.CourseAssessments.Add(assessment);
        }

        assessment.Title = string.IsNullOrWhiteSpace(editor.Title)
            ? "Final Assessment"
            : editor.Title.Trim();
        assessment.PassPercent = Math.Clamp(editor.PassPercent, 0m, 100m);
        assessment.MaxRetakesPerLearner = editor.MaxRetakesPerLearner is null or <= 0 ? null : editor.MaxRetakesPerLearner;
        assessment.RetakeCooldownMinutes = Math.Max(editor.RetakeCooldownMinutes, 0);

        var existingQuestionsById = assessment.Questions.ToDictionary(question => question.Id, question => question);
        var retainedQuestionIds = new HashSet<Guid>();

        for (var index = 0; index < editor.Questions.Count; index++)
        {
            var input = editor.Questions[index];
            ValidateEditorQuestion(input, index + 1);

            AssessmentQuestion question;
            if (input.QuestionId.HasValue && existingQuestionsById.TryGetValue(input.QuestionId.Value, out var existingQuestion))
            {
                question = existingQuestion;
            }
            else
            {
                question = new AssessmentQuestion
                {
                    CourseAssessment = assessment
                };

                assessment.Questions.Add(question);
            }

            question.Prompt = input.Prompt.Trim();
            question.OptionA = input.OptionA.Trim();
            question.OptionB = input.OptionB.Trim();
            question.OptionC = input.OptionC.Trim();
            question.OptionD = input.OptionD.Trim();
            question.CorrectOption = NormalizeOption(input.CorrectOption);
            question.FeedbackText = string.IsNullOrWhiteSpace(input.FeedbackText) ? null : input.FeedbackText.Trim();
            question.OrderIndex = index + 1;

            retainedQuestionIds.Add(question.Id);
        }

        var removedQuestions = assessment.Questions
            .Where(question => !retainedQuestionIds.Contains(question.Id))
            .ToList();

        if (removedQuestions.Count > 0)
        {
            _dbContext.AssessmentQuestions.RemoveRange(removedQuestions);
        }

        if (assessment.Attempts.Count > 0)
        {
            _dbContext.AssessmentAttempts.RemoveRange(assessment.Attempts);

            var issuedCertificates = await _dbContext.CompletionCertificates
                .Where(certificate => certificate.CourseId == courseId && !certificate.IsRevoked)
                .ToListAsync();

            foreach (var certificate in issuedCertificates)
            {
                certificate.IsRevoked = true;
                certificate.RevokedAt = DateTime.UtcNow;
                certificate.RevocationReason = "Assessment updated. Learner must pass the latest required assessment.";
            }
        }

        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(
            actorUserId,
            actorEmail,
            "assessment.updated",
            "CourseAssessment",
            assessment.Id,
            $"CourseId={courseId}; Questions={editor.Questions.Count}; PassPercent={assessment.PassPercent:0.##}");
    }

    public async Task<AssessmentEligibility> GetEligibilityAsync(Guid courseId, Guid userId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => new { existing.Id, existing.IsRequired, existing.MaxRetakesPerLearner, existing.RetakeCooldownMinutes })
            .FirstOrDefaultAsync();

        if (assessment is null || !assessment.IsRequired)
        {
            return new AssessmentEligibility(false, true, null, null, false, 0, 0);
        }

        var latestAttempt = await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .Where(attempt => attempt.CourseAssessmentId == assessment.Id && attempt.UserAccountId == userId)
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .Select(attempt => new AssessmentAttemptSummary(
                attempt.SubmittedAt,
                attempt.ScorePercent,
                attempt.Passed,
                attempt.Answers.Count,
                attempt.Answers.Count(answer => answer.IsCorrect)))
            .FirstOrDefaultAsync();

        var hasPassed = await HasPassedRequiredAssessmentAsync(courseId, userId);
        var attemptsUsed = await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.CourseAssessmentId == assessment.Id && attempt.UserAccountId == userId);

        var firstAttemptPassed = await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .Where(attempt => attempt.CourseAssessmentId == assessment.Id && attempt.UserAccountId == userId)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ThenBy(attempt => attempt.SubmittedAt)
            .Select(attempt => (bool?)attempt.Passed)
            .FirstOrDefaultAsync();

        var grant = await _dbContext.RetakeGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.CourseAssessmentId == assessment.Id && existing.UserAccountId == userId);

        var baseAttemptsAllowed = firstAttemptPassed switch
        {
            null => 1,
            true => 1,
            false => 2
        };
        var attemptsAllowed = baseAttemptsAllowed + (grant?.GrantedAttempts ?? 0);

        string? blockingReason;

        if (hasPassed)
        {
            blockingReason = null;
        }
        else if (latestAttempt is null)
        {
            blockingReason = "Certificate is gated until the required final assessment is passed.";
        }
        else if (attemptsUsed >= attemptsAllowed)
        {
            blockingReason = "Retake limit reached. Contact an admin for additional attempts.";
        }
        else
        {
            var nextAttemptAtUtc = latestAttempt.SubmittedAt.AddMinutes(Math.Max(assessment.RetakeCooldownMinutes, 0));
            blockingReason = nextAttemptAtUtc > DateTime.UtcNow
                ? $"Retake available at {nextAttemptAtUtc:u}."
                : "Latest assessment attempt did not pass. Submit another attempt to unlock certification.";
        }

        var hasRetakeGrantOverride = grant is not null && grant.GrantedAttempts > 0;
        return new AssessmentEligibility(
            true,
            hasPassed,
            latestAttempt,
            blockingReason,
            hasRetakeGrantOverride,
            attemptsUsed,
            attemptsAllowed);
    }

    public async Task<AssessmentAttemptSummary> SubmitAttemptAsync(Guid courseId, Guid userId, Dictionary<Guid, string> answersByQuestionId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired);

        if (assessment is null)
        {
            throw new InvalidOperationException("No required assessment was found for this course.");
        }

        var orderedQuestions = assessment.Questions.OrderBy(question => question.OrderIndex).ToList();
        if (orderedQuestions.Count == 0)
        {
            throw new InvalidOperationException("Assessment has no questions.");
        }

        var existingAttempts = await _dbContext.AssessmentAttempts
            .Where(existing => existing.CourseAssessmentId == assessment.Id && existing.UserAccountId == userId)
            .OrderByDescending(existing => existing.SubmittedAt)
            .ToListAsync();

        var attemptCount = existingAttempts.Count;
        var latestAttempt = existingAttempts.FirstOrDefault();

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseAssessmentId == assessment.Id && existing.UserAccountId == userId);

        var firstAttempt = existingAttempts
            .OrderBy(existing => existing.AttemptNumber)
            .ThenBy(existing => existing.SubmittedAt)
            .FirstOrDefault();
        var baseAttemptsAllowed = firstAttempt switch
        {
            null => 1,
            { Passed: true } => 1,
            _ => 2
        };
        var maxAttemptsAllowed = baseAttemptsAllowed + (grant?.GrantedAttempts ?? 0);

        if (attemptCount >= maxAttemptsAllowed)
        {
            throw new InvalidOperationException("Retake limit reached. Contact an admin for additional attempts.");
        }

        var cooldownMinutes = Math.Max(assessment.RetakeCooldownMinutes, 0);
        if (latestAttempt is not null && cooldownMinutes > 0)
        {
            var nextAttemptAtUtc = latestAttempt.SubmittedAt.AddMinutes(cooldownMinutes);
            if (nextAttemptAtUtc > DateTime.UtcNow)
            {
                throw new InvalidOperationException($"Retake is on cooldown until {nextAttemptAtUtc:u}.");
            }
        }

        var attempt = new AssessmentAttempt
        {
            CourseAssessmentId = assessment.Id,
            UserAccountId = userId,
            StartedAt = DateTime.UtcNow,
            SubmittedAt = DateTime.UtcNow,
            AttemptNumber = attemptCount + 1
        };

        var correctAnswers = 0;

        foreach (var question in orderedQuestions)
        {
            var selected = answersByQuestionId.TryGetValue(question.Id, out var value)
                ? NormalizeOption(value)
                : string.Empty;
            var isCorrect = string.Equals(selected, question.CorrectOption, StringComparison.OrdinalIgnoreCase);
            if (isCorrect)
            {
                correctAnswers += 1;
            }

            attempt.Answers.Add(new AssessmentAnswer
            {
                AssessmentQuestionId = question.Id,
                SelectedOption = string.IsNullOrWhiteSpace(selected) ? "-" : selected,
                IsCorrect = isCorrect
            });
        }

        var totalQuestions = orderedQuestions.Count;
        var score = Math.Round(100m * correctAnswers / totalQuestions, 2, MidpointRounding.AwayFromZero);
        var passed = score >= assessment.PassPercent;

        attempt.ScorePercent = score;
        attempt.Passed = passed;
        attempt.FeedbackSummary = passed
            ? "Pass. Assessment requirements are satisfied."
            : "Not passed yet. Review the hints for missed questions and retake when available.";

        _dbContext.AssessmentAttempts.Add(attempt);
        await _dbContext.SaveChangesAsync();

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await _auditLogService.WriteAsync(
            userId,
            actorEmail,
            passed ? "assessment.attempt.passed" : "assessment.attempt.failed",
            "CourseAssessment",
            assessment.Id,
            $"Score={score:0.##}; PassPercent={assessment.PassPercent:0.##}");

        return new AssessmentAttemptSummary(attempt.SubmittedAt, score, passed, totalQuestions, correctAnswers);
    }

    public async Task<List<AssessmentAttemptHistoryItem>> GetAttemptHistoryAsync(Guid courseId, Guid userId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessmentId = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();

        if (!assessmentId.HasValue)
        {
            return [];
        }

        var attempts = await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.AssessmentQuestion)
            .Where(attempt => attempt.CourseAssessmentId == assessmentId.Value && attempt.UserAccountId == userId)
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ToListAsync();

        return attempts
            .Select(attempt => new AssessmentAttemptHistoryItem(
                attempt.AttemptNumber,
                attempt.SubmittedAt,
                attempt.ScorePercent,
                attempt.Passed,
                attempt.Answers
                    .OrderBy(answer => answer.AssessmentQuestion?.OrderIndex ?? int.MaxValue)
                    .Select(answer => new AssessmentAttemptAnswerReview(
                        answer.AssessmentQuestion?.Prompt ?? "Question",
                        answer.SelectedOption,
                        "-",
                        answer.IsCorrect,
                        answer.AssessmentQuestion?.FeedbackText))
                    .ToList(),
                attempt.FeedbackSummary))
            .ToList();
    }

    public async Task GrantRetakeAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail)
    {
        if (grantedAttempts <= 0)
        {
            throw new InvalidOperationException("Granted attempts must be greater than zero.");
        }

        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => new { existing.Id, existing.RetakeCooldownMinutes })
            .FirstOrDefaultAsync();

        if (assessment is null)
        {
            throw new InvalidOperationException("Assessment not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseAssessmentId == assessment.Id && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            grant = new RetakeGrant
            {
                CourseAssessmentId = assessment.Id,
                UserAccountId = learnerUserId,
                GrantedAttempts = grantedAttempts,
                GrantedByAdminId = adminUserId,
                GrantedAt = DateTime.UtcNow
            };

            _dbContext.RetakeGrants.Add(grant);
        }
        else
        {
            grant.GrantedAttempts += grantedAttempts;
            grant.GrantedByAdminId = adminUserId;
            grant.GrantedAt = DateTime.UtcNow;
        }

        if (resetCooldownTimer)
        {
            await ResetRetakeCooldownTimerAsync(assessment.Id, learnerUserId, assessment.RetakeCooldownMinutes);
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "assessment.retake.granted", "CourseAssessment", assessment.Id, $"Learner={learnerUserId}; Attempts={grantedAttempts}; ResetCooldownTimer={resetCooldownTimer}");
    }

    public async Task<List<RetakeGrantView>> GetRetakeGrantsAsync(Guid courseId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessmentId = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();

        if (!assessmentId.HasValue)
        {
            return [];
        }

        var grants = await _dbContext.RetakeGrants
            .AsNoTracking()
            .Include(grant => grant.UserAccount)
            .Include(grant => grant.GrantedByAdmin)
            .Where(grant => grant.CourseAssessmentId == assessmentId.Value)
            .OrderByDescending(grant => grant.GrantedAt)
            .ToListAsync();

        return grants
            .Select(grant => new RetakeGrantView(
                grant.UserAccountId,
                $"{grant.UserAccount?.DisplayName ?? "Learner"} ({grant.UserAccount?.Email ?? "unknown@lms.local"})",
                grant.GrantedAttempts,
                grant.GrantedAt,
                grant.GrantedByAdminId,
                $"{grant.GrantedByAdmin?.DisplayName ?? "Admin"} ({grant.GrantedByAdmin?.Email ?? "unknown@lms.local"})"))
            .ToList();
    }

    public async Task SetRetakeGrantAttemptsAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail)
    {
        if (grantedAttempts <= 0)
        {
            throw new InvalidOperationException("Granted attempts must be greater than zero.");
        }

        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessment = await _dbContext.CourseAssessments
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => new { existing.Id, existing.RetakeCooldownMinutes })
            .FirstOrDefaultAsync();

        if (assessment is null)
        {
            throw new InvalidOperationException("Assessment not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseAssessmentId == assessment.Id && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            grant = new RetakeGrant
            {
                CourseAssessmentId = assessment.Id,
                UserAccountId = learnerUserId,
                GrantedAttempts = grantedAttempts,
                GrantedByAdminId = adminUserId,
                GrantedAt = DateTime.UtcNow
            };

            _dbContext.RetakeGrants.Add(grant);
        }
        else
        {
            grant.GrantedAttempts = grantedAttempts;
            grant.GrantedByAdminId = adminUserId;
            grant.GrantedAt = DateTime.UtcNow;
        }

        if (resetCooldownTimer)
        {
            await ResetRetakeCooldownTimerAsync(assessment.Id, learnerUserId, assessment.RetakeCooldownMinutes);
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "assessment.retake.updated", "CourseAssessment", assessment.Id, $"Learner={learnerUserId}; Attempts={grantedAttempts}; ResetCooldownTimer={resetCooldownTimer}");
    }

    public async Task RevokeRetakeGrantAsync(Guid courseId, Guid learnerUserId, Guid adminUserId, string adminEmail)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessmentId = await _dbContext.CourseAssessments
            .Where(existing => existing.CourseId == courseId && existing.IsRequired)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();

        if (!assessmentId.HasValue)
        {
            throw new InvalidOperationException("Assessment not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseAssessmentId == assessmentId.Value && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            return;
        }

        _dbContext.RetakeGrants.Remove(grant);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "assessment.retake.revoked", "CourseAssessment", assessmentId.Value, $"Learner={learnerUserId}");
    }

    public async Task<bool> HasPassedRequiredAssessmentAsync(Guid courseId, Guid userId)
    {
        await EnsureDefaultAssessmentForCourseAsync(courseId);

        var assessmentId = await _dbContext.CourseAssessments
            .AsNoTracking()
            .Where(assessment => assessment.CourseId == courseId && assessment.IsRequired)
            .Select(assessment => (Guid?)assessment.Id)
            .FirstOrDefaultAsync();

        if (!assessmentId.HasValue)
        {
            return true;
        }

        return await _dbContext.AssessmentAttempts
            .AsNoTracking()
            .AnyAsync(attempt =>
                attempt.CourseAssessmentId == assessmentId.Value &&
                attempt.UserAccountId == userId &&
                attempt.Passed);
    }

    private async Task ResetRetakeCooldownTimerAsync(Guid assessmentId, Guid learnerUserId, int cooldownMinutes)
    {
        var normalizedCooldownMinutes = Math.Max(cooldownMinutes, 0);
        if (normalizedCooldownMinutes <= 0)
        {
            return;
        }

        var latestAttempt = await _dbContext.AssessmentAttempts
            .Where(existing => existing.CourseAssessmentId == assessmentId && existing.UserAccountId == learnerUserId)
            .OrderByDescending(existing => existing.SubmittedAt)
            .FirstOrDefaultAsync();

        if (latestAttempt is null)
        {
            return;
        }

        var thresholdUtc = DateTime.UtcNow.AddMinutes(-(normalizedCooldownMinutes + 1));
        if (latestAttempt.SubmittedAt > thresholdUtc)
        {
            latestAttempt.SubmittedAt = thresholdUtc;
            if (latestAttempt.StartedAt > thresholdUtc)
            {
                latestAttempt.StartedAt = thresholdUtc;
            }
        }
    }

    private static string NormalizeOption(string? rawOption)
    {
        if (string.IsNullOrWhiteSpace(rawOption))
        {
            return string.Empty;
        }

        var candidate = rawOption.Trim().ToUpperInvariant();
        return candidate is "A" or "B" or "C" or "D" ? candidate : string.Empty;
    }

    private static void ValidateEditorQuestion(AssessmentQuestionEditorItem question, int position)
    {
        if (string.IsNullOrWhiteSpace(question.Prompt) ||
            string.IsNullOrWhiteSpace(question.OptionA) ||
            string.IsNullOrWhiteSpace(question.OptionB) ||
            string.IsNullOrWhiteSpace(question.OptionC) ||
            string.IsNullOrWhiteSpace(question.OptionD))
        {
            throw new InvalidOperationException($"Question {position} must include a prompt and all four options.");
        }

        if (NormalizeOption(question.CorrectOption) is not ("A" or "B" or "C" or "D"))
        {
            throw new InvalidOperationException($"Question {position} has an invalid correct option.");
        }
    }

    private sealed record DefaultAssessmentQuestionSpec(string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, string CorrectOption);
}
