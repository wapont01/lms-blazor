using Lms.Application.Services;
using Lms.Domain.Entities;
using Xunit;

namespace Lms.Application.Tests.Services;

public class CourseAuthoringProgressHelperTests
{
    [Fact]
    public void Evaluate_ReturnsExpectedReadinessForDraftCourse()
    {
        var course = new Course
        {
            Title = "",
            Description = "",
            Level = "",
            Price = 0m,
            Modules = new List<Module>()
        };

        var snapshot = CourseAuthoringProgressHelper.Evaluate(course);

        Assert.Equal(0, snapshot.PercentComplete);
        Assert.Contains(snapshot.Items, item => item.Label == "Course title and description are defined" && !item.IsComplete);
        Assert.Contains(snapshot.Items, item => item.Label == "At least one module exists" && !item.IsComplete);
    }

    [Fact]
    public void Evaluate_ReturnsHigherReadinessWhenStructureExists()
    {
        var course = new Course
        {
            Title = "Leadership Essentials",
            Description = "A practical course",
            Level = "Intermediate",
            Price = 49m,
            Modules = new List<Module>
            {
                new()
                {
                    Title = "Welcome",
                    Lessons = new List<Lesson>
                    {
                        new() { Title = "Intro", TextContent = "Hello" }
                    }
                }
            }
        };

        var snapshot = CourseAuthoringProgressHelper.Evaluate(course);

        Assert.True(snapshot.PercentComplete >= 60);
        Assert.Contains(snapshot.Items, item => item.Label == "Course title and description are defined" && item.IsComplete);
        Assert.Contains(snapshot.Items, item => item.Label == "At least one module exists" && item.IsComplete);
        Assert.Contains(snapshot.Items, item => item.Label == "At least one lesson exists" && item.IsComplete);
    }

    [Fact]
    public void Evaluate_RequiresGatingCheckpointForEachUnit_InPrelicensingCourse()
    {
        var course = new Course
        {
            Title = "NC Real Estate Prelicensing",
            Description = "A regulated course",
            Level = "Beginner",
            Price = 249m,
            ComplianceType = CourseComplianceTypes.Prelicensing,
            Modules = new List<Module>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Title = "Unit 1",
                    Lessons = new List<Lesson>
                    {
                        new() { Title = "Overview", TextContent = "Welcome" }
                    }
                }
            }
        };

        var checkpoint = new CourseCheckpointDefinition
        {
            ModuleId = course.Modules.First().Id,
            Title = "Unit 1 checkpoint",
            Prompt = "Choose the correct answer.",
            Description = "This checkpoint is not gating.",
            GatesProgression = false,
            Options = new List<CourseCheckpointOption>
            {
                new() { Label = "Correct", IsCorrect = true },
                new() { Label = "Wrong", IsCorrect = false }
            }
        };

        var snapshot = CourseAuthoringProgressHelper.Evaluate(course, new[] { checkpoint });

        Assert.Contains(snapshot.Items, item => item.Label == "Every unit has a progression checkpoint" && !item.IsComplete);
    }
}
