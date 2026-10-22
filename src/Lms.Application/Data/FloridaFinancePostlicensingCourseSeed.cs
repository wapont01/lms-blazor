using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

internal static class FloridaFinancePostlicensingCourseSeed
{
    private static readonly Guid CourseId = Guid.Parse("14a44b09-80fa-4f74-9aae-9d7b80910007");
    private const string CourseTitle = "Florida Post-Licensing: Real Estate Finance";
    private const string CourseRelativePath = "FLORIDA/COURSES/POST LICENSING/Florida Post-Licensing Real Estate Finance/txts";
    private const string FinalExamTitle = "Florida Post-Licensing Real Estate Finance Final Exam";
    private const string ReviewNote = "Imported from user-supplied materials using four inferred units. Verify the Florida post-licensing course outline, regulatory approval, and inferred final-exam answer key before publication.";
    private const decimal PassPercent = 75m;

    private static readonly string[] UnitTitles =
    [
        "Unit 1: Finance Foundations, Value, and Economics",
        "Unit 2: Mortgage Products and Government Programs",
        "Unit 3: Loan Origination, Underwriting, and Consumer Protection",
        "Unit 4: Mortgage Markets, Property Rights, and Default"
    ];

    private static readonly IReadOnlyList<QuestionDefinition> FinalQuestions =
    [
        Q("What program does the U.S. Department of Veterans Affairs offer to assist Native Americans in financing the purchase, construction, or improvement of homes on federal trust land?", "The Native American Direct Loan", "The Native American Guaranteed Loan", "The Native American Home Loan", "The Native American Veteran Loan", "A"),
        Q("Which of these pieces of legislation work together to prevent discriminatory lending practices?", "The Equal Credit Opportunity Act, the Community Reinvestment Act, and the Home Mortgage Disclosure Act", "The Housing and Economic Recovery Act, the American Recovery and Reinvestment Act, and the Consumer Financial Protection Act", "The Housing and Urban Development Act, the Fair Housing and Employment Act, and the Community Development Act", "The Real Estate Settlement Procedures Act, the Truth in Lending Act, and the Consumer Credit Protection Act", "A"),
        Q("What's a certified opinion or estimate of value of a particular property as of a particular date?", "Appraisal", "Assessment", "Evaluation", "Inspection", "A"),
        Q("Which factor that influences value focuses on the number of properties available?", "Demand", "Scarcity", "Transferability", "Utility", "B"),
        Q("A seller decides to help the buyer qualify for a loan by paying for a ______ of the interest rate, which will be reflected on the borrower's note.", "Graduated buydown", "Level buydown", "Permanent buydown", "Temporary buydown", "C"),
        Q("Dylan is in default on her mortgage. She decides to hand over the deed to her property rather than face foreclosure proceedings. This is an example of ______.", "Deed in lieu of foreclosure", "Delinquency", "Foreclosure", "Short sale", "A"),
        Q("Mutual savings banks are concentrated mostly in the ______ U.S.", "Eastern", "Northern", "Southern", "Western", "A"),
        Q("What's the purpose of the loan application?", "To provide the borrower with an estimate of costs and fees associated with obtaining a loan to determine affordability", "To provide the buyer and seller with a list of credits and debits each party is responsible for paying toward closing", "To provide the lender with a determination of value for the property being purchased", "To provide the lender with information needed to determine a borrower's ability to repay the loan", "D"),
        Q("What does a credit score tell a lender?", "How consistent a consumer's payment history is", "How likely a consumer is to repay a loan", "How much credit a consumer already has", "How much income a consumer earns", "B"),
        Q("The FHA 203(b) loan is a mortgage option for homebuyers who ______.", "Don't quite qualify for a conventional loan", "Have no down payment", "Need to borrow money to rehab the home on top of the mortgage", "Want an adjustable rate mortgage", "A"),
        Q("What's the purpose of a suit to quiet title?", "To add a cloud to a title", "To have a lien placed on a property", "To have a title's ownership history deleted", "To remove or modify clouds on a title", "D"),
        Q("A line of credit based on a percentage of the equity in a property that can be drawn upon for a number of years is known as a ______.", "Conventional mortgage", "Home equity line of credit", "Home equity loan", "Reverse annuity mortgage", "B"),
        Q("What does the FHLB do?", "Provides loans to consumers for the purchase of a primary residence.", "Provides loans to member banks to keep credit and money flowing into local communities.", "Provides loans to the Federal Reserve.", "Sets strict requirements on the amount of interest member banks can charge consumers.", "B"),
        Q("Which of the following is the primary goal for a consumer when reviewing a Loan Estimate?", "Check for accuracy of personal and transaction information and understanding of the loan terms", "Determine which costs can be negotiated with the seller", "Evaluate the lender's ability to service the loan", "Select service providers for the required credit report and property appraisal from the lender-provided list", "A"),
        Q("Which of the following is a possible result of a community growing too little or not at all?", "Communities can never grow too slowly", "Inadequate resources to support the population", "The economy expands too quickly", "The economy stalls", "D"),
        Q("______-only loans are a form of creative buyer financing.", "Index", "Interest", "Margin", "Principal", "B"),
        Q("Seller concessions on a VA loan ______.", "Aren't allowed", "Aren't limited by the VA", "Can't exceed 4% of the sales price", "Can't include payment of the buyer's funding fee", "C"),
        Q("The Johnsons are shopping for a mortgage loan and are attracted to the below-market interest rate offered for the first year of an adjustable rate mortgage. They likely have a ______.", "Bait and switch", "Balloon payment", "Fixed rate", "Teaser rate", "D"),
        Q("Manny is obtaining a home loan. He goes to his local bank and talks with Deborah, who discusses the lending products the bank offers. What is Deborah?", "Bank president", "Mortgage banker", "Mortgage broker", "Mortgage matchmaker", "B"),
        Q("Your client, Bruno, is an investor. He is in the process of selling a fourplex and mentions to you that he hates the idea of the capital gains tax he'll be subject to. You tell him ...", "\"Capital gains do not apply to investment properties.\"", "\"I hear ya, man. Taxes blow.\"", "\"Silly goose. Investors don't pay taxes.\"", "\"You might consider a 1031 tax-deferred exchange.\"", "D"),
        Q("What does each component of the 80/10/10 ratio represent in a split—or piggyback—loan?", "Loan one/interest/down payment", "Loan one/loan two/down payment", "Loan one/loan two/interest", "Principal/interest/insurance", "B"),
        Q("The veteran's certificate of eligibility describes ______.", "The amount of entitlement the veteran has available for the VA home loan program", "The maximum sale price the veteran may pay for a home using the VA home loan program", "The terms of service required for a veteran to be eligible for a VA home loan", "The VA-approved lenders in the veteran's geographic region", "A"),
        Q("A seller you're working with still owes $50,000 on his mortgage but wants to net $20,000 after the mortgage and 7% commission are paid. What is the minimum for which the house must sell?", "$75,269", "$79,645", "$89,900", "$90,645", "A"),
        Q("The U.S. Department of Veterans Affairs offers special programs, which can be used to adapt housing to accommodate the disability, to veterans or military members with permanent and total service-connected disabilities. These programs are _______.", "Covered under disability pay", "Direct loans", "Grants", "Guaranteed loans", "C"),
        Q("With an interest-only payment plan, ______.", "Monthly payments go toward interest, not the principal", "Over time, more of the payment goes toward the principal, and less toward the interest", "The borrower can make payments whenever they want", "The borrower pays a constant amount made up of principal and interest", "A"),
        Q("What is the ability to pay off liens against a property until a foreclosure sale is held?", "Adverse possession", "Dedication by deed", "Involuntary alienation", "Right of redemption", "D"),
        Q("What's typically used to prove there are no encroachments on the property?", "Abstract of title", "Chain of title", "Deed", "Survey", "D"),
        Q("A borrower makes the same monthly mortgage payment for the entire term of the loan. At the end of the loan period, all of the interest and principal is paid off. This is known as a ______ loan.", "Fully amortized", "Growing equity", "Negative amortization", "Reverse annuity", "A"),
        Q("Which statement best describes the method used to reconcile the value found for a property from the three appraisal approaches?", "The appraiser may weigh one or two approaches more heavily than the others, as appropriate for the property type.", "The appraiser will weigh the cost approach more heavily than the other two approaches.", "The appraiser will weigh the sales comparison approach more heavily than the other two approaches.", "The appraiser will weigh the value produced from each approach equally.", "A"),
        Q("What's another term for deed in lieu of foreclosure?", "Delinquency", "Involuntary conveyance", "Short sale", "Voluntary conveyance", "D"),
        Q("Juanita invests in mortgage-backed securities. What is the likely impact on her as an investor when mortgage interest rates rise?", "Juanita will stop receiving payments from her investment.", "There is no impact to Juanita.", "The return on her investment decreases.", "The returns on her investment may increase.", "C"),
        Q("Which of the following outcomes is most likely if the Fed decreases reserve requirements?", "A decrease in lending activity", "An increase in interest rates", "A slowdown in the economy", "The economy will heat up", "D"),
        Q("Ruby is selling her home. The buyer, who's not a veteran, would like to assume Ruby's VA loan. Is this allowed?", "No, a VA loan may only be assumed by another eligible veteran.", "No, VA loans are not assumable.", "Yes, an assumption is allowed and will automatically release Ruby from liability for the loan if the new borrower defaults.", "Yes, an assumption is allowed, but Ruby should have the buyer sign a release of liability, or she risks being held liable if the new borrower defaults.", "D"),
        Q("A bank obtaining a loan from its Federal Reserve bank is likely to do so at what rate?", "Discount rate", "Federal funds rate", "Fixed rate", "Prime rate", "A"),
        Q("Calculate the housing ratio based on the following information: $1,500 condo payment; $100 association fees per month; $150 monthly student loan payment; $300 car payment (with only four payments remaining); and a gross monthly income of $5,000.", "30%", "32%", "35%", "41%", "B"),
        Q("What's one of the main differences between a traditional mortgage loan and an interim loan?", "Commercial banks don't do interim financing.", "Interim financing isn't available to developers.", "Interim loans have better terms and rates.", "The length of the loan differs.", "D"),
        Q("What is a qualified mortgage?", "One in which the borrower is qualified", "One that is offered by a credit union", "One that is offered by a savings and loan institution", "One that meets certain lending standards", "D"),
        Q("The Real Estate Settlement Procedures Act is a consumer protection statute that ______.", "Prohibits lenders from denying credit to an applicant for a mortgage based solely on the location of the property in a certain geographic area", "Regulates the type and amount of fees and commissions that settlement service providers may charge to borrowers in a residential real estate transaction", "Requires lenders to disclose all credit terms in an advertisement for credit if any of the terms are given", "Requires lenders to provide the consumer with certain disclosures for residential real estate transactions and prohibits kickbacks among settlement service providers", "D"),
        Q("Which federal lending act was introduced to combat lending discrimination in low- and moderate-income neighborhoods?", "Community Reinvestment Act", "Consumer Credit Protection Act", "Equal Credit Opportunity Act", "Home Mortgage Disclosure Act", "A"),
        Q("Which of the following best describes what Ginnie Mae does in the secondary mortgage market?", "Guarantees mortgage-backed securities", "Issues loans to consumers", "Issues mortgage-backed securities", "Purchases government loans", "A"),
        Q("Which of the following statements is true about the FHA Energy Efficient Mortgage?", "It's only available for use on home purchases where the property is more than 15 years old.", "It's only available to first-time homebuyers.", "It's used in conjunction with other FHA loan programs to finance the cost of adding energy-efficient features to new or existing houses.", "It's used to finance properties that have an annual energy usage that is at least 20% less than the national average.", "C"),
        Q("Ruth and Paul live in a rural area where emergency services, health care, and even neighbors take time and travel to access. They know their house could use some renovation to make it safer for their kids. What government program offers loans and grants to assist people like Ruth and Paul?", "Farm Credit Administration", "Farm Credit System", "Farmer Mac", "USDA Rural Development", "D"),
        Q("The components that make up a borrower's mortgage payment can best be remembered using which acronym?", "CIIA", "DUST", "OLD CAR", "PITI", "D"),
        Q("Which approach to value used by appraisers relies on comparable properties to help determine the adjusted market price?", "Cost approach", "Income approach", "Market value approach", "Sales comparison approach", "D"),
        Q("With which approach to value does the appraiser analyze a property's ability to earn future income?", "Cost approach", "Income approach", "Sales comparison approach", "Taxable revenue approach", "B"),
        Q("The ______ balances the powers of federal agencies by requiring them to follow specific processes and procedures, such as providing for public participation in rulemaking.", "Administrative Procedure Act", "Federal Agency Process Act", "Federal Agency Restrictive Treatment Act", "Federal Powers and Agency Limitation Act", "A"),
        Q("Let's say you have an investor with an NOI of $40,000 and a debt service of $30,000. What is your investor's debt service ratio?", "$10,000", "1.25", "1.33", ".25", "C"),
        Q("Which of the following statements is true about the FHA Section 251 ARM?", "No down payment is required.", "The FHA sets a maximum number of percentage points that the interest rate may change over the life of the loan.", "The interest rate is adjusted every year for the life of the loan", "The interest rate will increase over the life of the loan, but will never decrease.", "B"),
        Q("Adrian purchased a house for $220,000, financing $200,000 using a 30-year conventional mortgage at 4% interest. His monthly principal and interest payment is $955. The annual property taxes are $1,400, and the combined annual insurance premium is $900. What's Adrian's PITI payment each month?", "$1,146.67", "$1,153.67", "$1,176.50", "$1,364.94", "A"),
        Q("The purpose of the ______ is to promote fairness and transparency for mortgages and other financial products and services through education, enforcement, and research.", "Consumer Credit Protection Act", "Consumer Financial Protection Bureau", "Department of Housing and Urban Development", "Equal Credit Opportunity Act", "B")
    ];

    public static async Task EnsureAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        var textDirectory = Path.Combine(root, "docs", CourseRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(textDirectory))
        {
            return;
        }

        var sourceFinalExam = Path.Combine(textDirectory, "final exam.txt");
        if (!File.Exists(sourceFinalExam))
        {
            throw new FileNotFoundException("The Florida Finance final exam source was not found.", sourceFinalExam);
        }

        var sourceQuestionCount = (await File.ReadAllLinesAsync(sourceFinalExam))
            .Count(line => line.Trim().StartsWith("Question ", StringComparison.Ordinal));
        if (sourceQuestionCount != FinalQuestions.Count)
        {
            throw new InvalidDataException(
                $"The Florida Finance final exam contains {sourceQuestionCount} questions; expected {FinalQuestions.Count}.");
        }

        var units = UnitTitles
            .Select((title, index) => new AdditionalCourseSeed.UnitDefinition(title, index + 1))
            .ToArray();
        var textFiles = Directory.GetFiles(textDirectory, "*.txt", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "final exam.txt", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        foreach (var path in textFiles)
        {
            var text = await File.ReadAllTextAsync(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException($"Florida Finance lesson source '{Path.GetFileName(path)}' is empty.");
            }

            var title = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(title, "practice exam results", StringComparison.OrdinalIgnoreCase))
            {
                title = "Practice Exam Review";
            }
            else if (string.Equals(title, "mid-point exam", StringComparison.OrdinalIgnoreCase))
            {
                title = "Mid-point Exam Review";
            }

            var unitIndex = GetUnitIndex(Path.GetFileNameWithoutExtension(path));
            var unit = units[unitIndex];
            unit.Lessons.Add(new AdditionalCourseSeed.LessonDefinition(
                title,
                text,
                unit.Lessons.Count + 1));
        }

        if (units.Any(unit => unit.Lessons.Count == 0))
        {
            throw new InvalidDataException("Florida Finance materials did not populate all four inferred units.");
        }

        await EnsureCourseAsync(dbContext, units);
    }

    private static int GetUnitIndex(string fileName)
    {
        if (ContainsAny(fileName,
                "FHA", "VA Loan", "USDA", "adjustable rate", "ARM", "reverse mortgage", "HELOC",
                "home equity", "government loan", "loan programs", "loan types", "refinance",
                "Farm Credit", "Farmer Mac"))
        {
            return 1;
        }

        if (ContainsAny(fileName,
                "application", "credit", "underwriting", "qualifying", "income", "closing",
                "disclosure", "loan estimate", "RESPA", "ECOA", "HMDA", "CFPB", "predatory",
                "processing", "origination", "settlement", "SAFE Act", "loan officer"))
        {
            return 2;
        }

        if (ContainsAny(fileName,
                "foreclosure", "default", "secondary market", "Fannie Mae", "Freddie Mac",
                "Ginnie Mae", "title", "lien", "mortgage-backed", "mortgage backed",
                "deed", "conveyance", "security instrument", "servicing", "practice exam",
                "mid-point exam"))
        {
            return 3;
        }

        return 0;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static async Task EnsureCourseAsync(
        ApplicationDbContext dbContext,
        IReadOnlyList<AdditionalCourseSeed.UnitDefinition> units)
    {
        var course = await dbContext.Courses
            .Include(existing => existing.Units)
                .ThenInclude(unit => unit.Lessons)
            .FirstOrDefaultAsync(existing => existing.Id == CourseId);
        var exam = course is null
            ? null
            : await dbContext.CourseExams
                .Include(existing => existing.Questions)
                .FirstOrDefaultAsync(existing =>
                    existing.CourseId == CourseId
                    && existing.UnitId == null
                    && existing.Title == FinalExamTitle);

        var unitsAreCurrent = course is not null
            && course.Units
                .OrderBy(unit => unit.OrderIndex)
                .Select(unit => unit.Title)
                .SequenceEqual(units.Select(unit => unit.Title))
            && course.Units
                .OrderBy(unit => unit.OrderIndex)
                .SelectMany(unit => unit.Lessons.OrderBy(lesson => lesson.OrderIndex))
                .Select(lesson => (lesson.Title, lesson.TextContent))
                .SequenceEqual(units.SelectMany(unit =>
                    unit.Lessons.Select(lesson => (lesson.Title, (string?)lesson.TextContent))));
        var examIsCurrent = exam is not null
            && exam.PassPercent == PassPercent
            && exam.Questions
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
            && course.MinimumPassingPercent == (int)PassPercent
            && course.Industry == CourseIndustries.Mortgage
            && course.ComplianceType == CourseComplianceTypes.Postlicensing
            && course.RequiresProctoredExam
            && course.ReviewStatus == CourseReviewStatuses.Draft
            && !course.IsPublished
            && unitsAreCurrent
            && examIsCurrent)
        {
            return;
        }

        course ??= new Course { Id = CourseId };
        if (dbContext.Entry(course).State == EntityState.Detached)
        {
            dbContext.Courses.Add(course);
        }

        course.Title = CourseTitle;
        course.Slug = "florida-post-licensing-real-estate-finance";
        course.Description = "A 30-hour Florida real estate finance post-licensing course based on the supplied materials. Its four-unit structure is inferred and must be reviewed against the official approved course outline before publication.";
        course.Level = "Post-Licensing";
        course.DurationHours = 30;
        course.CreditHours = 30;
        course.ComplianceType = CourseComplianceTypes.Postlicensing;
        course.ContinuingEducationType = null;
        course.Industry = CourseIndustries.Mortgage;
        course.DeliveryMethod = CourseDeliveryMethods.DistanceEducation;
        course.State = "Florida";
        course.RequiredInstructionalMinutes = 1800;
        course.MinimumPassingPercent = (int)PassPercent;
        course.MinimumAttendancePercent = 80;
        course.RequiresProctoredExam = true;
        course.CompletionWindowDays = 180;
        course.Price = 0m;
        course.IsPublished = false;
        course.IsArchived = false;
        course.ReviewStatus = CourseReviewStatuses.Draft;
        course.ReviewNote = ReviewNote;
        course.UpdatedAt = DateTime.UtcNow;

        if (!unitsAreCurrent)
        {
            var existingExams = await dbContext.CourseExams
                .Where(existing => existing.CourseId == CourseId)
                .ToListAsync();
            dbContext.CourseExams.RemoveRange(existingExams);
            var existingUnitExams = await dbContext.CourseUnitExamDefinitions
                .Where(existing => existing.CourseId == CourseId)
                .ToListAsync();
            dbContext.CourseUnitExamDefinitions.RemoveRange(existingUnitExams);
            dbContext.Units.RemoveRange(course.Units);
            await dbContext.SaveChangesAsync();
            exam = null;

            foreach (var unitDefinition in units)
            {
                var unit = new Unit
                {
                    CourseId = CourseId,
                    Title = unitDefinition.Title,
                    OrderIndex = unitDefinition.OrderIndex
                };
                dbContext.Units.Add(unit);
                foreach (var lessonDefinition in unitDefinition.Lessons)
                {
                    unit.Lessons.Add(new Lesson
                    {
                        UnitId = unit.Id,
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

        if (exam is not null)
        {
            dbContext.CourseExams.Remove(exam);
        }

        var finalExam = new CourseExam
        {
            CourseId = CourseId,
            Title = FinalExamTitle,
            PassPercent = PassPercent,
            IsRequired = true
        };
        dbContext.CourseExams.Add(finalExam);
        foreach (var question in FinalQuestions.Select((item, index) => new ExamQuestion
        {
            CourseExamId = finalExam.Id,
            Prompt = item.Prompt,
            OptionA = item.OptionA,
            OptionB = item.OptionB,
            OptionC = item.OptionC,
            OptionD = item.OptionD,
            CorrectOption = item.CorrectOption,
            OrderIndex = index + 1
        }))
        {
            dbContext.ExamQuestions.Add(question);
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

    private sealed record QuestionDefinition(
        string Prompt,
        string OptionA,
        string OptionB,
        string OptionC,
        string OptionD,
        string CorrectOption);
}
