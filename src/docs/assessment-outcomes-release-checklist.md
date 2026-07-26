# Assessment Outcomes Milestone Release Checklist

## Scope Included

- Retake policy controls per required assessment:
  - Max retakes per learner
  - Cooldown minutes between attempts
- Learner outcomes loop:
  - Attempt history
  - Failed-question review with remediation feedback text
- Admin controls:
  - Create/add retake grants
  - Edit grant attempts
  - Revoke grants
- Broker visibility:
  - Assessment-blocked learners dashboard section

## Pre-Release Validation

1. Run application tests:
   - `dotnet test src/Lms.Application.Tests/Lms.Application.Tests.csproj`
2. Build web app:
   - Stop running app process first on Windows to avoid locked DLLs
   - `dotnet build src/Lms.Web/Lms.Web.csproj`
3. Manual smoke checks:
   - Login as Admin and verify in Assessment Editor:
     - Grant retake
     - Update existing grant
     - Revoke grant
   - Login as Broker and verify "Assessment-Blocked Learners" renders
   - Login as Learner and verify assessment gate + submit UI renders on enrolled course detail page

## Data/Schema Expectations

- Required tables/columns exist:
  - `CourseAssessments.MaxRetakesPerLearner`
  - `CourseAssessments.RetakeCooldownMinutes`
  - `AssessmentQuestions.FeedbackText`
  - `AssessmentAttempts.AttemptNumber`
  - `AssessmentAttempts.FeedbackSummary`
  - `RetakeGrants`
- Startup schema bootstrap should be idempotent in `src/Lms.Web/Program.cs`

## Audit Log Expectations

Verify these events appear when actions are performed:

- `assessment.retake.granted`
- `assessment.retake.updated`
- `assessment.retake.revoked`
- `assessment.updated` (when admin saves assessment content)

## Known Operational Notes

- On Windows, `Lms.Web` can lock `Lms.Application.dll` / `Lms.Domain.dll` during build.
- If build shows `MSB3026` copy/retry warnings, stop the running web process and rebuild.

## Rollback Plan

1. Stop app process.
2. Re-deploy previous known-good build artifacts.
3. Restore previous SQLite database backup if data rollback is required.
4. Restart app and run login + course detail smoke checks.

## Sign-Off

- [ ] Engineering sign-off
- [ ] QA sign-off
- [ ] Product sign-off
- [ ] Release timestamp recorded
