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
}
