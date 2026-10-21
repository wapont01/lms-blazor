using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

internal static class FloridaBrokeragePostlicensingCourseSeed
{
    private static readonly Guid CourseId = Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910006");
    private const string CourseTitle = "FL 30-Hour Real Estate Brokerage";
    private const string CourseRelativePath = "FLORIDA/COURSES/POST LICENSING/FL 30-Hour Real Estate Brokerage/txts";
    private const string FinalExamTitle = "FL 30-Hour Real Estate Brokerage Final Exam";
    private const string ReviewNote = "Imported from user-supplied materials using four inferred units. Verify Florida post-licensing approval, the official course outline, the inferred final-exam answer key, and proctoring requirements before publication.";

    private static readonly IReadOnlyList<UnitDefinition> Units =
    [
        new(
            "Unit 1: Ethical and Legal Business Practices",
            [
                "Ethical Business Practices Study Sheet.txt",
                "Legal Business Practices Study Sheet.txt",
                "Managing Risk_ Protecting the License.txt",
                "License Law Violation Summary.txt"
            ]),
        new(
            "Unit 2: Organizing the Brokerage",
            [
                "Brokerage Business Legal Structures.txt",
                "Organizational Structures_ Centralized and Decentralized.txt",
                "Brokerage Personnel Positions.txt",
                "Broker Supervision Requirements and Liability.txt"
            ]),
        new(
            "Unit 3: Planning the Brokerage",
            [
                "Business Plan Concepts.txt",
                "SWOT Analysis.txt",
                "The Marketing Plan.txt",
                "The Cash Flow Statement.txt"
            ]),
        new(
            "Unit 4: Running a Brokerage",
            [
                "Management Skills, Theories, and Functions Study Sheet.txt",
                "Brokerage Finances Study Sheet.txt",
                "Running a Brokerage_ Managing Risk Study Sheet.txt",
                "Running a Brokerage_ Development and Coaching Study Sheet.txt",
                "Recordkeeping and Risk Management.txt",
                "practice final exam.txt"
            ])
    ];

    private static readonly IReadOnlyList<QuestionDefinition> FinalQuestions =
    [
        Q("Why is errors and omissions insurance recommended for all licensees?", "It eliminates risk.", "It provides coverage for all situations.", "Risk is an inherent part of real estate activities.", "There are fewer lawsuits.", "C"),
        Q("Ways to minimize computer risks include avoiding public networks, unsecure sites, and ______ sites.", "All", "Encrypted", "Secure", "Suspicious", "D"),
        Q("Everyone in the firm should receive training on your firm’s ________ to be proactive in preventing activities that could damage the firm’s reputation.", "Chain of command", "Policies and procedures manual", "Public relations policy", "Transaction file documentation requirements", "B"),
        Q("Errors and omissions insurance covers ______ who are sued by consumers for inadvertent mistakes in a real estate transaction.", "Buyers", "Homeowners", "Licensees", "Sellers", "C"),
        Q("What type of agency exists because of the actions and behavior of the parties?", "Action agency", "Behavioral agency", "Express agency", "Implied agency", "D"),
        Q("At what level or levels does antitrust oversight exist?", "Federal", "Federal and state", "Global", "State", "B"),
        Q("A set of guidelines for the ethical conduct of real estate licensees is called a _________.", "Code of ethics", "Contract", "Option", "Rule book", "A"),
        Q("Which of the following is an example of what would be included in a mission statement?", "“We intend to become the top choice for relocation services in the area.”", "“We’ll do what it takes to be known as the best brokerage in town.”", "“We treat our customers with respect, honesty, and integrity.”", "“We want to be number one in condominium sales.”", "C"),
        Q("Two designated agents, Mitch and Mickey, represent the buyer and seller in one transaction. If they are from the same brokerage, what is the broker's role?", "The agents are from different brokerages.", "Both brokers are dual agents.", "The broker uses designated agents for the parties.", "The broker is a designated agent for both parties.", "C"),
        Q("A written agency agreement explains both the agent’s responsibilities and the clients’ responsibilities. What type of agency exists?", "Implied agency", "Oral express agency", "Undocumented agency", "Written express agency", "D"),
        Q("When does a sales contract generally become legally binding?", "After an attorney has reviewed it", "Once the buyer and seller have signed it", "When the seller verbally agrees to the offer", "When the buyer signs and sends it to the seller", "B"),
        Q("When may an automatic renewal clause be used in a buyer agency agreement?", "If the property is valued at $250,000 or more", "Never", "When the buyer agrees in writing", "When the property is valued at less than $250,000", "C"),
        Q("A buyer representation agreement is similar to which document used when representing a seller?", "A consumer notice", "A listing agreement", "A seller disclosure", "A seller's net sheet", "B"),
        Q("What might lead a person opening a brokerage to select a franchise?", "A unique vision for the brokerage", "Control over all branding", "Entering the market with a recognized brand", "Minimizing fixed expenses", "C"),
        Q("Which term best describes when a party performs some, but not all, of the terms of a contract?", "Impossibility of performance", "Partial performance", "Performance of a contract", "Specific performance", "B"),
        Q("Which entity can represent a state and its citizens in antitrust cases?", "The attorney general", "The Department of Commerce and Insurance", "The Department of Justice", "The Federal Trade Commission", "A"),
        Q("Which act encourages fair, truthful, non-deceptive, and evidence-supported advertising claims?", "The Federal Trade Commission Act", "The Mortgage Disclosure Act", "The Real Estate Settlement Procedures Act", "The Sherman Act", "A"),
        Q("If an endangered species or wetlands exist on a site, which statement is true?", "The developer can usually buy the property for a lower price.", "The sales price will be higher.", "These environmental concerns can directly affect property development.", "These conditions will not affect development.", "C"),
        Q("What is the first step in developing a brokerage risk management plan?", "Remove licensees who engage in risky behavior.", "Enforce the policies and procedures manual.", "Evaluate risk-management alternatives.", "Identify the specific risks most likely to occur or cause damage.", "D"),
        Q("Under which type of buyer agency agreement does a buyer receive the most representation from a single agent?", "Exclusive agency", "Exclusive right to represent", "Limited agency", "Open agency", "B"),
        Q("The risk identification process for a brokerage involves identifying ______.", "All risks posed by business operations", "Risks faced by lenders", "Risks specific to the real estate industry", "The level of risk for activity categories", "A"),
        Q("Which type of lease generally has the landlord pay taxes, maintenance, and insurance?", "Gross lease", "Net lease", "Percentage lease", "Triple net lease", "A"),
        Q("Which statement about recruiting licensees is true?", "It is illegal to recruit licensees from another brokerage.", "Recruiting brochures at schools are an effective way to recruit experienced licensees.", "Recruitment should target licensees who fit the firm's needs.", "The best method is to advertise to everyone and sort applicants later.", "C"),
        Q("Which phrase in a property advertisement is prohibited under fair housing law?", "Five bedrooms", "Ideal location", "Mother-in-law suite", "Perfect for a couple without kids", "D"),
        Q("Two designated agents represent the buyer and seller in one transaction. If they are from the same brokerage, what is the broker's role?", "The agents are from different brokerages.", "Both brokers are dual agents.", "The broker uses designated agents for the parties.", "The broker is a designated agent for both parties.", "C"),
        Q("Price levels, vacancy rates, new building permits, inventory rates, and sales volume are all ______.", "Fixed", "Market indicators", "Marketing techniques", "Unreliable", "B"),
        Q("What is the minimum period for retaining transaction documents?", "10 years", "As required by regulatory agencies", "Five years", "Two years", "C"),
        Q("According to the supplied course material, what must be conducted before a project may be approved for development?", "An environmental impact study", "An investigation into the developer’s credit history", "A site inspection", "A test for hazardous materials", "A"),
        Q("Which state official may bring a lawsuit on behalf of citizens under the legal concept of parens patriae?", "The attorney general", "The chief justice", "The comptroller", "The governor", "A"),
        Q("Under the Sherman Antitrust Act, what maximum fines may apply to corporations and individuals?", "Up to $100,000 for corporations and $1,000 for individuals", "Up to $100 million for corporations and $1 million for individuals", "Up to $10 million for corporations and $1 million for individuals", "Up to $1 million for corporations and $100,000 for individuals", "B"),
        Q("An agreement gives Fiona the right to buy or not buy Nick’s vacant lot for $310,000 by a specific date. What is this agreement?", "A bilateral contract", "An option", "A sales contract", "A uniform procedural contract", "B"),
        Q("Which analytical tool can help a business owner create a value proposition?", "CPI", "Demographic data", "GDP", "SWOT", "D"),
        Q("Which act introduced the “innocent landowner” defense for certain contamination that predated ownership?", "The Comprehensive Environmental Response, Compensation, and Liability Act (CERCLA)", "The State Environmental Quality Review Act of 1976 (SEQRA)", "The Superfund Amendments and Reauthorization Act (SARA)", "The Clean Air Act", "C"),
        Q("When does express agency exist?", "When agency is implied", "When a written or spoken agreement is in place", "When the parties act as if they have an agency relationship", "When the parties are in a hurry", "B"),
        Q("Which document records Rufus’s purchase offer to the seller for a property?", "A buyer representation agreement", "A listing agreement", "A promissory note", "A sales contract", "D"),
        Q("Which federal law identifies hazardous-substance sites and requires responsible parties to clean them up?", "The Comprehensive Environmental Response, Compensation, and Liability Act", "The Department of Housing and Urban Development", "A state department of environmental quality", "The Superfund Amendments and Reauthorization Act", "A"),
        Q("Which statement applies to both employees and independent contractors who are real estate licensees?", "They can set their own work hours.", "They are both subject to broker supervision.", "They are not required to work in a specific location.", "They are paid by the job and not by the hour.", "B"),
        Q("Activities that restrain trade and reduce competition and consumer choice are ______ violations.", "Agency law", "Antitrust", "Fair housing", "License law", "B"),
        Q("With which type of commercial lease does the landlord typically pay some taxes, insurance, and maintenance expenses?", "Gross lease", "Net lease", "Percentage lease", "Triple net lease", "A"),
        Q("Which is an example of a specific tactic in implementing a business plan?", "Enroll two licensees in an online CRS course.", "Ensure at least two licensees are Certified Residential Specialists.", "Move to an office with additional space for expansion.", "Utilize web marketing tools more effectively.", "A"),
        Q("What is a key difference between an exclusive right-to-sell and an exclusive agency listing?", "With exclusive agency, the seller may avoid commission if the seller finds the buyer.", "An exclusive right-to-sell listing automatically permits dual agency.", "One agreement is for buyers and the other is for sellers.", "There is no difference between them.", "A"),
        Q("To reduce computer-system risks, avoid public networks, ______ sites, and suspicious sites.", "All", "Encrypted", "Secure", "Unsecure", "D"),
        Q("If a broker’s licensee is sued for misconduct, what may happen to the broker?", "The broker may be held liable for failing to supervise the licensee.", "The broker may sue the licensee for conduct unbecoming a professional.", "The broker may ask a judge to revoke the licensee’s license for two years.", "The broker may revoke the licensee’s license immediately.", "A"),
        Q("A broker encourages a seller to break an existing agency agreement and sign with the broker instead. What violation does this illustrate?", "Contractual interference", "Failure to disclose an agency relationship", "Substantial and willful misrepresentation", "Unauthorized practice of law", "A"),
        Q("Which statement about a sole proprietor’s liability is true?", "She cannot hire employees.", "She must operate under her legal name.", "She is legally shielded from taxes.", "She is personally liable for debts and taxes.", "D"),
        Q("A broker may be liable for acts or omissions of licensees under the doctrine of ______.", "No-fault liability", "Substitution liability", "Transfer of liability", "Vicarious liability", "D"),
        Q("With which pricing structure may clients be over- or under-charged for actual services performed, depending on property and market conditions?", "Flat-fee service", "Hourly charge for services performed", "Traditional pricing", "Unbundled service", "C"),
        Q("A broker loses two of 10 salespeople and decides not to replace them. Which may be an advantage to the brokerage?", "A decrease in revenue", "An increase in revenue", "An increase in support the broker must provide", "Reduced expense to the brokerage", "D"),
        Q("What is the role of a server in a business computer system?", "Creates spreadsheets, compiles data, and generates reports", "Encrypts data, detects viruses, and runs protection programs", "Manages data storage and retrieval and provides teleconferencing", "Runs programs, stores files, and sends information to connected computers", "D"),
        Q("Which lease term may help cover a tenant’s start-up costs through owner-funded office improvements?", "Add-on factor", "Common area maintenance", "Prevailing market rate", "Tenant improvement allowances", "D")
    ];

    public static async Task EnsureAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var textDirectory = Path.Combine(
            root,
            "docs",
            CourseRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var units = new List<AdditionalCourseSeed.UnitDefinition>();
        foreach (var unitDefinition in Units.Select((unit, index) => (unit, index)))
        {
            var unit = new AdditionalCourseSeed.UnitDefinition(unitDefinition.unit.Title, unitDefinition.index + 1);
            foreach (var fileName in unitDefinition.unit.SourceFiles)
            {
                var sourcePath = Path.Combine(textDirectory, fileName);
                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        $"Required Florida Brokerage lesson source '{fileName}' was not found.",
                        sourcePath);
                }

                var text = await File.ReadAllTextAsync(sourcePath);
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidDataException($"Florida Brokerage lesson source '{fileName}' is empty.");
                }

                unit.Lessons.Add(new AdditionalCourseSeed.LessonDefinition(
                    string.Equals(fileName, "practice final exam.txt", StringComparison.OrdinalIgnoreCase)
                        ? "Practice Final Exam"
                        : Path.GetFileNameWithoutExtension(fileName),
                    text,
                    unit.Lessons.Count + 1));
            }

            units.Add(unit);
        }

        await EnsureCourseAsync(dbContext, units);
    }

    private static async Task EnsureCourseAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<AdditionalCourseSeed.UnitDefinition> units)
    {
        var course = await dbContext.Courses
            .Include(existing => existing.Modules)
                .ThenInclude(module => module.Lessons)
            .FirstOrDefaultAsync(existing => existing.Id == CourseId);
        var assessment = course is null
            ? null
            : await dbContext.CourseAssessments
                .Include(existing => existing.Questions)
                .FirstOrDefaultAsync(existing =>
                    existing.CourseId == CourseId
                    && existing.ModuleId == null
                    && existing.Title == FinalExamTitle);

        var modulesAreCurrent = course is not null
            && course.Modules
                .OrderBy(module => module.OrderIndex)
                .Select(module => module.Title)
                .SequenceEqual(units.Select(unit => unit.Title))
            && course.Modules
                .OrderBy(module => module.OrderIndex)
                .SelectMany(module => module.Lessons.OrderBy(lesson => lesson.OrderIndex))
                .Select(lesson => lesson.Title)
                .SequenceEqual(units.SelectMany(unit => unit.Lessons).Select(lesson => lesson.Title));
        var assessmentIsCurrent = assessment is not null
            && assessment.PassPercent == 75m
            && assessment.Questions
                .OrderBy(question => question.OrderIndex)
                .Select(question => new QuestionDefinition(
                    question.Prompt,
                    question.OptionA,
                    question.OptionB,
                    question.OptionC,
                    question.OptionD,
                    question.CorrectOption))
                .SequenceEqual(FinalQuestions);

        if (course is not null
            && course.Title == CourseTitle
            && course.DurationHours == 30
            && course.CreditHours == 30
            && course.RequiredInstructionalMinutes == 1800
            && course.MinimumPassingPercent == 75
            && course.Industry == CourseIndustries.RealEstate
            && course.ComplianceType == CourseComplianceTypes.Postlicensing
            && course.RequiresProctoredExam
            && course.ReviewStatus == CourseReviewStatuses.Draft
            && !course.IsPublished
            && modulesAreCurrent
            && assessmentIsCurrent)
        {
            return;
        }

        course ??= new Course { Id = CourseId };
        if (dbContext.Entry(course).State == EntityState.Detached)
        {
            dbContext.Courses.Add(course);
        }

        course.Title = CourseTitle;
        course.Slug = "florida-30-hour-real-estate-brokerage";
        course.Description = "A 30-hour Florida real estate brokerage post-licensing course. The four-unit structure is inferred from supplied materials and requires review against the official approved course outline before publication.";
        course.Level = "Post-Licensing";
        course.DurationHours = 30;
        course.CreditHours = 30;
        course.ComplianceType = CourseComplianceTypes.Postlicensing;
        course.ContinuingEducationType = null;
        course.Industry = CourseIndustries.RealEstate;
        course.DeliveryMethod = CourseDeliveryMethods.DistanceEducation;
        course.State = "Florida";
        course.RequiredInstructionalMinutes = 1800;
        course.MinimumPassingPercent = 75;
        course.MinimumAttendancePercent = 80;
        course.RequiresProctoredExam = true;
        course.CompletionWindowDays = 180;
        course.Price = 0m;
        course.IsPublished = false;
        course.IsArchived = false;
        course.ReviewStatus = CourseReviewStatuses.Draft;
        course.ReviewNote = ReviewNote;
        course.UpdatedAt = DateTime.UtcNow;

        if (!modulesAreCurrent)
        {
            var existingAssessments = await dbContext.CourseAssessments
                .Where(existing => existing.CourseId == CourseId)
                .ToListAsync();
            dbContext.CourseAssessments.RemoveRange(existingAssessments);
            var existingCheckpoints = await dbContext.CourseCheckpointDefinitions
                .Where(existing => existing.CourseId == CourseId)
                .ToListAsync();
            dbContext.CourseCheckpointDefinitions.RemoveRange(existingCheckpoints);
            dbContext.Modules.RemoveRange(course.Modules);
            await dbContext.SaveChangesAsync();
            assessment = null;

            foreach (var unitDefinition in units)
            {
                var module = new Module
                {
                    CourseId = CourseId,
                    Title = unitDefinition.Title,
                    OrderIndex = unitDefinition.OrderIndex
                };
                dbContext.Modules.Add(module);
                foreach (var lessonDefinition in unitDefinition.Lessons)
                {
                    module.Lessons.Add(new Lesson
                    {
                        ModuleId = module.Id,
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

        if (assessment is not null)
        {
            dbContext.CourseAssessments.Remove(assessment);
        }

        var finalAssessment = new CourseAssessment
        {
            CourseId = CourseId,
            Title = FinalExamTitle,
            PassPercent = 75m,
            IsRequired = true
        };
        dbContext.CourseAssessments.Add(finalAssessment);
        foreach (var question in FinalQuestions.Select((item, index) => new AssessmentQuestion
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

    private static QuestionDefinition Q(
        string prompt,
        string optionA,
        string optionB,
        string optionC,
        string optionD,
        string correctOption) =>
        new(prompt, optionA, optionB, optionC, optionD, correctOption);

    private sealed record UnitDefinition(string Title, IReadOnlyList<string> SourceFiles);

    private sealed record QuestionDefinition(
        string Prompt,
        string OptionA,
        string OptionB,
        string OptionC,
        string OptionD,
        string CorrectOption);
}
