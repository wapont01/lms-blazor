using Lms.Application.Data;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Services;

public static class ProctoringDefaults
{
    public const string CompanyName = "WILLIAMS LAND REALTY LLC";
}

public interface IExamService
{
    Task EnsureDefaultExamForCourseAsync(Guid courseId);
    Task<CourseExamView?> GetCourseExamAsync(Guid courseId, Guid? unitId = null);
    Task<Dictionary<Guid, string>> GetCorrectAnswerMapAsync(Guid courseId, Guid? unitId = null);
    Task<ExamEditorModel?> GetExamEditorAsync(Guid courseId);
    Task SaveExamEditorAsync(Guid courseId, ExamEditorModel editor, Guid actorUserId, string actorEmail);
    Task<ExamEligibility> GetEligibilityAsync(Guid courseId, Guid userId, Guid? unitId = null);
    Task<ExamEligibility> GetEligibilityForExamAsync(Guid examId, Guid userId);
    Task<CourseExamView?> GetExamViewAsync(Guid examId);
    Task<CourseExamMenuInfo> CreateUnitExamAsync(Guid courseId, Guid unitId, Guid actorUserId, string actorEmail);
    Task<Dictionary<Guid, string>> GetCorrectAnswerMapForExamAsync(Guid examId);
    Task<ExamAttemptSummary> SubmitAttemptForExamAsync(Guid examId, Guid userId, Dictionary<Guid, string> answersByQuestionId, DateTime? startedAtUtc = null);
    Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryForExamAsync(Guid examId, Guid userId);
    Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryForCourseAsync(Guid courseId, Guid userId);
    Task<List<ExamAttemptAuditRecord>> GetAttemptAuditAsync(Guid? courseId = null, Guid? learnerUserId = null);
    Task<List<CourseExamMenuInfo>> GetCourseMenuExamsAsync(Guid courseId);
    Task<List<SupplementalExamInfo>> GetSupplementalExamsAsync(Guid courseId);
    Task<SupplementalExamInfo> CreateSupplementalExamAsync(Guid courseId, string kind, Guid? unitId, string? title, Guid actorUserId, string actorEmail);
    Task DeleteSupplementalExamAsync(Guid examId, Guid actorUserId, string actorEmail);
    Task<ExamEditorModel?> GetExamEditorByIdAsync(Guid examId);
    Task SaveExamEditorByIdAsync(Guid examId, ExamEditorModel editor, Guid actorUserId, string actorEmail);
    Task<ExamAttemptSummary> SubmitAttemptAsync(Guid courseId, Guid userId, Dictionary<Guid, string> answersByQuestionId, Guid? unitId = null, DateTime? startedAtUtc = null);
    Task<bool> HasPassedRequiredExamAsync(Guid courseId, Guid userId);
    Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryAsync(Guid courseId, Guid userId, Guid? unitId = null);
    Task GrantRetakeAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail);
    Task<List<RetakeGrantView>> GetRetakeGrantsAsync(Guid courseId);
    Task SetRetakeGrantAttemptsAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail);
    Task RevokeRetakeGrantAsync(Guid courseId, Guid learnerUserId, Guid adminUserId, string adminEmail);
    Task<ExamProctoringSessionView> RegisterProctoringSessionAsync(Guid courseId, Guid learnerUserId, string proctorName, string? externalSessionId, int validForMinutes, Guid actorUserId, string actorEmail);
    Task<ExamProctoringSessionView?> GetActiveProctoringSessionAsync(Guid courseId, Guid learnerUserId);
    Task ExpireProctoringSessionAsync(Guid sessionId, Guid actorUserId, string actorEmail);
    Task ReportProctoringIncidentAsync(Guid sessionId, string notes, Guid actorUserId, string actorEmail);
}

public sealed record ExamQuestionView(Guid Id, string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, int OrderIndex, string? HintText);
public sealed record CourseExamView(Guid Id, string Title, decimal PassPercent, bool IsRequired, List<ExamQuestionView> Questions);
public sealed record CourseExamMenuInfo(Guid Id, string Kind, Guid? UnitId, string Title, int OrderIndex, int QuestionCount, int ContentVersion = 1, bool IsRequired = true);
public sealed record SupplementalExamInfo(Guid Id, string Kind, Guid? UnitId, string Title, int OrderIndex, int QuestionCount);
public sealed record SupplementalExamCreateRequest(string Kind, Guid? UnitId);
public sealed record ExamAttemptSummary(DateTime SubmittedAt, decimal ScorePercent, bool Passed, int TotalQuestions, int CorrectAnswers, DateTime? StartedAt = null)
{
    public TimeSpan Duration => StartedAt.HasValue && SubmittedAt >= StartedAt.Value
        ? SubmittedAt - StartedAt.Value
        : TimeSpan.Zero;
}
public sealed record ExamEligibility(
    bool RequiresExam,
    bool HasPassed,
    ExamAttemptSummary? LatestAttempt,
    string? BlockingReason,
    bool HasRetakeGrantOverride,
    int AttemptsUsed,
    int AttemptsAllowed,
    bool HasUnlimitedAttempts,
    bool RequiresProctorVerification);
public sealed record ExamAttemptAnswerReview(
    string Prompt,
    string SelectedOption,
    string CorrectOption,
    bool IsCorrect,
    string? FeedbackText,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null);
public sealed record ExamAttemptHistoryItem(
    int AttemptNumber,
    DateTime SubmittedAt,
    decimal ScorePercent,
    bool Passed,
    List<ExamAttemptAnswerReview> Answers,
    string? FeedbackSummary,
    Guid ExamId = default,
    string ExamTitle = "",
    string ExamKind = CourseExamKinds.Standard,
    string? UnitTitle = null,
    DateTime? StartedAt = null,
    int ExamVersion = 1)
{
    public TimeSpan Duration => StartedAt.HasValue && SubmittedAt >= StartedAt.Value
        ? SubmittedAt - StartedAt.Value
        : TimeSpan.Zero;
}
public sealed record ExamAttemptAuditRecord(
    Guid AttemptId,
    Guid CourseId,
    string CourseTitle,
    Guid ExamId,
    string ExamTitle,
    string ExamKind,
    string? UnitTitle,
    Guid LearnerUserId,
    string LearnerName,
    string LearnerEmail,
    int AttemptNumber,
    DateTime StartedAt,
    DateTime SubmittedAt,
    decimal ScorePercent,
    bool Passed,
    int TotalQuestions,
    int CorrectAnswers,
    int ExamVersion)
{
    public TimeSpan Duration => SubmittedAt >= StartedAt
        ? SubmittedAt - StartedAt
        : TimeSpan.Zero;
}
public sealed record RetakeGrantView(Guid LearnerUserId, string LearnerDisplay, int GrantedAttempts, DateTime GrantedAt, Guid GrantedByAdminId, string GrantedByAdminDisplay);
public sealed record ExamProctoringSessionView(Guid Id, string ProctorName, string? ExternalSessionId, DateTime IdentityVerifiedAtUtc, DateTime ExpiresAtUtc, bool ClosedBookConfirmed);

public sealed class ExamEditorModel
{
    public Guid? ExamId { get; set; }
    public string Title { get; set; } = "Final Exam";
    public decimal PassPercent { get; set; } = 80m;
    public int? MaxRetakesPerLearner { get; set; }
    public int RetakeCooldownMinutes { get; set; }
    public List<ExamQuestionEditorItem> Questions { get; set; } = new();
}

public sealed class ExamQuestionEditorItem
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

public class ExamService : IExamService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;

    private static readonly DefaultExamQuestionSpec[] DefaultExamQuestionSpecs =
    [
        new("What should you review first when planning a license path?", "Office branding", "State requirements and education hours", "Social media templates", "Desk layout", "B"),
        new("Why is brokerage supervision important in early practice?", "It replaces study", "It provides required oversight and authorization", "It removes disclosure rules", "It shortens the exam", "B"),
        new("What should happen when a policy update changes a procedure?", "Ignore it until next year", "Apply it promptly and document the change", "Wait for a client complaint", "Ask a peer to guess", "B"),
        new("Which practice best supports recordkeeping and accountability?", "Keep clear, dated files", "Rely only on memory", "Delete drafts immediately", "Use only verbal confirmation", "A"),
        new("How should you respond to a disclosure request?", "Delay until closing", "Provide it promptly and document delivery", "Avoid the subject", "Wait for the other party to ask again", "B"),
        new("Before working a first client file, what should be confirmed?", "The office paint color", "Supervision, forms, and approval steps", "The marketing slogan", "The break room schedule", "B"),
        new("What is the main purpose of contingencies in a contract?", "Remove all risk", "Define conditions and deadlines", "Replace signatures", "Guarantee commission", "B"),
        new("What should you do if you are unsure about a compliance decision?", "Escalate to a supervisor or responsible party", "Proceed silently", "Randomize the process", "Wait until quarter-end", "A"),
        new("What is the best way to prepare for the exam?", "Memorize one chapter only", "Study by topic and use practice unit exams", "Skip review after one pass", "Avoid feedback", "B"),
        new("What belongs in a first-30-day transaction plan?", "Lead intake scripts, disclosure timing, and supervision touchpoints", "Holiday decorations", "Personal slogans", "Office renovation notes", "A")
    ];

    public ExamService(ApplicationDbContext dbContext, IAuditLogService auditLogService)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
    }

    // Detects whether an exam's questions still exactly match the auto-seeded defaults from
    // EnsureDefaultExamForCourseAsync, so course-readiness checks can require an instructor to
    // actually author their own content rather than submitting the placeholder quiz for review as-is.
    public static bool MatchesDefaultExamContent(CourseExam exam)
    {
        if (exam.Questions.Count != DefaultExamQuestionSpecs.Length)
        {
            return false;
        }

        var orderedQuestions = exam.Questions.OrderBy(question => question.OrderIndex).ToList();
        for (var index = 0; index < DefaultExamQuestionSpecs.Length; index++)
        {
            var spec = DefaultExamQuestionSpecs[index];
            var question = orderedQuestions[index];
            if (question.Prompt != spec.Prompt
                || question.OptionA != spec.OptionA
                || question.OptionB != spec.OptionB
                || question.OptionC != spec.OptionC
                || question.OptionD != spec.OptionD
                || question.CorrectOption != spec.CorrectOption)
            {
                return false;
            }
        }

        return true;
    }

    public async Task EnsureDefaultExamForCourseAsync(Guid courseId)
    {
        var defaultTitle = await _dbContext.Courses
            .Where(course => course.Id == courseId)
            .Select(course => $"{course.Title} Final Exam")
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(defaultTitle))
        {
            return;
        }

        var exam = await _dbContext.CourseExams
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null);

        if (exam is null)
        {
            exam = new CourseExam
            {
                CourseId = courseId,
                UnitId = null,
                Title = defaultTitle,
                PassPercent = 80m,
                IsRequired = true
            };

            _dbContext.CourseExams.Add(exam);
        }

        var shouldSeedDefaultContent = exam.Questions.Count == 0;

        if (exam.Questions.Count > 0 && !shouldSeedDefaultContent)
        {
            return;
        }

        exam.Title = defaultTitle;
        exam.PassPercent = 80m;
        exam.IsRequired = true;
        exam.MaxRetakesPerLearner = null;
        exam.RetakeCooldownMinutes = 0;

        var orderedQuestions = exam.Questions.OrderBy(question => question.OrderIndex).ToList();
        for (var index = 0; index < DefaultExamQuestionSpecs.Length; index++)
        {
            var spec = DefaultExamQuestionSpecs[index];
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

            exam.Questions.Add(new ExamQuestion
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

        if (orderedQuestions.Count > DefaultExamQuestionSpecs.Length)
        {
            _dbContext.ExamQuestions.RemoveRange(orderedQuestions.Skip(DefaultExamQuestionSpecs.Length));
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

    public async Task<CourseExamView?> GetCourseExamAsync(Guid courseId, Guid? unitId = null)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await ResolveStandardExamIdAsync(courseId, unitId);
        return examId is null ? null : await GetExamViewAsync(examId.Value);
    }

    public async Task<CourseExamView?> GetExamViewAsync(Guid examId)
    {
        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.Id == examId);

        if (exam is null)
        {
            return null;
        }

        return new CourseExamView(
            exam.Id,
            exam.Title,
            exam.PassPercent,
            exam.IsRequired,
            exam.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new ExamQuestionView(
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

    public async Task<Dictionary<Guid, string>> GetCorrectAnswerMapAsync(Guid courseId, Guid? unitId = null)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await ResolveStandardExamIdAsync(courseId, unitId);
        return examId is null ? new Dictionary<Guid, string>() : await GetCorrectAnswerMapForExamAsync(examId.Value);
    }

    public async Task<Dictionary<Guid, string>> GetCorrectAnswerMapForExamAsync(Guid examId)
    {
        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.Id == examId);

        if (exam is null)
        {
            return new Dictionary<Guid, string>();
        }

        return exam.Questions
            .OrderBy(question => question.OrderIndex)
            .ToDictionary(question => question.Id, question => question.CorrectOption);
    }

    public async Task<ExamEditorModel?> GetExamEditorAsync(Guid courseId)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null);

        if (exam is null)
        {
            return null;
        }

        return new ExamEditorModel
        {
            ExamId = exam.Id,
            Title = exam.Title,
            PassPercent = exam.PassPercent,
            MaxRetakesPerLearner = exam.MaxRetakesPerLearner,
            RetakeCooldownMinutes = exam.RetakeCooldownMinutes,
            Questions = exam.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new ExamQuestionEditorItem
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

    public async Task SaveExamEditorAsync(Guid courseId, ExamEditorModel editor, Guid actorUserId, string actorEmail)
    {
        if (editor.Questions.Count == 0)
        {
            throw new InvalidOperationException("Exam must include at least one question.");
        }

        var exam = await _dbContext.CourseExams
            .Include(existing => existing.Questions)
            .Include(existing => existing.Attempts)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null);

        if (exam is null)
        {
            exam = new CourseExam
            {
                CourseId = courseId,
                IsRequired = true
            };

            _dbContext.CourseExams.Add(exam);
        }

        await ApplyExamEditorAsync(exam, courseId, editor, actorUserId, actorEmail);
    }

    public async Task<ExamEditorModel?> GetExamEditorByIdAsync(Guid examId)
    {
        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.Id == examId);

        return exam is null ? null : ToEditorModel(exam);
    }

    public async Task SaveExamEditorByIdAsync(Guid examId, ExamEditorModel editor, Guid actorUserId, string actorEmail)
    {
        if (editor.Questions.Count == 0)
        {
            throw new InvalidOperationException("Exam must include at least one question.");
        }

        var exam = await _dbContext.CourseExams
            .Include(existing => existing.Questions)
            .Include(existing => existing.Attempts)
            .FirstOrDefaultAsync(existing => existing.Id == examId)
            ?? throw new InvalidOperationException("Exam not found.");

        if (exam.Kind == CourseExamKinds.Standard && exam.UnitId is null)
        {
            throw new InvalidOperationException("The course Final Exam must be edited from the Final Exam editor.");
        }

        await ApplyExamEditorAsync(exam, exam.CourseId, editor, actorUserId, actorEmail);
    }

    public async Task<CourseExamMenuInfo> CreateUnitExamAsync(Guid courseId, Guid unitId, Guid actorUserId, string actorEmail)
    {
        var course = await _dbContext.Courses.AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.Id == courseId)
            ?? throw new InvalidOperationException("Course not found.");
        var unit = await _dbContext.Units.AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.Id == unitId && existing.CourseId == courseId)
            ?? throw new InvalidOperationException("Unit not found in this course.");

        var existingExam = await _dbContext.CourseExams
            .FirstOrDefaultAsync(exam => exam.CourseId == courseId
                && exam.UnitId == unitId
                && exam.Kind == CourseExamKinds.Standard);
        if (existingExam is not null)
        {
            throw new InvalidOperationException("This unit already has a Unit Exam.");
        }

        var finalExamPassPercent = await _dbContext.CourseExams
            .Where(exam => exam.CourseId == courseId
                && exam.UnitId == null
                && exam.Kind == CourseExamKinds.Standard)
            .Select(exam => (decimal?)exam.PassPercent)
            .FirstOrDefaultAsync();
        var exam = new CourseExam
        {
            CourseId = course.Id,
            UnitId = unit.Id,
            Kind = CourseExamKinds.Standard,
            Title = $"{unit.Title} Unit Exam",
            PassPercent = finalExamPassPercent ?? 80m,
            IsRequired = true
        };

        _dbContext.CourseExams.Add(exam);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.created", "CourseExam", exam.Id,
            $"CourseId={courseId}; Kind={exam.Kind}; UnitId={unitId}");

        return new CourseExamMenuInfo(exam.Id, exam.Kind, exam.UnitId, exam.Title, exam.OrderIndex, 0);
    }

    public async Task<List<CourseExamMenuInfo>> GetCourseMenuExamsAsync(Guid courseId)
    {
        return await _dbContext.CourseExams
            .AsNoTracking()
            .Where(exam => exam.CourseId == courseId)
            .OrderBy(exam => exam.UnitId == null ? 1 : 0)
            .ThenBy(exam => exam.UnitId)
            .ThenBy(exam => exam.OrderIndex)
            .Select(exam => new CourseExamMenuInfo(exam.Id, exam.Kind, exam.UnitId, exam.Title, exam.OrderIndex, exam.Questions.Count, exam.ContentVersion, exam.IsRequired))
            .ToListAsync();
    }

    public async Task<List<SupplementalExamInfo>> GetSupplementalExamsAsync(Guid courseId)
    {
        return await _dbContext.CourseExams
            .AsNoTracking()
            .Where(exam => exam.CourseId == courseId && exam.Kind != CourseExamKinds.Standard)
            .OrderBy(exam => exam.OrderIndex)
            .ThenBy(exam => exam.Title)
            .Select(exam => new SupplementalExamInfo(exam.Id, exam.Kind, exam.UnitId, exam.Title, exam.OrderIndex, exam.Questions.Count))
            .ToListAsync();
    }

    public async Task<SupplementalExamInfo> CreateSupplementalExamAsync(Guid courseId, string kind, Guid? unitId, string? title, Guid actorUserId, string actorEmail)
    {
        if (kind != CourseExamKinds.Intermediate && kind != CourseExamKinds.Practice)
        {
            throw new InvalidOperationException("Only Intermediate and Practice exams can be added here.");
        }

        var course = await _dbContext.Courses.AsNoTracking().FirstOrDefaultAsync(existing => existing.Id == courseId)
            ?? throw new InvalidOperationException("Course not found.");
        await EnsureDefaultExamForCourseAsync(courseId);

        if (kind == CourseExamKinds.Intermediate)
        {
            if (unitId is null)
            {
                throw new InvalidOperationException("An Intermediate Exam must follow a unit.");
            }

            var unitExists = await _dbContext.Units.AnyAsync(unit => unit.Id == unitId && unit.CourseId == courseId);
            if (!unitExists)
            {
                throw new InvalidOperationException("Unit not found in this course.");
            }
        }
        else
        {
            unitId = null;
            if (await _dbContext.CourseExams.AnyAsync(exam => exam.CourseId == courseId && exam.Kind == CourseExamKinds.Practice))
            {
                throw new InvalidOperationException("This course already has a Practice Exam.");
            }
        }

        var nextOrder = (await _dbContext.CourseExams
            .Where(exam => exam.CourseId == courseId && exam.Kind == kind)
            .Select(exam => (int?)exam.OrderIndex)
            .MaxAsync() ?? 0) + 1;

        var finalExam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.CourseId == courseId && existing.IsRequired && existing.Kind == CourseExamKinds.Standard && existing.UnitId == null);

        var exam = new CourseExam
        {
            CourseId = courseId,
            UnitId = unitId,
            Kind = kind,
            OrderIndex = nextOrder,
            Title = string.IsNullOrWhiteSpace(title)
                ? (kind == CourseExamKinds.Practice ? "Practice Exam" : $"Intermediate Exam {nextOrder}")
                : title.Trim(),
            PassPercent = finalExam?.PassPercent ?? 80m,
            IsRequired = kind == CourseExamKinds.Intermediate,
            MaxRetakesPerLearner = kind == CourseExamKinds.Intermediate ? finalExam?.MaxRetakesPerLearner : null,
            RetakeCooldownMinutes = kind == CourseExamKinds.Intermediate ? finalExam?.RetakeCooldownMinutes ?? 0 : 0
        };

        // A Practice Exam mimics the Final Exam, so it starts as a copy of its questions.
        if (kind == CourseExamKinds.Practice && finalExam is not null)
        {
            foreach (var question in finalExam.Questions.OrderBy(q => q.OrderIndex))
            {
                exam.Questions.Add(new ExamQuestion
                {
                    Prompt = question.Prompt,
                    OptionA = question.OptionA,
                    OptionB = question.OptionB,
                    OptionC = question.OptionC,
                    OptionD = question.OptionD,
                    CorrectOption = question.CorrectOption,
                    FeedbackText = question.FeedbackText,
                    OrderIndex = question.OrderIndex
                });
            }
        }

        _dbContext.CourseExams.Add(exam);
        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.created", "CourseExam", exam.Id,
            $"CourseId={courseId}; Kind={kind}; UnitId={unitId}");

        return new SupplementalExamInfo(exam.Id, exam.Kind, exam.UnitId, exam.Title, exam.OrderIndex, exam.Questions.Count);
    }

    public async Task DeleteSupplementalExamAsync(Guid examId, Guid actorUserId, string actorEmail)
    {
        var exam = await _dbContext.CourseExams.FirstOrDefaultAsync(existing => existing.Id == examId)
            ?? throw new InvalidOperationException("Exam not found.");

        if (exam.Kind == CourseExamKinds.Standard)
        {
            throw new InvalidOperationException("Only Intermediate and Practice exams can be deleted here.");
        }

        if (await _dbContext.ExamAttempts.AnyAsync(attempt => attempt.CourseExamId == examId))
        {
            throw new InvalidOperationException("An exam with recorded attempts cannot be deleted. Retain it for audit history.");
        }

        _dbContext.CourseExams.Remove(exam);
        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.deleted", "CourseExam", examId,
            $"CourseId={exam.CourseId}; Kind={exam.Kind}");
    }

    private static ExamEditorModel ToEditorModel(CourseExam exam) => new()
    {
        ExamId = exam.Id,
        Title = exam.Title,
        PassPercent = exam.PassPercent,
        MaxRetakesPerLearner = exam.MaxRetakesPerLearner,
        RetakeCooldownMinutes = exam.RetakeCooldownMinutes,
        Questions = exam.Questions
            .OrderBy(question => question.OrderIndex)
            .Select(question => new ExamQuestionEditorItem
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

    private async Task ApplyExamEditorAsync(CourseExam exam, Guid courseId, ExamEditorModel editor, Guid actorUserId, string actorEmail)
    {
        var defaultTitle = exam.Kind switch
        {
            CourseExamKinds.Practice => "Practice Exam",
            CourseExamKinds.Intermediate => "Intermediate Exam",
            _ when exam.UnitId.HasValue => "Unit Exam",
            _ => "Final Exam"
        };

        var updatedTitle = string.IsNullOrWhiteSpace(editor.Title)
            ? defaultTitle
            : editor.Title.Trim();
        var existingQuestionsById = exam.Questions.ToDictionary(question => question.Id, question => question);
        var updatedPassPercent = Math.Clamp(editor.PassPercent, 0m, 100m);
        var updatedMaxRetakes = editor.MaxRetakesPerLearner is null or <= 0 ? null : editor.MaxRetakesPerLearner;
        var updatedCooldown = Math.Max(editor.RetakeCooldownMinutes, 0);
        var contentChanged = exam.Title != updatedTitle
            || exam.PassPercent != updatedPassPercent
            || exam.MaxRetakesPerLearner != updatedMaxRetakes
            || exam.RetakeCooldownMinutes != updatedCooldown
            || exam.Questions.Count != editor.Questions.Count;

        for (var index = 0; index < editor.Questions.Count; index++)
        {
            var input = editor.Questions[index];
            ValidateEditorQuestion(input, index + 1);
            if (!input.QuestionId.HasValue || !existingQuestionsById.TryGetValue(input.QuestionId.Value, out var oldQuestion)
                || oldQuestion.Prompt != input.Prompt.Trim()
                || oldQuestion.OptionA != input.OptionA.Trim()
                || oldQuestion.OptionB != input.OptionB.Trim()
                || oldQuestion.OptionC != input.OptionC.Trim()
                || oldQuestion.OptionD != input.OptionD.Trim()
                || oldQuestion.CorrectOption != NormalizeOption(input.CorrectOption)
                || oldQuestion.FeedbackText != (string.IsNullOrWhiteSpace(input.FeedbackText) ? null : input.FeedbackText.Trim())
                || oldQuestion.OrderIndex != index + 1)
            {
                contentChanged = true;
            }
        }

        exam.Title = updatedTitle;
        exam.PassPercent = updatedPassPercent;
        exam.MaxRetakesPerLearner = updatedMaxRetakes;
        exam.RetakeCooldownMinutes = updatedCooldown;
        var retainedQuestionIds = new HashSet<Guid>();

        for (var index = 0; index < editor.Questions.Count; index++)
        {
            var input = editor.Questions[index];
            ValidateEditorQuestion(input, index + 1);

            ExamQuestion question;
            if (input.QuestionId.HasValue && existingQuestionsById.TryGetValue(input.QuestionId.Value, out var existingQuestion))
            {
                question = existingQuestion;
            }
            else
            {
                question = new ExamQuestion
                {
                    CourseExam = exam
                };

                exam.Questions.Add(question);
                _dbContext.ExamQuestions.Add(question);
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

        var removedQuestions = exam.Questions
            .Where(question => !retainedQuestionIds.Contains(question.Id))
            .ToList();

        if (removedQuestions.Count > 0)
        {
            var removedQuestionIds = removedQuestions.Select(question => question.Id).ToList();
            var historicalAnswers = await _dbContext.ExamAnswers
                .Where(answer => answer.ExamQuestionId.HasValue && removedQuestionIds.Contains(answer.ExamQuestionId.Value))
                .ToListAsync();
            foreach (var answer in historicalAnswers)
            {
                answer.ExamQuestionId = null;
            }

            _dbContext.ExamQuestions.RemoveRange(removedQuestions);
        }

        if (contentChanged)
        {
            exam.ContentVersion += 1;
            if (exam.Kind != CourseExamKinds.Practice)
            {
                var issuedCertificates = await _dbContext.CompletionCertificates
                    .Where(certificate => certificate.CourseId == courseId && !certificate.IsRevoked)
                    .ToListAsync();
                foreach (var certificate in issuedCertificates)
                {
                    certificate.IsRevoked = true;
                    certificate.RevokedAt = DateTime.UtcNow;
                    certificate.RevocationReason = "Exam updated. Student must pass the latest required exam.";
                }
            }
        }

        await _dbContext.SaveChangesAsync();

        await _auditLogService.WriteAsync(
            actorUserId,
            actorEmail,
            "exam.updated",
            "CourseExam",
            exam.Id,
            $"CourseId={courseId}; Questions={editor.Questions.Count}; PassPercent={exam.PassPercent:0.##}");
    }

    public async Task<ExamEligibility> GetEligibilityAsync(Guid courseId, Guid userId, Guid? unitId = null)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var courseExists = await _dbContext.Courses.AsNoTracking().AnyAsync(existing => existing.Id == courseId);
        if (!courseExists)
        {
            return new ExamEligibility(false, false, null, "Course not found.", false, 0, 0, false, false);
        }

        var examId = await ResolveStandardExamIdAsync(courseId, unitId);
        if (examId is null)
        {
            return new ExamEligibility(false, true, null, null, false, 0, 0, false, false);
        }

        return await GetEligibilityForExamAsync(examId.Value, userId);
    }

    private async Task<Guid?> ResolveStandardExamIdAsync(Guid courseId, Guid? unitId)
    {
        return await _dbContext.CourseExams
            .AsNoTracking()
            .Where(existing => existing.CourseId == courseId
                && existing.IsRequired
                && existing.Kind == CourseExamKinds.Standard
                && existing.UnitId == unitId)
            .OrderBy(existing => existing.Title)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();
    }

    public async Task<ExamEligibility> GetEligibilityForExamAsync(Guid examId, Guid userId)
    {
        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Where(existing => existing.Id == examId)
            .Select(existing => new { existing.Id, existing.CourseId, existing.Kind, existing.IsRequired, existing.MaxRetakesPerLearner, existing.RetakeCooldownMinutes, existing.PassPercent, existing.ContentVersion })
            .FirstOrDefaultAsync();

        if (exam is null)
        {
            return new ExamEligibility(false, false, null, "Exam not found.", false, 0, 0, false, false);
        }

        var courseId = exam.CourseId;
        var course = await _dbContext.Courses
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.Id == courseId);

        if (course is null)
        {
            return new ExamEligibility(false, false, null, "Course not found.", false, 0, 0, false, false);
        }

        if (string.Equals(exam.Kind, CourseExamKinds.Practice, StringComparison.Ordinal))
        {
            var practiceLatest = await _dbContext.ExamAttempts
                .AsNoTracking()
                .Where(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion)
                .OrderByDescending(attempt => attempt.SubmittedAt)
                .Select(attempt => new ExamAttemptSummary(
                    attempt.SubmittedAt,
                    attempt.ScorePercent,
                    attempt.Passed,
                    attempt.Answers.Count,
                    attempt.Answers.Count(answer => answer.IsCorrect),
                    attempt.StartedAt))
                .FirstOrDefaultAsync();
            var practiceAttempts = await _dbContext.ExamAttempts
                .AsNoTracking()
                .CountAsync(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion);
            var practicePassed = await _dbContext.ExamAttempts
                .AsNoTracking()
                .AnyAsync(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion && attempt.Passed);

            // Practice is never gated: unlimited attempts, no cooldown, no proctoring.
            return new ExamEligibility(true, practicePassed, practiceLatest, null, false, practiceAttempts, practiceAttempts + 1, true, false);
        }

        if (!exam.IsRequired)
        {
            return new ExamEligibility(false, true, null, null, false, 0, 0, false, false);
        }

        var requiredPassPercent = course.UsesUnitBasedStructure ? course.MinimumPassingPercent : exam.PassPercent;

        var latestAttempt = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Where(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion)
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .Select(attempt => new ExamAttemptSummary(
                attempt.SubmittedAt,
                attempt.ScorePercent,
                attempt.Passed,
                attempt.Answers.Count,
                attempt.Answers.Count(answer => answer.IsCorrect),
                attempt.StartedAt))
            .FirstOrDefaultAsync();

        var hasPassed = await _dbContext.ExamAttempts
            .AsNoTracking()
            .AnyAsync(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion && attempt.Passed);
        var attemptsUsed = await _dbContext.ExamAttempts
            .AsNoTracking()
            .CountAsync(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion);

        var firstAttemptPassed = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Where(attempt => attempt.CourseExamId == exam.Id && attempt.UserAccountId == userId && attempt.ExamVersion == exam.ContentVersion)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ThenBy(attempt => attempt.SubmittedAt)
            .Select(attempt => (bool?)attempt.Passed)
            .FirstOrDefaultAsync();

        var grant = await _dbContext.RetakeGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == userId);
        var hasRetakeGrantOverride = grant is not null && grant.GrantedAttempts > 0;
        var effectiveHasPassed = hasPassed && !hasRetakeGrantOverride;
        var requiresProctoredExam = course.RequiresProctoredExam;
        var hasActiveProctoringSession = !requiresProctoredExam || await HasActiveProctoringSessionAsync(courseId, userId);

        var baseAttemptsAllowed = GetBaseAttemptsAllowed(course, exam.MaxRetakesPerLearner, firstAttemptPassed, hasRetakeGrantOverride);
        var hasUnlimitedAttempts = baseAttemptsAllowed is null;
        var attemptsAllowed = hasUnlimitedAttempts
            ? attemptsUsed + 1
            : baseAttemptsAllowed!.Value + (grant?.GrantedAttempts ?? 0);

        string? blockingReason;

        if (effectiveHasPassed)
        {
            blockingReason = null;
        }
        else if (!hasActiveProctoringSession)
        {
            blockingReason = "Identity verification and a closed-book proctoring session are required before beginning this exam.";
        }
        else if (latestAttempt is null)
        {
            blockingReason = $"Progress is gated until this required exam is passed at {requiredPassPercent}% or higher.";
        }
        else if (!hasUnlimitedAttempts && attemptsUsed >= attemptsAllowed)
        {
            blockingReason = "Retake limit reached. Contact an admin for additional attempts.";
        }
        else
        {
            var nextAttemptAtUtc = latestAttempt.SubmittedAt.AddMinutes(Math.Max(exam.RetakeCooldownMinutes, 0));
            blockingReason = nextAttemptAtUtc > DateTime.UtcNow
                ? $"Retake available at {nextAttemptAtUtc:u}."
                : $"Latest exam attempt did not pass. Submit another attempt to unlock certification at {requiredPassPercent}% or higher.";
        }

        return new ExamEligibility(
            true,
            effectiveHasPassed,
            latestAttempt,
            blockingReason,
            hasRetakeGrantOverride,
            attemptsUsed,
            attemptsAllowed,
            hasUnlimitedAttempts,
            requiresProctoredExam && !hasActiveProctoringSession);
    }

    public async Task<ExamAttemptSummary> SubmitAttemptAsync(
        Guid courseId,
        Guid userId,
        Dictionary<Guid, string> answersByQuestionId,
        Guid? unitId = null,
        DateTime? startedAtUtc = null)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await ResolveStandardExamIdAsync(courseId, unitId);
        if (examId is null)
        {
            throw new InvalidOperationException("No required exam was found for this course.");
        }

        return await SubmitAttemptForExamAsync(examId.Value, userId, answersByQuestionId, startedAtUtc);
    }

    public async Task<ExamAttemptSummary> SubmitAttemptForExamAsync(
        Guid examId,
        Guid userId,
        Dictionary<Guid, string> answersByQuestionId,
        DateTime? startedAtUtc = null)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        var exam = await _dbContext.CourseExams
            .AsNoTracking()
            .Include(existing => existing.Questions)
            .FirstOrDefaultAsync(existing => existing.Id == examId);

        if (exam is null)
        {
            throw new InvalidOperationException("No required exam was found for this course.");
        }

        var courseId = exam.CourseId;
        var isPractice = string.Equals(exam.Kind, CourseExamKinds.Practice, StringComparison.Ordinal);

        var course = await _dbContext.Courses
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.Id == courseId);
        ExamProctoringSession? proctoringSession = null;
        if (!isPractice && course?.RequiresProctoredExam == true)
        {
            proctoringSession = await _dbContext.ExamProctoringSessions
                .Where(session =>
                    session.CourseId == courseId
                    && session.UserAccountId == userId
                    && session.UsedAtUtc == null
                    && session.ClosedBookConfirmed
                    && !session.SecurityIncidentReported
                    && session.ExpiresAtUtc >= DateTime.UtcNow)
                .OrderByDescending(session => session.IdentityVerifiedAtUtc)
                .FirstOrDefaultAsync();

            if (proctoringSession is null)
            {
                throw new InvalidOperationException("A verified, closed-book proctoring session is required before submitting this exam.");
            }
        }

        var orderedQuestions = exam.Questions.OrderBy(question => question.OrderIndex).ToList();
        if (orderedQuestions.Count == 0)
        {
            throw new InvalidOperationException("Exam has no questions.");
        }

        var existingAttempts = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Where(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == userId)
            .Where(existing => existing.ExamVersion == exam.ContentVersion)
            .OrderByDescending(existing => existing.SubmittedAt)
            .ToListAsync();

        if (startedAtUtc.HasValue && existingAttempts.Any(attempt => attempt.StartedAt == startedAtUtc.Value))
        {
            throw new InvalidOperationException("This exam session has already been submitted. Refresh your exam history before starting another attempt.");
        }

        var attemptCount = existingAttempts.Count;
        var latestAttempt = existingAttempts.FirstOrDefault();
        var attemptNumber = await _dbContext.ExamAttempts
            .CountAsync(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == userId) + 1;

        var grant = await _dbContext.RetakeGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == userId);

        var firstAttempt = existingAttempts
            .OrderBy(existing => existing.AttemptNumber)
            .ThenBy(existing => existing.SubmittedAt)
            .FirstOrDefault();
        var hasRetakeGrantOverride = grant is not null && grant.GrantedAttempts > 0;
        var baseAttemptsAllowed = GetBaseAttemptsAllowed(course, exam.MaxRetakesPerLearner, firstAttempt?.Passed, hasRetakeGrantOverride);
        var maxAttemptsAllowed = baseAttemptsAllowed is null
            ? (int?)null
            : baseAttemptsAllowed.Value + (grant?.GrantedAttempts ?? 0);

        if (!isPractice && maxAttemptsAllowed.HasValue && attemptCount >= maxAttemptsAllowed.Value)
        {
            throw new InvalidOperationException("Retake limit reached. Contact an admin for additional attempts.");
        }

        var cooldownMinutes = isPractice ? 0 : Math.Max(exam.RetakeCooldownMinutes, 0);
        if (latestAttempt is not null && cooldownMinutes > 0)
        {
            var nextAttemptAtUtc = latestAttempt.SubmittedAt.AddMinutes(cooldownMinutes);
            if (nextAttemptAtUtc > DateTime.UtcNow)
            {
                throw new InvalidOperationException($"Retake is on cooldown until {nextAttemptAtUtc:u}.");
            }
        }

        var submittedAtUtc = DateTime.UtcNow;
        var attemptStartedAtUtc = startedAtUtc.HasValue && startedAtUtc.Value <= submittedAtUtc
            ? startedAtUtc.Value
            : submittedAtUtc;
        var unitTitle = exam.UnitId.HasValue
            ? await _dbContext.Units
                .Where(unit => unit.Id == exam.UnitId.Value)
                .Select(unit => unit.Title)
                .FirstOrDefaultAsync()
            : null;
        var attempt = new ExamAttempt
        {
            CourseExamId = exam.Id,
            UserAccountId = userId,
            ExamVersion = exam.ContentVersion,
            StartedAt = attemptStartedAtUtc,
            SubmittedAt = submittedAtUtc,
            AttemptNumber = attemptNumber,
            ExamProctoringSessionId = proctoringSession?.Id,
            CourseTitleSnapshot = course?.Title,
            ExamTitleSnapshot = exam.Title,
            ExamKindSnapshot = exam.Kind,
            UnitTitleSnapshot = unitTitle
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

            attempt.Answers.Add(new ExamAnswer
            {
                ExamQuestionId = question.Id,
                QuestionOrderSnapshot = question.OrderIndex,
                SelectedOption = string.IsNullOrWhiteSpace(selected) ? "-" : selected,
                IsCorrect = isCorrect,
                PromptSnapshot = question.Prompt,
                OptionASnapshot = question.OptionA,
                OptionBSnapshot = question.OptionB,
                OptionCSnapshot = question.OptionC,
                OptionDSnapshot = question.OptionD,
                CorrectOptionSnapshot = question.CorrectOption,
                FeedbackTextSnapshot = question.FeedbackText
            });
        }

        var totalQuestions = orderedQuestions.Count;
        var score = Math.Round(100m * correctAnswers / totalQuestions, 2, MidpointRounding.AwayFromZero);
        var requiredPassPercent = course is not null && course.UsesUnitBasedStructure ? course.MinimumPassingPercent : exam.PassPercent;
        var passed = score >= requiredPassPercent;

        attempt.ScorePercent = score;
        attempt.Passed = passed;
        attempt.FeedbackSummary = passed
            ? "Pass. Exam requirements are satisfied."
            : $"Not passed yet. You need {requiredPassPercent}% or higher to meet the course requirement.";

        _dbContext.ExamAttempts.Add(attempt);
        if (proctoringSession is not null)
        {
            proctoringSession.UsedAtUtc = attempt.SubmittedAt;
        }
        await _dbContext.SaveChangesAsync();

        var actorEmail = await _dbContext.UserAccounts
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync();

        await _auditLogService.WriteAsync(
            userId,
            actorEmail,
            isPractice ? "exam.practice.attempt" : passed ? "exam.attempt.passed" : "exam.attempt.failed",
            "CourseExam",
            exam.Id,
            $"Score={score:0.##}; PassPercent={exam.PassPercent:0.##}");

        await transaction.CommitAsync();
        return new ExamAttemptSummary(attempt.SubmittedAt, score, passed, totalQuestions, correctAnswers, attempt.StartedAt);
    }

    private static int? GetBaseAttemptsAllowed(Course? course, int? maxRetakesPerLearner, bool? firstAttemptPassed, bool hasRetakeGrantOverride)
    {
        if (firstAttemptPassed == true && !hasRetakeGrantOverride)
        {
            return 1;
        }

        if (maxRetakesPerLearner.HasValue)
        {
            return 1 + maxRetakesPerLearner.Value;
        }

        return string.Equals(course?.ComplianceType, CourseComplianceTypes.Postlicensing, StringComparison.OrdinalIgnoreCase)
            ? null
            : 2;
    }

    public async Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryAsync(Guid courseId, Guid userId, Guid? unitId = null)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await ResolveStandardExamIdAsync(courseId, unitId);
        if (!examId.HasValue)
        {
            return [];
        }

        var currentVersion = await _dbContext.CourseExams
            .Where(exam => exam.Id == examId.Value)
            .Select(exam => exam.ContentVersion)
            .SingleAsync();
        var history = await GetAttemptHistoryForExamAsync(examId.Value, userId);
        return history.Where(attempt => attempt.ExamVersion == currentVersion).ToList();
    }

    public async Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryForCourseAsync(Guid courseId, Guid userId)
    {
        var attempts = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Include(attempt => attempt.CourseExam)
                .ThenInclude(exam => exam!.Course)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.ExamQuestion)
            .Where(attempt => attempt.CourseExam != null
                && attempt.CourseExam.CourseId == courseId
                && attempt.UserAccountId == userId)
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ToListAsync();

        return attempts.Select(ToHistoryItem).ToList();
    }

    public async Task<List<ExamAttemptHistoryItem>> GetAttemptHistoryForExamAsync(Guid examId, Guid userId)
    {
        var attempts = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Include(attempt => attempt.CourseExam)
                .ThenInclude(exam => exam!.Course)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.ExamQuestion)
            .Where(attempt => attempt.CourseExamId == examId && attempt.UserAccountId == userId)
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ToListAsync();

        return attempts.Select(ToHistoryItem).ToList();
    }

    public async Task<List<ExamAttemptAuditRecord>> GetAttemptAuditAsync(Guid? courseId = null, Guid? learnerUserId = null)
    {
        var query = _dbContext.ExamAttempts
            .AsNoTracking()
            .Include(attempt => attempt.CourseExam)
                .ThenInclude(exam => exam!.Course)
            .Include(attempt => attempt.UserAccount)
            .Include(attempt => attempt.Answers)
            .AsQueryable();

        if (courseId.HasValue)
        {
            query = query.Where(attempt => attempt.CourseExam != null && attempt.CourseExam.CourseId == courseId.Value);
        }

        if (learnerUserId.HasValue)
        {
            query = query.Where(attempt => attempt.UserAccountId == learnerUserId.Value);
        }

        var attempts = await query
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ToListAsync();

        return attempts.Select(attempt =>
        {
            var exam = attempt.CourseExam;
            var learner = attempt.UserAccount;
            return new ExamAttemptAuditRecord(
                attempt.Id,
                exam?.CourseId ?? Guid.Empty,
                attempt.CourseTitleSnapshot ?? exam?.Course?.Title ?? "Course",
                attempt.CourseExamId,
                attempt.ExamTitleSnapshot ?? exam?.Title ?? "Exam",
                attempt.ExamKindSnapshot ?? exam?.Kind ?? CourseExamKinds.Standard,
                attempt.UnitTitleSnapshot,
                attempt.UserAccountId,
                learner?.DisplayName ?? "Student",
                learner?.Email ?? string.Empty,
                attempt.AttemptNumber,
                attempt.StartedAt,
                attempt.SubmittedAt,
                attempt.ScorePercent,
                attempt.Passed,
                attempt.Answers.Count,
                attempt.Answers.Count(answer => answer.IsCorrect),
                attempt.ExamVersion);
        }).ToList();
    }

    private static ExamAttemptHistoryItem ToHistoryItem(ExamAttempt attempt)
    {
        var exam = attempt.CourseExam;
        return new ExamAttemptHistoryItem(
            attempt.AttemptNumber,
            attempt.SubmittedAt,
            attempt.ScorePercent,
            attempt.Passed,
            attempt.Answers
                .OrderBy(answer => answer.QuestionOrderSnapshot)
                .Select(answer => new ExamAttemptAnswerReview(
                    answer.PromptSnapshot ?? answer.ExamQuestion?.Prompt ?? "Question",
                    answer.SelectedOption,
                    answer.CorrectOptionSnapshot ?? answer.ExamQuestion?.CorrectOption ?? "-",
                    answer.IsCorrect,
                    answer.FeedbackTextSnapshot ?? answer.ExamQuestion?.FeedbackText,
                    answer.OptionASnapshot ?? answer.ExamQuestion?.OptionA,
                    answer.OptionBSnapshot ?? answer.ExamQuestion?.OptionB,
                    answer.OptionCSnapshot ?? answer.ExamQuestion?.OptionC,
                    answer.OptionDSnapshot ?? answer.ExamQuestion?.OptionD))
                .ToList(),
            attempt.FeedbackSummary,
            attempt.CourseExamId,
            attempt.ExamTitleSnapshot ?? exam?.Title ?? "Exam",
            attempt.ExamKindSnapshot ?? exam?.Kind ?? CourseExamKinds.Standard,
            attempt.UnitTitleSnapshot,
            attempt.StartedAt,
            attempt.ExamVersion);
    }

    public async Task GrantRetakeAsync(Guid courseId, Guid learnerUserId, int grantedAttempts, bool resetCooldownTimer, Guid adminUserId, string adminEmail)
    {
        if (grantedAttempts <= 0)
        {
            throw new InvalidOperationException("Granted attempts must be greater than zero.");
        }

        await EnsureDefaultExamForCourseAsync(courseId);

        var exam = await _dbContext.CourseExams
            .Where(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null)
            .Select(existing => new { existing.Id, existing.RetakeCooldownMinutes })
            .FirstOrDefaultAsync();

        if (exam is null)
        {
            throw new InvalidOperationException("Exam not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            grant = new RetakeGrant
            {
                CourseExamId = exam.Id,
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
            await ResetRetakeCooldownTimerAsync(exam.Id, learnerUserId, exam.RetakeCooldownMinutes);
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "exam.retake.granted", "CourseExam", exam.Id, $"Learner={learnerUserId}; Attempts={grantedAttempts}; ResetCooldownTimer={resetCooldownTimer}");
    }

    public async Task<List<RetakeGrantView>> GetRetakeGrantsAsync(Guid courseId)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await _dbContext.CourseExams
            .AsNoTracking()
            .Where(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();

        if (!examId.HasValue)
        {
            return [];
        }

        var grants = await _dbContext.RetakeGrants
            .AsNoTracking()
            .Include(grant => grant.UserAccount)
            .Include(grant => grant.GrantedByAdmin)
            .Where(grant => grant.CourseExamId == examId.Value)
            .OrderByDescending(grant => grant.GrantedAt)
            .ToListAsync();

        return grants
            .Select(grant => new RetakeGrantView(
                grant.UserAccountId,
                $"{grant.UserAccount?.DisplayName ?? "Student"} ({grant.UserAccount?.Email ?? "unknown@lms.local"})",
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

        await EnsureDefaultExamForCourseAsync(courseId);

        var exam = await _dbContext.CourseExams
            .Where(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null)
            .Select(existing => new { existing.Id, existing.RetakeCooldownMinutes })
            .FirstOrDefaultAsync();

        if (exam is null)
        {
            throw new InvalidOperationException("Exam not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseExamId == exam.Id && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            grant = new RetakeGrant
            {
                CourseExamId = exam.Id,
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
            await ResetRetakeCooldownTimerAsync(exam.Id, learnerUserId, exam.RetakeCooldownMinutes);
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "exam.retake.updated", "CourseExam", exam.Id, $"Learner={learnerUserId}; Attempts={grantedAttempts}; ResetCooldownTimer={resetCooldownTimer}");
    }

    public async Task RevokeRetakeGrantAsync(Guid courseId, Guid learnerUserId, Guid adminUserId, string adminEmail)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examId = await _dbContext.CourseExams
            .Where(existing => existing.CourseId == courseId && existing.IsRequired && existing.UnitId == null)
            .Select(existing => (Guid?)existing.Id)
            .FirstOrDefaultAsync();

        if (!examId.HasValue)
        {
            throw new InvalidOperationException("Exam not found for this course.");
        }

        var grant = await _dbContext.RetakeGrants
            .FirstOrDefaultAsync(existing => existing.CourseExamId == examId.Value && existing.UserAccountId == learnerUserId);

        if (grant is null)
        {
            return;
        }

        _dbContext.RetakeGrants.Remove(grant);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(adminUserId, adminEmail, "exam.retake.revoked", "CourseExam", examId.Value, $"Learner={learnerUserId}");
    }

    public async Task<bool> HasPassedRequiredExamAsync(Guid courseId, Guid userId)
    {
        await EnsureDefaultExamForCourseAsync(courseId);

        var examIds = await _dbContext.CourseExams
            .AsNoTracking()
            .Where(exam => exam.CourseId == courseId && exam.IsRequired)
            .Select(exam => exam.Id)
            .ToListAsync();

        if (examIds.Count == 0)
        {
            return true;
        }

        var hasRetakeGrant = await _dbContext.RetakeGrants
            .AsNoTracking()
            .AnyAsync(existing => examIds.Contains(existing.CourseExamId)
                && existing.UserAccountId == userId
                && existing.GrantedAttempts > 0);

        if (hasRetakeGrant)
        {
            return false;
        }

        var passedExamIds = await _dbContext.ExamAttempts
            .AsNoTracking()
            .Where(attempt => examIds.Contains(attempt.CourseExamId)
                && attempt.UserAccountId == userId
                && _dbContext.CourseExams.Any(exam => exam.Id == attempt.CourseExamId && exam.ContentVersion == attempt.ExamVersion)
                && attempt.Passed)
            .Select(attempt => attempt.CourseExamId)
            .Distinct()
            .ToListAsync();

        return examIds.All(passedExamIds.Contains);
    }

    public async Task<ExamProctoringSessionView> RegisterProctoringSessionAsync(
        Guid courseId,
        Guid learnerUserId,
        string proctorName,
        string? externalSessionId,
        int validForMinutes,
        Guid actorUserId,
        string actorEmail)
    {
        if (string.IsNullOrWhiteSpace(proctorName))
        {
            throw new InvalidOperationException("Proctor name is required.");
        }

        var courseExists = await _dbContext.Courses.AnyAsync(course => course.Id == courseId);
        var learnerExists = await _dbContext.UserAccounts.AnyAsync(user => user.Id == learnerUserId && user.Role == "Learner" && user.IsActive);
        if (!courseExists || !learnerExists)
        {
            throw new InvalidOperationException("Course or student was not found.");
        }

        var verifiedAtUtc = DateTime.UtcNow;
        var priorActiveSessions = await _dbContext.ExamProctoringSessions
            .Where(existing =>
                existing.CourseId == courseId
                && existing.UserAccountId == learnerUserId
                && existing.UsedAtUtc == null
                && !existing.SecurityIncidentReported
                && existing.ExpiresAtUtc >= verifiedAtUtc)
            .ToListAsync();

        foreach (var priorSession in priorActiveSessions)
        {
            priorSession.ExpiresAtUtc = verifiedAtUtc;
        }

        var session = new ExamProctoringSession
        {
            CourseId = courseId,
            UserAccountId = learnerUserId,
            ProctorName = proctorName.Trim(),
            ExternalSessionId = string.IsNullOrWhiteSpace(externalSessionId) ? null : externalSessionId.Trim(),
            IdentityVerifiedAtUtc = verifiedAtUtc,
            ClosedBookConfirmed = true,
            ExpiresAtUtc = verifiedAtUtc.AddMinutes(Math.Clamp(validForMinutes, 5, 240))
        };

        _dbContext.ExamProctoringSessions.Add(session);
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.proctoring.verified", "ExamProctoringSession", session.Id, $"CourseId={courseId};Learner={learnerUserId};Proctor={session.ProctorName}");
        return ToProctoringSessionView(session);
    }

    public async Task<ExamProctoringSessionView?> GetActiveProctoringSessionAsync(Guid courseId, Guid learnerUserId)
    {
        var session = await _dbContext.ExamProctoringSessions
            .AsNoTracking()
            .Where(existing =>
                existing.CourseId == courseId
                && existing.UserAccountId == learnerUserId
                && existing.UsedAtUtc == null
                && existing.ClosedBookConfirmed
                && !existing.SecurityIncidentReported
                && existing.ExpiresAtUtc >= DateTime.UtcNow)
            .OrderByDescending(existing => existing.IdentityVerifiedAtUtc)
            .FirstOrDefaultAsync();

        return session is null ? null : ToProctoringSessionView(session);
    }

    public async Task ExpireProctoringSessionAsync(Guid sessionId, Guid actorUserId, string actorEmail)
    {
        var session = await _dbContext.ExamProctoringSessions
            .FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null)
        {
            throw new InvalidOperationException("Proctoring session was not found.");
        }

        if (session.UsedAtUtc.HasValue)
        {
            throw new InvalidOperationException("A used proctoring session cannot be expired.");
        }

        var expiredAtUtc = DateTime.UtcNow;
        var activeSessions = await _dbContext.ExamProctoringSessions
            .Where(existing =>
                existing.CourseId == session.CourseId
                && existing.UserAccountId == session.UserAccountId
                && existing.UsedAtUtc == null
                && existing.ExpiresAtUtc >= expiredAtUtc)
            .ToListAsync();

        foreach (var activeSession in activeSessions)
        {
            activeSession.ExpiresAtUtc = expiredAtUtc;
        }

        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.proctoring.expired", "ExamProctoringSession", session.Id, $"CourseId={session.CourseId};Learner={session.UserAccountId};Proctor={session.ProctorName}");
    }

    public async Task ReportProctoringIncidentAsync(Guid sessionId, string notes, Guid actorUserId, string actorEmail)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new InvalidOperationException("Security incident notes are required.");
        }

        var session = await _dbContext.ExamProctoringSessions
            .FirstOrDefaultAsync(existing => existing.Id == sessionId);
        if (session is null)
        {
            throw new InvalidOperationException("Proctoring session was not found.");
        }

        session.SecurityIncidentReported = true;
        session.SecurityIncidentNotes = notes.Trim();
        session.ExpiresAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        await _auditLogService.WriteAsync(actorUserId, actorEmail, "exam.proctoring.incident-reported", "ExamProctoringSession", session.Id, $"CourseId={session.CourseId};Learner={session.UserAccountId};Proctor={session.ProctorName};Notes={session.SecurityIncidentNotes}");
    }

    private async Task<bool> HasActiveProctoringSessionAsync(Guid courseId, Guid learnerUserId)
    {
        return await _dbContext.ExamProctoringSessions
            .AsNoTracking()
            .AnyAsync(session =>
                session.CourseId == courseId
                && session.UserAccountId == learnerUserId
                && session.UsedAtUtc == null
                && session.ClosedBookConfirmed
                && !session.SecurityIncidentReported
                && session.ExpiresAtUtc >= DateTime.UtcNow);
    }

    private static ExamProctoringSessionView ToProctoringSessionView(ExamProctoringSession session)
    {
        return new ExamProctoringSessionView(session.Id, session.ProctorName, session.ExternalSessionId, session.IdentityVerifiedAtUtc, session.ExpiresAtUtc, session.ClosedBookConfirmed);
    }

    private async Task ResetRetakeCooldownTimerAsync(Guid examId, Guid learnerUserId, int cooldownMinutes)
    {
        var normalizedCooldownMinutes = Math.Max(cooldownMinutes, 0);
        if (normalizedCooldownMinutes <= 0)
        {
            return;
        }

        var latestAttempt = await _dbContext.ExamAttempts
            .Where(existing => existing.CourseExamId == examId && existing.UserAccountId == learnerUserId)
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

    private static void ValidateEditorQuestion(ExamQuestionEditorItem question, int position)
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

    private sealed record DefaultExamQuestionSpec(string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, string CorrectOption);
}
