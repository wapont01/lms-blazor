using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lms.Application.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExamAttemptAuditSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAnswers_ExamQuestions_ExamQuestionId",
                table: "ExamAnswers");

            migrationBuilder.AddColumn<string>(
                name: "CourseTitleSnapshot",
                table: "ExamAttempts",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamKindSnapshot",
                table: "ExamAttempts",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExamTitleSnapshot",
                table: "ExamAttempts",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExamVersion",
                table: "ExamAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "UnitTitleSnapshot",
                table: "ExamAttempts",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamQuestionId",
                table: "ExamAnswers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "CorrectOptionSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 1,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FeedbackTextSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionASnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 220,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionBSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 220,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionCSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 220,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionDSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 220,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptSnapshot",
                table: "ExamAnswers",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuestionOrderSnapshot",
                table: "ExamAnswers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ContentVersion",
                table: "CourseExams",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAnswers_ExamQuestions_ExamQuestionId",
                table: "ExamAnswers",
                column: "ExamQuestionId",
                principalTable: "ExamQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExamAnswers_ExamQuestions_ExamQuestionId",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "CourseTitleSnapshot",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ExamKindSnapshot",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ExamTitleSnapshot",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "ExamVersion",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "UnitTitleSnapshot",
                table: "ExamAttempts");

            migrationBuilder.DropColumn(
                name: "CorrectOptionSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "FeedbackTextSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "OptionASnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "OptionBSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "OptionCSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "OptionDSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "PromptSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionOrderSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "QuestionOrderSnapshot",
                table: "ExamAnswers");

            migrationBuilder.DropColumn(
                name: "ContentVersion",
                table: "CourseExams");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExamQuestionId",
                table: "ExamAnswers",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ExamAnswers_ExamQuestions_ExamQuestionId",
                table: "ExamAnswers",
                column: "ExamQuestionId",
                principalTable: "ExamQuestions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
