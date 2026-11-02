using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class BackfillExamAttemptAuditSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE ExamAttempts
                SET CourseTitleSnapshot = (
                        SELECT Courses.Title
                        FROM CourseExams
                        INNER JOIN Courses ON Courses.Id = CourseExams.CourseId
                        WHERE CourseExams.Id = ExamAttempts.CourseExamId
                    ),
                    ExamTitleSnapshot = (
                        SELECT CourseExams.Title
                        FROM CourseExams
                        WHERE CourseExams.Id = ExamAttempts.CourseExamId
                    ),
                    ExamKindSnapshot = (
                        SELECT CourseExams.Kind
                        FROM CourseExams
                        WHERE CourseExams.Id = ExamAttempts.CourseExamId
                    ),
                    UnitTitleSnapshot = (
                        SELECT Units.Title
                        FROM CourseExams
                        INNER JOIN Units ON Units.Id = CourseExams.UnitId
                        WHERE CourseExams.Id = ExamAttempts.CourseExamId
                    ),
                    ExamVersion = (
                        SELECT CourseExams.ContentVersion
                        FROM CourseExams
                        WHERE CourseExams.Id = ExamAttempts.CourseExamId
                    );

                UPDATE ExamAnswers
                SET PromptSnapshot = (SELECT Prompt FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    OptionASnapshot = (SELECT OptionA FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    OptionBSnapshot = (SELECT OptionB FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    OptionCSnapshot = (SELECT OptionC FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    OptionDSnapshot = (SELECT OptionD FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    CorrectOptionSnapshot = (SELECT CorrectOption FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    FeedbackTextSnapshot = (SELECT FeedbackText FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId),
                    QuestionOrderSnapshot = (SELECT OrderIndex FROM ExamQuestions WHERE ExamQuestions.Id = ExamAnswers.ExamQuestionId)
                WHERE ExamQuestionId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE ExamAttempts
                SET CourseTitleSnapshot = NULL,
                    ExamTitleSnapshot = NULL,
                    ExamKindSnapshot = NULL,
                    UnitTitleSnapshot = NULL,
                    ExamVersion = 1;

                UPDATE ExamAnswers
                SET PromptSnapshot = NULL,
                    OptionASnapshot = NULL,
                    OptionBSnapshot = NULL,
                    OptionCSnapshot = NULL,
                    OptionDSnapshot = NULL,
                    CorrectOptionSnapshot = NULL,
                    FeedbackTextSnapshot = NULL,
                    QuestionOrderSnapshot = 0;
                """);
        }
    }
}
