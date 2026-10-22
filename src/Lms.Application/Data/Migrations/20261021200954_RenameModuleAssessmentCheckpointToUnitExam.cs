using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameModuleAssessmentCheckpointToUnitExam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(name: "ModuleId", table: "CourseAssessments", newName: "UnitId");
            migrationBuilder.RenameColumn(name: "CourseAssessmentId", table: "AssessmentQuestions", newName: "CourseExamId");
            migrationBuilder.RenameColumn(name: "CourseAssessmentId", table: "RetakeGrants", newName: "CourseExamId");
            migrationBuilder.RenameColumn(name: "AssessmentAttemptId", table: "AssessmentAnswers", newName: "ExamAttemptId");
            migrationBuilder.RenameColumn(name: "AssessmentQuestionId", table: "AssessmentAnswers", newName: "ExamQuestionId");
            migrationBuilder.RenameColumn(name: "CheckpointKey", table: "ModuleCheckpointProgresses", newName: "UnitExamKey");
            migrationBuilder.RenameColumn(name: "AssessmentReminderSentAt", table: "Enrollments", newName: "ExamReminderSentAt");
            migrationBuilder.RenameColumn(name: "CourseCheckpointDefinitionId", table: "CourseCheckpointOptions", newName: "CourseUnitExamDefinitionId");
            migrationBuilder.RenameColumn(name: "ModuleId", table: "Lessons", newName: "UnitId");
            migrationBuilder.RenameColumn(name: "ModuleId", table: "CourseCheckpointDefinitions", newName: "UnitId");
            migrationBuilder.RenameColumn(name: "CourseAssessmentId", table: "AssessmentAttempts", newName: "CourseExamId");
            migrationBuilder.RenameTable(name: "CourseAssessments", newName: "CourseExams");
            migrationBuilder.RenameTable(name: "Modules", newName: "Units");
            migrationBuilder.RenameTable(name: "AssessmentQuestions", newName: "ExamQuestions");
            migrationBuilder.RenameTable(name: "AssessmentAnswers", newName: "ExamAnswers");
            migrationBuilder.RenameTable(name: "ModuleCheckpointProgresses", newName: "UnitExamProgresses");
            migrationBuilder.RenameTable(name: "CourseCheckpointOptions", newName: "CourseUnitExamOptions");
            migrationBuilder.RenameTable(name: "CourseCheckpointDefinitions", newName: "CourseUnitExamDefinitions");
            migrationBuilder.RenameTable(name: "AssessmentAttempts", newName: "ExamAttempts");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentAnswers_AssessmentAttemptId_AssessmentQuestionId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ExamAnswers_ExamAttemptId_ExamQuestionId\" ON \"ExamAnswers\" (\"ExamAttemptId\", \"ExamQuestionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentAnswers_AssessmentQuestionId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_ExamAnswers_ExamQuestionId\" ON \"ExamAnswers\" (\"ExamQuestionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentQuestions_CourseAssessmentId_OrderIndex\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_ExamQuestions_CourseExamId_OrderIndex\" ON \"ExamQuestions\" (\"CourseExamId\", \"OrderIndex\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseAssessments_CourseId_IsRequired\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseExams_CourseId_IsRequired\" ON \"CourseExams\" (\"CourseId\", \"IsRequired\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Modules_CourseId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_Units_CourseId\" ON \"Units\" (\"CourseId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_RetakeGrants_CourseAssessmentId_UserAccountId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_RetakeGrants_CourseExamId_UserAccountId\" ON \"RetakeGrants\" (\"CourseExamId\", \"UserAccountId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ModuleCheckpointProgresses_CourseId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_UnitExamProgresses_CourseId\" ON \"UnitExamProgresses\" (\"CourseId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ModuleCheckpointProgresses_UserAccountId_CourseId_CheckpointKey\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_UnitExamProgresses_UserAccountId_CourseId_UnitExamKey\" ON \"UnitExamProgresses\" (\"UserAccountId\", \"CourseId\", \"UnitExamKey\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseCheckpointOptions_CourseCheckpointDefinitionId_OrderIndex\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseUnitExamOptions_CourseUnitExamDefinitionId_OrderIndex\" ON \"CourseUnitExamOptions\" (\"CourseUnitExamDefinitionId\", \"OrderIndex\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Lessons_ModuleId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_Lessons_UnitId\" ON \"Lessons\" (\"UnitId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseCheckpointDefinitions_CourseId_Key\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_CourseUnitExamDefinitions_CourseId_Key\" ON \"CourseUnitExamDefinitions\" (\"CourseId\", \"Key\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseCheckpointDefinitions_ModuleId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseUnitExamDefinitions_UnitId\" ON \"CourseUnitExamDefinitions\" (\"UnitId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentAttempts_CourseAssessmentId_UserAccountId_SubmittedAt\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_ExamAttempts_CourseExamId_UserAccountId_SubmittedAt\" ON \"ExamAttempts\" (\"CourseExamId\", \"UserAccountId\", \"SubmittedAt\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentAttempts_ExamProctoringSessionId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ExamAttempts_ExamProctoringSessionId\" ON \"ExamAttempts\" (\"ExamProctoringSessionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AssessmentAttempts_UserAccountId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_ExamAttempts_UserAccountId\" ON \"ExamAttempts\" (\"UserAccountId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseAssessments_ModuleId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseExams_UnitId\" ON \"CourseExams\" (\"UnitId\");");
            migrationBuilder.Sql("UPDATE \"UnitExamProgresses\" SET \"UnitExamKey\" = 'unit:' || substr(\"UnitExamKey\", 8) WHERE \"UnitExamKey\" LIKE 'module:%';");
            migrationBuilder.Sql("UPDATE \"AuditLogs\" SET \"Action\" = 'exam.' || substr(\"Action\", 12) WHERE \"Action\" LIKE 'assessment.%';");
            migrationBuilder.Sql("UPDATE \"AuditLogs\" SET \"TargetType\" = 'CourseExam' WHERE \"TargetType\" = 'CourseAssessment';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"AuditLogs\" SET \"TargetType\" = 'CourseAssessment' WHERE \"TargetType\" = 'CourseExam';");
            migrationBuilder.Sql("UPDATE \"AuditLogs\" SET \"Action\" = 'assessment.' || substr(\"Action\", 6) WHERE \"Action\" LIKE 'exam.%';");
            migrationBuilder.Sql("UPDATE \"UnitExamProgresses\" SET \"UnitExamKey\" = 'module:' || substr(\"UnitExamKey\", 6) WHERE \"UnitExamKey\" LIKE 'unit:%';");
            migrationBuilder.RenameTable(name: "ExamAttempts", newName: "AssessmentAttempts");
            migrationBuilder.RenameTable(name: "CourseUnitExamDefinitions", newName: "CourseCheckpointDefinitions");
            migrationBuilder.RenameTable(name: "CourseUnitExamOptions", newName: "CourseCheckpointOptions");
            migrationBuilder.RenameTable(name: "UnitExamProgresses", newName: "ModuleCheckpointProgresses");
            migrationBuilder.RenameTable(name: "ExamAnswers", newName: "AssessmentAnswers");
            migrationBuilder.RenameTable(name: "ExamQuestions", newName: "AssessmentQuestions");
            migrationBuilder.RenameTable(name: "Units", newName: "Modules");
            migrationBuilder.RenameTable(name: "CourseExams", newName: "CourseAssessments");
            migrationBuilder.RenameColumn(name: "CourseExamId", table: "ExamAttempts", newName: "CourseAssessmentId");
            migrationBuilder.RenameColumn(name: "UnitId", table: "CourseUnitExamDefinitions", newName: "ModuleId");
            migrationBuilder.RenameColumn(name: "UnitId", table: "Lessons", newName: "ModuleId");
            migrationBuilder.RenameColumn(name: "CourseUnitExamDefinitionId", table: "CourseUnitExamOptions", newName: "CourseCheckpointDefinitionId");
            migrationBuilder.RenameColumn(name: "ExamReminderSentAt", table: "Enrollments", newName: "AssessmentReminderSentAt");
            migrationBuilder.RenameColumn(name: "UnitExamKey", table: "UnitExamProgresses", newName: "CheckpointKey");
            migrationBuilder.RenameColumn(name: "ExamQuestionId", table: "ExamAnswers", newName: "AssessmentQuestionId");
            migrationBuilder.RenameColumn(name: "ExamAttemptId", table: "ExamAnswers", newName: "AssessmentAttemptId");
            migrationBuilder.RenameColumn(name: "CourseExamId", table: "RetakeGrants", newName: "CourseAssessmentId");
            migrationBuilder.RenameColumn(name: "CourseExamId", table: "ExamQuestions", newName: "CourseAssessmentId");
            migrationBuilder.RenameColumn(name: "UnitId", table: "CourseExams", newName: "ModuleId");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamAnswers_ExamAttemptId_ExamQuestionId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_AssessmentAnswers_AssessmentAttemptId_AssessmentQuestionId\" ON \"AssessmentAnswers\" (\"AssessmentAttemptId\", \"AssessmentQuestionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamAnswers_ExamQuestionId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_AssessmentAnswers_AssessmentQuestionId\" ON \"AssessmentAnswers\" (\"AssessmentQuestionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamQuestions_CourseExamId_OrderIndex\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_AssessmentQuestions_CourseAssessmentId_OrderIndex\" ON \"AssessmentQuestions\" (\"CourseAssessmentId\", \"OrderIndex\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseExams_CourseId_IsRequired\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseAssessments_CourseId_IsRequired\" ON \"CourseAssessments\" (\"CourseId\", \"IsRequired\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Units_CourseId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_Modules_CourseId\" ON \"Modules\" (\"CourseId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_RetakeGrants_CourseExamId_UserAccountId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_RetakeGrants_CourseAssessmentId_UserAccountId\" ON \"RetakeGrants\" (\"CourseAssessmentId\", \"UserAccountId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UnitExamProgresses_CourseId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_ModuleCheckpointProgresses_CourseId\" ON \"ModuleCheckpointProgresses\" (\"CourseId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_UnitExamProgresses_UserAccountId_CourseId_UnitExamKey\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ModuleCheckpointProgresses_UserAccountId_CourseId_CheckpointKey\" ON \"ModuleCheckpointProgresses\" (\"UserAccountId\", \"CourseId\", \"CheckpointKey\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseUnitExamOptions_CourseUnitExamDefinitionId_OrderIndex\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseCheckpointOptions_CourseCheckpointDefinitionId_OrderIndex\" ON \"CourseCheckpointOptions\" (\"CourseCheckpointDefinitionId\", \"OrderIndex\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Lessons_UnitId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_Lessons_ModuleId\" ON \"Lessons\" (\"ModuleId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseUnitExamDefinitions_CourseId_Key\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_CourseCheckpointDefinitions_CourseId_Key\" ON \"CourseCheckpointDefinitions\" (\"CourseId\", \"Key\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseUnitExamDefinitions_UnitId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseCheckpointDefinitions_ModuleId\" ON \"CourseCheckpointDefinitions\" (\"ModuleId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamAttempts_CourseExamId_UserAccountId_SubmittedAt\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_AssessmentAttempts_CourseAssessmentId_UserAccountId_SubmittedAt\" ON \"AssessmentAttempts\" (\"CourseAssessmentId\", \"UserAccountId\", \"SubmittedAt\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamAttempts_ExamProctoringSessionId\";");
            migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_AssessmentAttempts_ExamProctoringSessionId\" ON \"AssessmentAttempts\" (\"ExamProctoringSessionId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ExamAttempts_UserAccountId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_AssessmentAttempts_UserAccountId\" ON \"AssessmentAttempts\" (\"UserAccountId\");");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_CourseExams_UnitId\";");
            migrationBuilder.Sql("CREATE INDEX \"IX_CourseAssessments_ModuleId\" ON \"CourseAssessments\" (\"ModuleId\");");
        }
    }
}
