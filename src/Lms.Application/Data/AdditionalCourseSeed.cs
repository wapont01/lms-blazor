using System.Text.RegularExpressions;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class AdditionalCourseSeed
{
    private const string CourseReviewNote = "Imported from user-supplied course materials. Verify current {0} course/provider approval, CE hours, reproduction rights, and exam answer keys before publication.";
    private static IReadOnlyList<CourseDefinition> Courses =>
    [
        new(
            Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910001"),
            "Ethics at Work",
            "florida-ethics-at-work",
            "Florida",
            "FLORIDA/COURSES/CE/Ethics at Work/txts/Ethics at Work Course Printout.txt",
            5,
            3,
            "Ethics at Work Final Exam",
            EthicsFinalQuestions),
        new(
            Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910002"),
            "Preparing a Market Analysis - Best Practices",
            "nc-preparing-a-market-analysis-best-practices",
            "North Carolina",
            "NORTH CAROLINA/COURSES/CE/Preparing a Market Analysis - Best Practices/txts/Course Print - Preparing a Market Analysis.txt",
            5,
            4,
            "Preparing a Market Analysis - Best Practices Final Exam",
            MarketAnalysisFinalQuestions,
            CoursePrintTitle: "Preparing a Market Analysis: Best Practices"),
        new(
            Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910003"),
            "Document Diligence: Safeguarding Your Transactions",
            "nc-document-diligence-safeguarding-transactions",
            "North Carolina",
            "NORTH CAROLINA/COURSES/CE/Document Diligence - Safeguarding Your Transactions/txts/Document Diligence - Safeguarding Your Transactions Handbook.txt",
            3,
            4,
            "Document Diligence: Safeguarding Your Transactions Final Exam",
            DocumentDiligenceFinalQuestions,
            IsHandbook: true,
            PassPercent: 75m),
        new(
            Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910004"),
            "Residential Property Management Essentials",
            "florida-residential-property-management-essentials",
            "Florida",
            "FLORIDA/COURSES/CE/Residential Property Management Essentials (4)/txts/Residential Property Management Essentials PRINT (4).txt",
            6,
            4,
            "Residential Property Management Essentials Final Exam",
            ResidentialPropertyManagementFinalQuestions,
            CoursePrintTitle: "Residential Property Management Essentials",
            LessonHeadings: ResidentialPropertyManagementLessonHeadings),
        new(
            Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910005"),
            "Assistance Animals And Fair Housing",
            "florida-assistance-animals-and-fair-housing",
            "Florida",
            "FLORIDA/COURSES/CE/Assistance Animals And Fair Housing/txts/Assistance Animals and Fair Housing PRINT.txt",
            4,
            4,
            "Assistance Animals And Fair Housing Final Exam",
            AssistanceAnimalsFinalQuestions,
            CoursePrintTitle: "Assistance Animals and Fair Housing")
    ];

    public static async Task EnsureAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        foreach (var definition in Courses)
        {
            var sourcePath = Path.Combine(root, "docs", definition.SourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var sourceText = await File.ReadAllTextAsync(sourcePath);
            var units = definition.IsHandbook
                ? ParseHandbook(sourceText)
                : ParseCoursePrint(sourceText, definition.CoursePrintTitle ?? definition.Title, definition.LessonHeadings);
            if (units.Count != definition.ExpectedUnitCount || units.Any(unit => unit.Lessons.Count == 0))
            {
                throw new InvalidDataException($"{definition.Title} source did not contain the expected course structure.");
            }

            await EnsureCourseAsync(dbContext, definition, units);
        }
    }

    public static List<UnitDefinition> ParseCoursePrint(
        string coursePrint,
        string courseTitle,
        IReadOnlyList<string>? lessonHeadings = null)
    {
        var lines = coursePrint.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var contentStart = Array.FindIndex(lines, line =>
            string.Equals(line.Trim(), $"Unit 1: Course Introduction: {courseTitle}", StringComparison.Ordinal));
        if (contentStart < 0)
        {
            throw new InvalidDataException($"{courseTitle} source unit content could not be located.");
        }

        var lessonTitlesByUnit = ParseLessonTitlesFromContents(lines, contentStart);
        var units = new List<UnitDefinition>();
        UnitDefinition? currentUnit = null;
        string? currentLessonTitle = null;
        var lessonLines = new List<string>();

        void FinishLesson()
        {
            if (currentUnit is null || string.IsNullOrWhiteSpace(currentLessonTitle))
            {
                return;
            }

            var content = string.Join("\n", lessonLines).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                currentUnit.Lessons.Add(new LessonDefinition(currentLessonTitle, content, currentUnit.Lessons.Count + 1));
            }

            lessonLines.Clear();
        }

        for (var lineIndex = contentStart; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].TrimEnd();
            var trimmed = line.Trim();

            var unitMatch = Regex.Match(trimmed, @"^Unit (?<number>\d+): (?<title>.+)$");
            if (unitMatch.Success)
            {
                FinishLesson();
                var unitNumber = int.Parse(unitMatch.Groups["number"].Value);
                var title = Regex.Replace(unitMatch.Groups["title"].Value.Trim(), @"\s+Unit Introduction$", string.Empty, RegexOptions.IgnoreCase);
                currentUnit = new UnitDefinition($"Unit {unitNumber}: {title}", unitNumber);
                units.Add(currentUnit);
                currentLessonTitle = "Unit Introduction";
                continue;
            }

            if (currentUnit is null)
            {
                continue;
            }

            var matchedLessonHeading = lessonHeadings?.FirstOrDefault(heading =>
                string.Equals(trimmed, heading, StringComparison.OrdinalIgnoreCase));
            if (matchedLessonHeading is not null)
            {
                FinishLesson();
                currentLessonTitle = matchedLessonHeading;
                continue;
            }

            var lessonMatch = Regex.Match(trimmed, @"(?<prefix>.*?)(?:^|\s)Lesson (?<number>\d+): (?<title>.+)$");
            if (lessonMatch.Success)
            {
                AddPrefix(lessonMatch.Groups["prefix"].Value, lessonLines);
                FinishLesson();
                var lessonNumber = int.Parse(lessonMatch.Groups["number"].Value);
                currentLessonTitle = lessonTitlesByUnit.TryGetValue((currentUnit.OrderIndex, lessonNumber), out var sourceTitle)
                    ? sourceTitle
                    : lessonMatch.Groups["title"].Value.Trim();
                var remainingText = trimmed[(lessonMatch.Index + lessonMatch.Length)..].Trim();
                if (remainingText.StartsWith(currentLessonTitle, StringComparison.OrdinalIgnoreCase))
                {
                    remainingText = remainingText[currentLessonTitle.Length..].Trim();
                }

                if (remainingText.Length > 0)
                {
                    lessonLines.Add(remainingText);
                }

                continue;
            }

            var keyPointsMatch = Regex.Match(trimmed, @"(?<prefix>.*?)(?:^|\s)Key Points: (?<title>.+)$");
            if (keyPointsMatch.Success)
            {
                AddPrefix(keyPointsMatch.Groups["prefix"].Value, lessonLines);
                FinishLesson();
                currentLessonTitle = $"Key Points: {keyPointsMatch.Groups["title"].Value.Trim()}";
                continue;
            }

            if (trimmed.Equals("Unit Exam", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Final Exam", StringComparison.OrdinalIgnoreCase))
            {
                FinishLesson();
                currentLessonTitle = null;
                continue;
            }

            if (currentLessonTitle is not null)
            {
                lessonLines.Add(line);
            }
        }

        FinishLesson();
        return units;
    }

    public static List<UnitDefinition> ParseHandbook(string handbook)
    {
        var tocStart = handbook.IndexOf("Table of Contents", StringComparison.OrdinalIgnoreCase);
        var tocEnd = tocStart < 0
            ? -1
            : handbook.IndexOf("Security in the Clouds", tocStart, StringComparison.OrdinalIgnoreCase);
        if (tocEnd < 0)
        {
            throw new InvalidDataException("Document Diligence source table of contents could not be located.");
        }

        var topics = new[]
        {
            ("Course Introduction: Document Diligence - Safeguarding Your Transactions", 1),
            ("Common Real Estate Transaction Documents", 1),
            ("Common Real Estate Documents", 1),
            ("Purchase Offers", 1),
            ("Contract Clauses & Addenda", 1),
            ("Unauthorized Practice of Law", 1),
            ("Common Contract Contingencies", 1),
            ("Closing Documents", 1),
            ("Multiple Offers & Contingencies", 2),
            ("Document Signing", 2),
            ("E-Signatures & Electronic Documents", 2),
            ("Purchase Agreements", 2),
            ("Escrow Fund Handling", 2),
            ("Document Retention", 2),
            ("Transaction & Property Managment", 2),
            ("Risk Management & Information", 3),
            ("Handling Sensitive Information", 3),
            ("Preventing Wire Fraud", 3),
            ("Security in the Clouds", 3)
        };

        var sectionStarts = new int[topics.Length];
        var searchFrom = tocEnd + "Security in the Clouds".Length;
        for (var index = 0; index < topics.Length; index++)
        {
            var sectionStart = handbook.IndexOf(topics[index].Item1, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (sectionStart < 0)
            {
                throw new InvalidDataException($"Document Diligence section '{topics[index].Item1}' could not be located.");
            }

            sectionStarts[index] = sectionStart;
            searchFrom = sectionStart + topics[index].Item1.Length;
        }

        var units = new[]
        {
            new UnitDefinition("Unit 1: Transaction Documents", 1),
            new UnitDefinition("Unit 2: Managing Transaction Documents", 2),
            new UnitDefinition("Unit 3: Safeguarding Transaction Information", 3)
        };

        for (var index = 0; index < topics.Length; index++)
        {
            var sectionEnd = index == topics.Length - 1 ? handbook.Length : sectionStarts[index + 1];
            var content = handbook[sectionStarts[index]..sectionEnd].Trim();
            if (content.Length == 0)
            {
                throw new InvalidDataException($"Document Diligence section '{topics[index].Item1}' was empty.");
            }

            var unit = units[topics[index].Item2 - 1];
            unit.Lessons.Add(new LessonDefinition(topics[index].Item1, content, unit.Lessons.Count + 1));
        }

        return units.ToList();
    }

    private static async Task EnsureCourseAsync(
        ApplicationDbContext dbContext,
        CourseDefinition definition,
        IReadOnlyList<UnitDefinition> units)
    {
        var course = await dbContext.Courses
            .Include(existing => existing.Modules)
                .ThenInclude(module => module.Lessons)
            .FirstOrDefaultAsync(existing => existing.Id == definition.Id);
        var expectedAssessmentTitle = definition.FinalExamTitle;
        var existingFinal = course is null
            ? null
            : await dbContext.CourseAssessments
                .Include(assessment => assessment.Questions)
                .FirstOrDefaultAsync(assessment =>
                    assessment.CourseId == definition.Id
                    && assessment.ModuleId == null
                    && assessment.Title == expectedAssessmentTitle);

        var assessmentIsCurrent = existingFinal is not null
            && existingFinal.PassPercent == definition.PassPercent
            && existingFinal.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new QuestionDefinition(
                    question.Prompt,
                    question.OptionA,
                    question.OptionB,
                    question.OptionC,
                    question.OptionD,
                    question.CorrectOption))
                .SequenceEqual(definition.FinalQuestions);
        var coursePrice = definition.DurationHours switch
        {
            3 => 30m,
            4 => 35m,
            _ => 0m
        };
        if (course?.Title == definition.Title
            && course.Modules.Count == units.Count
            && course.Price == coursePrice
            && course.Industry == CourseIndustries.RealEstate
            && course.MinimumPassingPercent == (int)definition.PassPercent
            && assessmentIsCurrent)
        {
            return;
        }

        course ??= new Course { Id = definition.Id };
        if (dbContext.Entry(course).State == EntityState.Detached)
        {
            dbContext.Courses.Add(course);
        }

        course.Title = definition.Title;
        course.Slug = definition.Slug;
        course.Description = $"{definition.Title} continuing education based on the supplied course materials. Course duration and {definition.State} CE approval must be verified before publication.";
        course.Level = "Continuing Education";
        course.DurationHours = definition.DurationHours;
        course.CreditHours = definition.DurationHours;
        course.ComplianceType = CourseComplianceTypes.ContinuingEducation;
        course.ContinuingEducationType = ContinuingEducationTypes.Elective;
        course.Industry = CourseIndustries.RealEstate;
        course.DeliveryMethod = CourseDeliveryMethods.DistanceEducation;
        course.State = definition.State;
        course.RequiredInstructionalMinutes = definition.DurationHours * 60;
        course.MinimumPassingPercent = (int)definition.PassPercent;
        course.MinimumAttendancePercent = 80;
        course.RequiresProctoredExam = false;
        course.Price = coursePrice;
        course.IsPublished = false;
        course.IsArchived = false;
        course.ReviewStatus = CourseReviewStatuses.Draft;
        course.ReviewNote = string.Format(CourseReviewNote, definition.State);
        course.UpdatedAt = DateTime.UtcNow;

        if (course.Modules.Count != units.Count)
        {
            var oldAssessments = await dbContext.CourseAssessments
                .Where(assessment => assessment.CourseId == definition.Id)
                .ToListAsync();
            dbContext.CourseAssessments.RemoveRange(oldAssessments);
            var oldCheckpoints = await dbContext.CourseCheckpointDefinitions
                .Where(checkpoint => checkpoint.CourseId == definition.Id)
                .ToListAsync();
            dbContext.CourseCheckpointDefinitions.RemoveRange(oldCheckpoints);
            dbContext.Modules.RemoveRange(course.Modules);
            await dbContext.SaveChangesAsync();

            foreach (var unitDefinition in units)
            {
                var unit = new Module
                {
                    CourseId = definition.Id,
                    Title = unitDefinition.Title,
                    OrderIndex = unitDefinition.OrderIndex
                };
                dbContext.Modules.Add(unit);

                foreach (var lessonDefinition in unitDefinition.Lessons)
                {
                    unit.Lessons.Add(new Lesson
                    {
                        ModuleId = unit.Id,
                        Title = lessonDefinition.Title,
                        ContentType = "Text",
                        TextContent = lessonDefinition.TextContent,
                        DurationMinutes = 0,
                        OrderIndex = lessonDefinition.OrderIndex,
                        IsRequired = true
                    });
                }
            }

            await dbContext.SaveChangesAsync();
        }

        if (existingFinal is not null)
        {
            dbContext.CourseAssessments.Remove(existingFinal);
        }

        var finalAssessment = new CourseAssessment
        {
            CourseId = definition.Id,
            Title = definition.FinalExamTitle,
            PassPercent = definition.PassPercent,
            IsRequired = true
        };
        dbContext.CourseAssessments.Add(finalAssessment);
        foreach (var question in definition.FinalQuestions.Select((item, index) => new AssessmentQuestion
        {
            CourseAssessmentId = finalAssessment.Id,
            Prompt = item.Prompt,
            OptionA = item.OptionA,
            OptionB = item.OptionB,
            OptionC = item.OptionC,
            OptionD = item.OptionD,
            CorrectOption = item.CorrectOption,
            OrderIndex = index + 1
        }))
        {
            dbContext.AssessmentQuestions.Add(question);
        }

        await dbContext.SaveChangesAsync();
    }

    private static Dictionary<(int Unit, int Lesson), string> ParseLessonTitlesFromContents(string[] lines, int contentStart)
    {
        var titles = new Dictionary<(int Unit, int Lesson), string>();
        var currentUnitNumber = 0;
        for (var lineIndex = 0; lineIndex < contentStart; lineIndex++)
        {
            var line = lines[lineIndex].Trim();
            var unitMatch = Regex.Match(line, @"^Unit (?<unit>\d+): .+\s+\d+$");
            if (unitMatch.Success)
            {
                currentUnitNumber = int.Parse(unitMatch.Groups["unit"].Value);
                continue;
            }

            var lessonMatch = Regex.Match(line, @"^Lesson (?<lesson>\d+): (?<title>.+?)\s+\d+$");
            if (currentUnitNumber > 0 && lessonMatch.Success)
            {
                titles[(currentUnitNumber, int.Parse(lessonMatch.Groups["lesson"].Value))] = lessonMatch.Groups["title"].Value.Trim();
            }
        }

        return titles;
    }

    private static void AddPrefix(string prefix, ICollection<string> lines)
    {
        var trimmedPrefix = prefix.Trim();
        if (trimmedPrefix.Length > 0)
        {
            lines.Add(trimmedPrefix);
        }
    }

    private static QuestionDefinition Q(
        string prompt,
        string optionA,
        string optionB,
        string optionC,
        string optionD,
        string correctOption) =>
        new(prompt, optionA, optionB, optionC, optionD, correctOption);

    private static readonly IReadOnlyList<QuestionDefinition> EthicsFinalQuestions = BalanceCorrectOptions(
    [
        Q("What best describes the role of the NAR Code of Ethics in real estate practice?", "It replaces state licensing law", "It provides ethical duties for NAR members and a professional benchmark", "It guarantees that every transaction will close", "It applies only to property managers", "B"),
        Q("A buyer tells their agent the maximum price they are willing to pay. What should the agent do with that information?", "Share it with the seller to strengthen the offer", "Keep it confidential unless the buyer authorizes disclosure or law requires it", "Post it in the listing remarks", "Share it with other buyer agents", "B"),
        Q("A licensee learns of a material property condition that could affect a buyer's decision. What is the most appropriate action?", "Disclose the known material fact promptly", "Wait until after closing", "Mention it only if the buyer asks the right question", "Ask the seller to remove it from the disclosure form", "A"),
        Q("A real estate professional observes conduct that may violate a professional ethics code. What should guide the next step?", "Use the applicable association or regulatory reporting process", "Publicly accuse the other professional online", "Alter transaction records before reporting", "Threaten the other party with a lawsuit", "A"),
        Q("A client asks an agent to draft a custom legal clause with consequences the agent cannot explain. What should the agent do?", "Write the clause from memory", "Recommend that the client consult a qualified attorney", "Copy language from an unrelated transaction", "Promise the clause will be enforceable", "B"),
        Q("Why is the Code of Ethics relevant even to licensees who are not NAR members?", "It automatically makes them association members", "It can inform professional expectations and industry practice", "It overrides their state law", "It guarantees access to association arbitration", "B"),
        Q("When does the Code of Ethics create membership obligations for REALTORS®?", "Only when a client requests it", "As part of their NAR membership obligations", "Only in transactions involving a commercial property", "Only after a local board complaint is filed", "B"),
        Q("A disagreement between members is subject to a written agreement to arbitrate. Which process is intended to resolve the dispute through a decision by a neutral panel?", "Mediation", "Arbitration", "A property inspection", "A comparative market analysis", "B"),
        Q("What is a key difference between mediation and arbitration?", "Mediation helps parties seek their own agreement; arbitration results in a decision", "Mediation is always binding and arbitration is not", "Arbitration is an informal conversation without a neutral", "They are identical processes", "A"),
        Q("A licensee is asked to make a public claim about a competing broker's services. What is the ethical approach?", "Make only truthful, supportable statements", "Use any claim that might attract more clients", "Repeat unverified rumors but label them opinions", "Avoid correcting an inaccurate claim after learning it is false", "A"),
        Q("A professional ethics complaint is being considered. Which source is the best starting point for the applicable process?", "The relevant association's current complaint procedures", "A social media poll", "An unrelated state's forms", "A title insurance policy", "A"),
        Q("Which statement about ethics requirements and real estate law is most accurate?", "Ethics rules replace all legal requirements", "Legal compliance alone always satisfies every professional ethics duty", "Ethical duties and legal requirements may both apply", "Only a court can decide whether a professional code exists", "C"),
        Q("A licensee represents a client while also having a personal interest that could affect the representation. What should the licensee do?", "Disclose the relevant conflict as required and follow applicable duties", "Hide the interest unless the client discovers it", "Transfer the interest to a relative", "Disclose it only to the other broker", "A"),
        Q("Why should a professional keep accurate transaction communications and records?", "They can support accountability and clarify what was communicated", "They remove the need to disclose material facts", "They guarantee a favorable hearing outcome", "They allow the agent to change a signed agreement", "A"),
        Q("A member believes another member violated an ethical duty. Which response is most consistent with a fair complaint process?", "Use the established process and provide relevant, accurate information", "Publish the allegation before reviewing the facts", "Destroy records that could complicate the complaint", "Demand that the other member resign before filing", "A")
    ]);

    private static readonly IReadOnlyList<QuestionDefinition> MarketAnalysisFinalQuestions = BalanceCorrectOptions(
    [
        Q("What is the primary purpose of a comparative market analysis?", "To estimate a supportable price range using property and market evidence", "To guarantee the final sale price", "To replace a required appraisal in every situation", "To set a price without considering the subject property", "A"),
        Q("Which comparable property is generally the most useful starting point?", "A recent sale with similar location, property type, size, and condition", "The most expensive active listing in the county", "A distant sale with a different property type", "A property selected only because it supports the seller's preferred price", "A"),
        Q("What can active competing listings help a seller understand?", "The current alternatives buyers may consider", "The exact price every buyer will pay", "The property's tax basis", "The amount of a future appraisal", "A"),
        Q("A similar comparable has an upgraded feature that the subject property lacks. When adjusting the comparable's sale price to reflect the subject, what is generally appropriate?", "Subtract the contributory value of the comparable's feature", "Add the feature's value to the comparable", "Ignore the difference in every case", "Replace the sale price with the asking price", "A"),
        Q("What may a high number of expired listings in a segment suggest?", "Some properties may have been priced or marketed beyond what buyers accepted", "Every expired property was defective", "Demand is guaranteed to rise", "The subject property's value is exactly the average list price", "A"),
        Q("Why should a market analysis account for a property's condition?", "Condition can affect buyer appeal and the adjustments needed against comparables", "Condition never affects residential value", "Only new construction can be compared", "It determines the seller's legal ownership", "A"),
        Q("Which extra due-diligence issue can be especially important when analyzing vacant land?", "Access, zoning, and available utilities", "The number of interior bedrooms", "The age of the kitchen appliances", "The seller's preferred moving company", "A"),
        Q("How should a listing agent respond when a seller insists on a price unsupported by the available data?", "Explain the evidence and likely market tradeoffs clearly", "Promise that the price will produce a quick sale", "Remove unfavorable comparables from the analysis", "Guarantee the seller will receive the asking price", "A"),
        Q("What is a sensible use of seller motivation in pricing advice?", "Use it to understand timing and strategy while keeping the value analysis evidence-based", "Use it to change comparable sale facts", "Ignore it even when timing matters", "Treat it as a substitute for market data", "A"),
        Q("What is the main value of looking at more than one comparable sale?", "It helps identify a range and reduces reliance on an unusual single transaction", "It proves all properties are identical", "It eliminates the need to inspect the subject", "It makes market conditions irrelevant", "A"),
        Q("A new construction property is being analyzed. Which approach is most helpful?", "Consider its features, location, competing new homes, and buyer alternatives", "Assume it is always worth more than any resale home", "Use only the builder's original construction cost", "Ignore incentives offered by the builder", "A"),
        Q("Why should an agent distinguish between an asking price and a closed sale price?", "An asking price reflects a seller's offer, while a closed sale shows an agreed transaction", "They are always the same amount", "Asking prices are legally binding valuations", "Closed sale prices do not reflect buyer decisions", "A"),
        Q("Which communication practice supports a useful pricing discussion with a seller?", "Explain assumptions, comparable differences, and uncertainty", "Present a single number as certain", "Avoid discussing unfavorable data", "Base advice only on the seller's desired net proceeds", "A"),
        Q("How can property research before a tour improve a market analysis?", "It helps the agent prepare questions and identify features to verify", "It removes the need to view the property", "It proves the public records are always error-free", "It determines the eventual buyer's financing", "A"),
        Q("What is a reasonable response when market conditions change after a listing begins?", "Review current evidence with the seller and adjust strategy if warranted", "Keep the original price regardless of new data", "Change the listing facts to attract more buyers", "Stop reporting showing feedback", "A")
    ]);

    private static readonly IReadOnlyList<QuestionDefinition> DocumentDiligenceFinalQuestions =
    [
        Q("What does a service level agreement do when working in a cloud environment?", "Automates software applications", "Defines what's being provided", "Discounts fees", "Requires regular backups", "B"),
        Q("Agents sometimes do not take enough time to go over complex documents that require signatures with their clients. The electronic mode of communication _____ the problem.", "Alleviates", "Eliminates", "Exacerbates", "Helps with", "C"),
        Q("Based on level of security, how does cloud storage compare to enterprise storage?", "Equally vulnerable to attack", "Impenetrable", "Less vulnerable to attack", "More vulnerable to attack", "D"),
        Q("Agent Barb thinks that there is no reason for her to invest in new software for electronic contracts and e-signatures because her clients will not understand it. According to Nielsen, Barb is _____.", "Overestimating her clients; right", "Overestimating her clients; wrong", "Overestimating technology use; right", "Overestimating technology use; wrong", "A"),
        Q("To properly dispose of electronic information, which of the following is required to be done?", "Accession", "Investigation", "Sanitization", "Storage", "C"),
        Q("Which of the following documents should you retain?", "An e-mail about a lunch meeting", "An IRS determination letter", "A thank-you card from a satisfied client", "Feedback from a disgruntled client", "B"),
        Q("The key to using technology wisely is to stay _____.", "Informed", "In the office", "On the computer", "Out of new programs", "A"),
        Q("If broker Rick has a solid policy in place for handling sensitive documents, which of the following is true?", "He never needs to update it", "He should outsource the policy's maintenance to a third party", "He should periodically review his policy", "He should update it daily", "C"),
        Q("What does wire fraud in a real estate transaction typically involve?", "A buyer untruthfully claiming to have funds available", "Cashier's checks", "Full cooperation of the title company", "Intentionally misdirected down payment or closing funds", "D"),
        Q("Broker Bill is a tech-savvy professional. He loves electronic everything. Bill has had quite a few clients that he never even met. This is _____.", "A best practice", "A requirement of e-signatures", "Not the way to do business in real estate", "The way of the future", "C")
    ];

    private static readonly string[] ResidentialPropertyManagementLessonHeadings =
    [
        "Property Management and the Real Estate Broker/Salesperson",
        "Property Management and the Duties of Property Managers",
        "Property Management and Property Types",
        "Property Management and Property Selection",
        "Property Management and Property Management Agreements",
        "Property Management and Informal Rental Agreements",
        "Property Management and Rental Agreements",
        "Property Management and Standard Landlord and Tenant Obligations",
        "Property Management and Collecting Rent",
        "How to Handle Security Deposits",
        "Property Management and Delinquent Tenants",
        "Property Management and Evictions",
        "Property Management and Pet Policies",
        "Property Management and Tenant Screening",
        "Property Management and Tenant Retention",
        "Property Management and Avoiding Discriminatory Rental Practices",
        "Property Management and Federal Fair Housing Laws",
        "Property Management and Federally Protected Classes",
        "Property Management and Exemptions to the Fair Housing Act",
        "Property Management and Reasonable Accommodations",
        "Property Management and Testing for Discrimination",
        "The Long Island Newsday Report: Lessons Learned",
        "Property Management and Service Animals",
        "Property Management and the Sherman Antitrust Act",
        "Property Management and Data Security"
    ];

    private static readonly IReadOnlyList<QuestionDefinition> ResidentialPropertyManagementFinalQuestions = BalanceCorrectOptions(
    [
        Q("Property manager Tamika aims for good tenant retention by ______.", "Avoiding conflict", "Believing the tenant is always right", "Enforcing rules consistently", "Having flexible rent deadlines", "C"),
        Q("Leo manages property in which he receives part of the rent payment from the government. He manages ______.", "A condominium", "Affordable housing", "A mobile home park", "A single-family rental", "B"),
        Q("Which of the following is one of the seven protected classes under the Fair Housing Act?", "Criminals", "Education", "National origin", "Smokers", "C"),
        Q("Rodrigo wants to file a national origin discrimination complaint. Which category may apply?", "African American", "Australian", "European", "Hispanic", "D"),
        Q("When Curtis writes his rental agreements, his goal is to ______.", "Be as specific as possible", "Be vague", "Keep it short and simple", "Not scare away tenants", "A"),
        Q("Competing brokers divide a city into regions, with each taking one region. Which antitrust violation is this?", "Group boycotting", "Market allocation", "Price fixing", "Targeted acquisitions", "B"),
        Q("A property manager researches contractors to protect against risk. Which agreement provision relates to this practice?", "Contractors clause", "Maintenance clause", "Property management clause", "Reasonable care clause", "D"),
        Q("An owner who rents a spare room in their home says they only want a male roommate. Which statement best describes the situation addressed in this course?", "The owner may qualify for an exemption applicable to shared living arrangements", "The owner is always violating the Fair Housing Act", "The owner is violating the Housing and Community Development Act", "The owner must pay a fine under every circumstance", "A"),
        Q("Which clause in a property management agreement outlines why and when the manager or owner may terminate the contract?", "A hold harmless clause", "An activation clause", "A reasonable care clause", "A termination clause", "D"),
        Q("A tenant reports receiving more application documentation requests than a white friend. What is the appropriate response?", "Inform the tenant of their rights and appropriate complaint options", "Leave it entirely to the property manager", "Tell the tenant not to worry about it", "Tell the tenant to look elsewhere", "A"),
        Q("A property manager wants to reduce misunderstandings about disability-related issues in a multifamily property. What is a helpful practice?", "Be partial to disabled tenants", "Educate all residents", "Let certain behavior slide", "Provide exemptions without review", "B"),
        Q("A property manager wants a clear paper trail for rental finances. Which approach best supports that goal?", "Accept cash rental payments", "Require all cash payments to be made in person", "Avoid accepting cash rental payments", "Rely on a collection agency", "C"),
        Q("A one-year lease is considered a _______ tenancy.", "Holdover tenancy", "Periodic tenancy", "Tenancy at will", "Tenancy for a specific term", "D"),
        Q("Which change in an area could indicate that the neighborhood is up and coming?", "Decreased property sales", "Lower property taxes", "New construction", "Reduced property values", "C"),
        Q("Retaliatory evictions are considered to be ______.", "A last resort", "Common", "Illegal", "Legal", "C"),
        Q("Which law provides an exemption from familial-status discrimination for qualifying housing for older persons?", "Architectural Barriers Act", "Equal Credit Opportunity Act", "Fair Housing Act", "Housing for Older Persons Act", "D"),
        Q("A property manager wants to handle tenant issues without discrimination. What should the manager recognize?", "Avoid all informal meetings", "Forbid all property modifications", "Immediately evict anyone perceived to violate a rule", "Problematic behavior may be involuntary and require an appropriate response", "D"),
        Q("Property managers discuss boycotting a local vendor. What is the safest course of action?", "Only use the vendor to show support", "Suggest a boycott", "Voice agreement with the boycott", "Walk away from the discussion", "D"),
        Q("Which eviction notice specifies why a tenant is being evicted?", "A conditional quit notice", "An eviction notice for cause", "An unconditional quit notice", "A retaliatory eviction notice", "B"),
        Q("Who is typically responsible for setting up and maintaining a reserve fund for maintenance, emergencies, and daily matters?", "The lender", "The property manager", "The property owner", "The tenant", "C")
    ]);

    private static readonly IReadOnlyList<QuestionDefinition> AssistanceAnimalsFinalQuestions = BalanceCorrectOptions(
    [
        Q("A psychiatrist prescribes a rabbit to help a tenant manage anxiety related to a disability. What type of assistance animal is it?", "A disability-related pet", "An emotional support animal", "A prescription anxiety aid", "A service rabbit", "B"),
        Q("What may state laws do regarding service animals?", "Provide additional protections beyond federal law", "Overturn federal law", "Require service animals to be smaller than 25 pounds", "Remove all protections provided by federal law", "A"),
        Q("Which statement about an emotional support animal is true?", "It is always a service animal under the ADA", "It must be a dog or cat", "It is protected only when under 25 pounds", "It serves the needs of a person with a disability", "D"),
        Q("A landlord enforces a lease's no-smoking rule. Is smoking itself a protected class under the federal Fair Housing Act?", "Yes, smoking is a disability", "No, smokers are not a federally protected class", "Yes, smoking is familial status", "No, landlords may ignore all fair housing duties", "B"),
        Q("How does an ADA service animal differ from an emotional support animal?", "A service animal must always be a dog or cat", "A service animal is specifically trained to perform tasks for a person with a disability", "A service animal automatically reduces rent", "A service animal must always be kept on a leash", "B"),
        Q("Which two protected classes were added by the Fair Housing Amendments Act of 1988?", "Disability and sex", "Disability and source of income", "Familial status and disability", "Sex and familial status", "C"),
        Q("A prospective tenant with a deaf child requests a service dog in a no-pets building. What should the housing provider do?", "Charge an additional pet deposit", "Permit the dog as a reasonable accommodation when applicable", "Reject the application", "Allow the tenant to move in but exclude the dog", "B"),
        Q("Under the Americans with Disabilities Act, emotional support animals are ______.", "Not service animals", "Required to be a dog or cat", "Required to be insured", "Permitted on airplanes only when housebroken", "A"),
        Q("A tenant with a disability requests parking near their residence. What does fair housing law generally allow the tenant to request?", "A reasonable accommodation related to equal use and enjoyment of housing", "Any parking space without regard to availability", "A space only if the tenant is a woman", "No accommodation because parking is never related to housing", "A"),
        Q("Which dog would most likely qualify as an ADA-compliant service dog?", "A boxer trained only to sit and stay", "A German Shepherd still learning basic obedience", "A pit bull trained to signal the onset of a seizure", "A poodle that visits nursing home residents", "C"),
        Q("Which buildings are covered by the Architectural Barriers Act of 1968?", "Every private home in use after 1969", "Certain buildings and facilities designed, built, altered, or leased with federal funds after September 1969", "All commercial buildings regardless of funding", "Only public sidewalks built after 1988", "B"),
        Q("A doctor determines that a cat provides comfort that lessens the symptoms of a tenant's disability. What type of animal is this?", "An emotional support animal", "A service animal", "A therapy animal", "A pet by definition in all housing contexts", "A"),
        Q("A property manager receives disability-related information with a reasonable accommodation request. How should it be handled?", "Shared with all other residents", "Kept confidential and shared only with people involved in the decision", "Reported to local authorities", "Shown to unrelated property managers for informal review", "B"),
        Q("How do state fair housing laws relate to federal fair housing law?", "Federal law always displaces more protective state law", "A state must use less restrictive rules", "If state protections are more restrictive, the housing provider must comply with them", "Overlapping laws cancel each other out", "C"),
        Q("Which circumstance may qualify for the Fair Housing Act's owner-occupied small multifamily exemption?", "A landlord rents a unit in a large apartment complex", "An owner lives in a building with no more than four units and does not use a real estate broker", "A seller uses a broker to sell a home", "A tenant rents both units of a duplex", "B"),
        Q("Under the Fair Housing Act, who should a tenant contact to discuss a reasonable accommodation or modification in a rental property?", "Any real estate professional", "The housing provider", "State politicians", "The local government only", "B"),
        Q("The Fair Housing Amendments Act of 1988 added familial status and which other protected class?", "Creed", "Disability", "Sex", "Source of income", "B"),
        Q("Which client is least likely to qualify for a reasonable accommodation for an animal under fair housing law?", "A tenant who wants to pay a larger pet deposit for a pet ferret", "A deaf tenant with a sound-alert service dog", "A blind tenant with a guide dog", "A tenant with an emotional support animal related to a disability", "A"),
        Q("What must a service dog be specially trained to do?", "Call 911 whenever its owner requests help", "Deliver items to its owner", "Open and close doors in every case", "Perform tasks or other work for its owner who has a disability", "D"),
        Q("Which individual meets the Fair Housing Act definition of having a disability?", "A person who dislikes being home alone", "A person addicted to painkillers", "A person with a record of a disability", "A person who has a live-in nanny", "C")
    ]);

    private static IReadOnlyList<QuestionDefinition> BalanceCorrectOptions(IReadOnlyList<QuestionDefinition> questions)
    {
        var targetIndexPattern = new[] { 1, 2, 3, 0 };
        var answerLetters = new[] { "A", "B", "C", "D" };
        return questions.Select((question, questionIndex) =>
        {
            var originalOptions = new[] { question.OptionA, question.OptionB, question.OptionC, question.OptionD };
            var originalCorrectIndex = Array.IndexOf(answerLetters, question.CorrectOption);
            if (originalCorrectIndex < 0)
            {
                throw new InvalidDataException($"Question '{question.Prompt}' has an invalid correct option.");
            }

            var correctAnswer = originalOptions[originalCorrectIndex];
            var reorderedOptions = originalOptions
                .Where((_, optionIndex) => optionIndex != originalCorrectIndex)
                .ToList();
            var targetCorrectIndex = targetIndexPattern[questionIndex % targetIndexPattern.Length];
            reorderedOptions.Insert(targetCorrectIndex, correctAnswer);
            return new QuestionDefinition(
                question.Prompt,
                reorderedOptions[0],
                reorderedOptions[1],
                reorderedOptions[2],
                reorderedOptions[3],
                answerLetters[targetCorrectIndex]);
        }).ToArray();
    }

    private sealed record CourseDefinition(
        Guid Id,
        string Title,
        string Slug,
        string State,
        string SourceRelativePath,
        int ExpectedUnitCount,
        int DurationHours,
        string FinalExamTitle,
        IReadOnlyList<QuestionDefinition> FinalQuestions,
        bool IsHandbook = false,
        string? CoursePrintTitle = null,
        IReadOnlyList<string>? LessonHeadings = null,
        decimal PassPercent = 80m);

    public sealed class UnitDefinition(string title, int orderIndex)
    {
        public string Title { get; } = title;
        public int OrderIndex { get; } = orderIndex;
        public List<LessonDefinition> Lessons { get; } = new();
    }

    public sealed record LessonDefinition(string Title, string TextContent, int OrderIndex);

    private sealed record QuestionDefinition(string Prompt, string OptionA, string OptionB, string OptionC, string OptionD, string CorrectOption);
}
