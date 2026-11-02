using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

public static class CourseExamContentSeed
{
    private static readonly IReadOnlyDictionary<string, CourseExamQuestionSet> QuestionSets =
        new Dictionary<string, CourseExamQuestionSet>(StringComparer.OrdinalIgnoreCase)
        {
            ["Assistance Animals And Fair Housing"] = new(
                [
                    Q("Under the Fair Housing Act, what is an assistance animal?", "Only a dog trained for public access", "An animal that works, assists, or provides disability-related support", "Any pet allowed by a lease", "An animal used only in group therapy", "B"),
                    Q("How does an emotional support animal differ from a service animal?", "An ESA is a common animal that provides disability-related emotional support; a service animal is trained to perform tasks", "An ESA must be trained for public access", "A service animal is used only for group visits", "There is no distinction under any law", "A"),
                    Q("A tenant requests an assistance-animal accommodation and the disability-related need is not apparent. What is an appropriate next step?", "Automatically deny the request", "Request reliable information limited to verifying the disability-related need", "Demand the tenant disclose a complete medical history", "Charge a pet deposit before reviewing the request", "B"),
                    Q("What best describes the purpose of a reasonable accommodation in housing?", "To change a rule or policy when needed for equal use and enjoyment of housing", "To provide a property upgrade unrelated to disability", "To waive every lease term permanently", "To replace the tenant's rental agreement", "A"),
                    Q("Which statement about therapy dogs is most accurate?", "They are generally trained to interact with groups, unlike service animals trained for an individual's needs", "They are automatically service animals under every law", "They can only live in hospitals", "They must be accepted as pets by every landlord", "A")
                ],
                [
                    Q("When may a housing provider deny an assistance-animal accommodation?", "Whenever the lease has a no-pets rule", "When supported evidence establishes a legally recognized reason, such as an individualized direct threat that cannot be reduced", "Whenever the animal is not a dog", "When another tenant dislikes animals", "B"),
                    Q("What is the best way to evaluate an assistance-animal request?", "Use an individualized, timely review of the request and relevant information", "Apply a blanket breed restriction", "Require the same documents in every case", "Treat the animal as a pet without considering the request", "A"),
                    Q("How should a property manager distinguish an accommodation from a modification?", "An accommodation changes a rule or practice; a modification changes a physical feature", "An accommodation is always structural; a modification is always a policy", "They are interchangeable terms", "A modification changes the rent only", "A"),
                    Q("A housing provider charges a tenant a pet fee for an approved assistance animal. What is the concern?", "Assistance animals are not treated as pets for accommodation purposes", "The fee should be collected twice", "Only service dogs may be exempt", "The fee is required by the Fair Housing Act", "A"),
                    Q("What should a real estate professional do when uncertain about a disability-related housing request?", "Avoid making assumptions and consult the applicable law or qualified compliance guidance", "Tell the tenant to withdraw the request", "Promise a particular legal outcome", "Share the tenant's medical information with other residents", "A")
                ]),
            ["Document Diligence: Safeguarding Your Transactions"] = new(
                [
                    Q("What generally makes a real estate sale contract enforceable?", "A written agreement signed by the parties", "An unsigned text message from one party", "A listing photograph", "A verbal estimate of closing costs", "A"),
                    Q("What is the proper role of a contingency in a purchase agreement?", "It states a condition and related deadlines that affect a party's obligations", "It guarantees financing approval", "It replaces the signatures", "It removes the need to disclose material facts", "A"),
                    Q("A client asks an agent to draft a custom legal clause with unclear consequences. What should the agent do?", "Recommend that the client consult a qualified attorney", "Draft language from memory and guarantee enforceability", "Copy a clause from an unrelated file", "Tell the client the clause is unnecessary", "A"),
                    Q("Why should transaction documents be organized and retained?", "To support accurate records, accountability, and later reference", "To make signed documents optional", "To avoid communicating with clients", "To let a party revise a contract after signing", "A"),
                    Q("How should an escrow holder treat client funds?", "Handle them according to applicable trust-account rules and document receipt and disbursement", "Combine them with personal funds", "Use them for operating expenses until closing", "Disburse them based on an oral request alone", "A")
                ],
                [
                    Q("A buyer receives wiring instructions by email that differ from previously verified instructions. What should happen before sending funds?", "Independently verify the instructions using a trusted, previously known phone number", "Reply to the email and ask if it is correct", "Send a small test wire to the new account", "Forward the message to all parties", "A"),
                    Q("Which practice best protects sensitive transaction information?", "Limit access to people who need it and use secure, approved systems", "Upload it to a public shared folder", "Send passwords in the same email as protected files", "Keep unneeded copies indefinitely", "A"),
                    Q("What should a licensee do before using an electronic signature process?", "Confirm the method and records satisfy applicable law and transaction requirements", "Assume any typed name is sufficient in every situation", "Delete the audit trail after signing", "Let one party sign for another without authorization", "A"),
                    Q("What is a sound response to a request to change a signed agreement?", "Document amendments in the required form with the parties' authorization", "Edit the signed file without notice", "Replace the original and delete the history", "Change only the copy held by one party", "A"),
                    Q("What is a key safeguard for cloud-stored transaction files?", "Use access controls, secure authentication, and appropriate retention practices", "Share one password with every vendor", "Store files in personal public links", "Disable backups and audit logs", "A")
                ]),
            ["Ethics at Work"] = new(
                [
                    Q("What does Article 1 of the NAR Code of Ethics emphasize?", "Protecting and promoting the client's interests while treating all parties honestly", "Guaranteeing every buyer a successful offer", "Replacing state licensing laws", "Keeping every transaction secret from regulators", "A"),
                    Q("A buyer shares the maximum price they can pay. How should the agent handle it?", "Keep it confidential unless disclosure is authorized or required", "Tell the seller to strengthen the offer", "Share it with competing buyers", "Put it in public marketing", "A"),
                    Q("What is the best response to a material fact that could affect a buyer's decision?", "Disclose it as required and avoid misleading statements", "Wait until after closing", "Mention it only if asked using the exact legal term", "Ask the seller to hide it", "A"),
                    Q("Why does the Code of Ethics matter to REALTORS®?", "It establishes professional duties associated with membership", "It replaces all federal and state law", "It guarantees a favorable arbitration award", "It applies only to property managers", "A"),
                    Q("What distinguishes mediation from arbitration?", "Mediation helps parties seek their own agreement; arbitration results in a decision by a neutral", "Mediation is always binding and arbitration is never binding", "Both processes are identical", "Arbitration is an informal listing presentation", "A")
                ]),
            ["FL 30-Hour Real Estate Brokerage"] = new(
                [
                    Q("What is a broker's central responsibility when supervising associated licensees?", "Provide required oversight, policies, and review of brokerage activity", "Delegate all legal responsibility to the licensee", "Approve only marketing slogans", "Avoid keeping transaction records", "A"),
                    Q("What is a key risk of implied agency?", "The parties may believe representation exists without clearly documented scope and duties", "It automatically creates a written disclosure", "It eliminates the need for supervision", "It guarantees a transaction will close", "A"),
                    Q("What does a SWOT analysis evaluate?", "Strengths, weaknesses, opportunities, and threats", "Sales, wages, ownership, and taxes", "Signatures, witnesses, options, and title", "Strategy, workflow, operations, and timing", "A"),
                    Q("What does a brokerage cash-flow statement help management assess?", "Cash received and paid over a period and the firm's liquidity", "The exact market value of each listing", "Whether a contract is enforceable", "The number of ethics complaints", "A"),
                    Q("Which practice supports effective development of new brokerage personnel?", "Provide a planned orientation, coaching, and ongoing feedback", "Rely on informal observation only", "Withhold procedures until a mistake occurs", "Skip supervision after onboarding", "A")
                ],
                [
                    Q("What is the purpose of a brokerage business plan?", "To connect goals with strategies, resources, and measures for implementation", "To replace the firm's accounting records", "To guarantee a certain market share", "To serve only as a recruiting brochure", "A"),
                    Q("How should a broker respond to a recurring compliance risk?", "Identify its cause, set controls and training, and monitor whether they work", "Ignore it until a lawsuit occurs", "Delete records of prior incidents", "Assign it to a new licensee without guidance", "A"),
                    Q("What does a marketing plan define?", "Strategies and tactics for reaching the firm's goals and target audience", "The legal ownership of every property", "The firm's tax return", "The terms of all buyer agreements", "A"),
                    Q("Why are accurate brokerage records important?", "They document transactions, support oversight, and help demonstrate compliance", "They make supervision unnecessary", "They permit changing signed agreements", "They replace required disclosures", "A"),
                    Q("Which organizational choice should guide a brokerage's business structure?", "The firm's ownership, liability, tax, and operational needs with qualified advice", "The current logo color", "A competitor's choice without review", "The highest agent headcount only", "A")
                ]),
            ["Florida Core Law"] = new(
                [
                    Q("What is the purpose of reviewing Florida real estate law and updates?", "To keep practice aligned with current statutory and regulatory requirements", "To replace required license education", "To guarantee a transaction's outcome", "To avoid documenting client instructions", "A"),
                    Q("What is FREC's role in Florida real estate?", "It adopts rules and participates in regulating real estate licensees under state law", "It insures every transaction", "It represents buyers in contract disputes", "It sets local property tax assessments", "A"),
                    Q("What should a licensee do when a client relationship creates a conflict of interest?", "Make required disclosures and follow applicable duties and rules", "Hide the conflict until closing", "Share confidential information with the other party", "Assume consent without disclosure", "A"),
                    Q("Why is timely disclosure of a known material fact important?", "It allows parties to make informed decisions and supports legal compliance", "It guarantees the property has no other defects", "It transfers all inspection duties to the agent", "It replaces written transaction terms", "A"),
                    Q("When a Florida rule or statutory requirement is unclear, what is the safest professional response?", "Check current authoritative guidance or consult qualified compliance counsel before acting", "Rely on an outdated form", "Guess based on another state's rules", "Proceed and wait for a complaint", "A")
                ]),
            ["Florida Post-Licensing: Real Estate Finance"] = new(
                [
                    Q("What does loan-to-value (LTV) compare?", "The loan amount with the property's value used by the lender", "The borrower's income with monthly expenses", "The interest rate with the APR", "The purchase price with annual taxes", "A"),
                    Q("How does a fixed-rate mortgage generally differ from an adjustable-rate mortgage?", "A fixed-rate loan keeps its rate under the contract terms; an ARM rate may change under its index and adjustment terms", "An ARM has no principal repayment", "A fixed rate changes every month", "There is no difference after closing", "A"),
                    Q("What is a lender evaluating during underwriting?", "Whether the borrower and collateral meet the loan program's risk and eligibility standards", "Whether the home will appreciate", "Whether the seller will accept a counteroffer", "Whether the buyer qualifies for a real estate license", "A"),
                    Q("What is a primary mortgage market transaction?", "A lender makes a mortgage loan to a borrower", "Investors trade existing mortgage-backed securities", "A county records a deed", "An appraiser selects comparable sales", "A"),
                    Q("Why must a mortgage professional take consumer-protection rules seriously?", "They govern disclosures, fair treatment, and conduct in the credit process", "They eliminate the need to verify loan terms", "They guarantee approval for qualified applicants", "They permit steering borrowers by protected status", "A")
                ],
                [
                    Q("What does amortization describe?", "Scheduled repayment of principal and interest over the loan term", "A property's annual appreciation", "A fee charged only at closing", "A lender's sale of a loan to an investor", "A"),
                    Q("What is the general purpose of FHA or VA mortgage programs?", "Provide government-backed or insured pathways subject to program-specific eligibility and terms", "Guarantee that every applicant is approved", "Eliminate all closing costs", "Set local property tax rates", "A"),
                    Q("What is a debt-to-income ratio used to assess?", "The borrower's recurring debt obligations relative to income", "The home's condition compared with its price", "The lender's servicing costs", "The down payment relative to closing costs", "A"),
                    Q("What role does the secondary mortgage market serve?", "It allows existing mortgage loans or related securities to be sold or funded beyond the original lender", "It originates every loan directly to consumers", "It sets property boundaries", "It replaces consumer disclosure requirements", "A"),
                    Q("What should a professional do if a borrower receives suspicious instructions to send closing funds?", "Pause and independently verify using trusted contact information", "Send funds immediately to avoid delay", "Use the phone number in the suspicious message", "Forward account information publicly", "A")
                ]),
            ["Preparing a Market Analysis - Best Practices"] = new(
                [
                    Q("What is the purpose of a comparative market analysis (CMA)?", "Estimate a supportable price range from relevant property and market evidence", "Guarantee the final sale price", "Replace an appraisal in every transaction", "Set a price based only on the seller's preference", "A"),
                    Q("Which comparable is generally the strongest starting point?", "A recent sale similar in location, property type, size, and condition", "The highest-priced listing in a distant neighborhood", "A property with a different use and no adjustments", "A withdrawn listing selected to support a target price", "A"),
                    Q("What can active competing listings show a seller?", "The alternatives buyers may currently consider", "The exact price buyers will pay", "The appraised value of the subject", "The property's tax basis", "A"),
                    Q("A comparable has an upgrade the subject lacks. How should the difference generally be considered?", "Adjust the comparable to account for the feature's contributory value", "Add the upgrade value to the comparable without analysis", "Ignore the feature in all cases", "Replace its sale price with its asking price", "A"),
                    Q("What may repeated expired listings in a segment indicate?", "Some properties may have been priced or marketed beyond what buyers accepted", "Every expired property had a title defect", "Demand is guaranteed to rise", "The subject's value is exactly the average list price", "A")
                ]),
            ["Residential Property Management Essentials"] = new(
                [
                    Q("What is an important first step when taking on a property-management assignment?", "Define the owner's authorization, duties, fees, and reporting in a written agreement", "Collect rent before confirming authority", "Promise a specific return regardless of conditions", "Delegate all decisions to the tenant", "A"),
                    Q("What is a sound tenant-screening practice?", "Use consistent, lawful, documented criteria for every applicant", "Change criteria based on an applicant's protected characteristics", "Ask neighbors to choose tenants", "Skip required notices", "A"),
                    Q("What should a property manager do after receiving a maintenance request?", "Document it, assess urgency, and coordinate an appropriate response", "Ignore it unless rent is late", "Make no record if a vendor is contacted", "Wait until lease renewal", "A"),
                    Q("Why should a property manager document the condition of a unit at move-in and move-out?", "To create a clear record relevant to maintenance, damage, and deposit accounting", "To guarantee there will never be a dispute", "To replace the lease", "To allow deductions without explanation", "A"),
                    Q("How should a property manager handle a reasonable accommodation request?", "Review it promptly and individually under applicable fair-housing requirements", "Automatically deny it if the lease is silent", "Share the request with other tenants", "Apply a blanket surcharge before review", "A")
                ],
                [
                    Q("A landlord wants to reject an applicant because of a protected characteristic. What should the property manager do?", "Apply fair-housing law and the same lawful criteria to all applicants", "Follow the instruction without documenting it", "Ask another applicant to decide", "Use a different application form for that person", "A"),
                    Q("Why should rent, deposit, and other tenant funds be tracked carefully?", "Accurate accounting supports compliance, transparency, and timely reconciliation", "It allows funds to be used as operating cash", "It removes the need for receipts", "It transfers the owner's obligations to tenants", "A"),
                    Q("What is an appropriate approach to lease enforcement?", "Apply the lease consistently and follow required notices and legal procedures", "Use self-help measures without checking the law", "Change terms verbally after signing", "Enforce rules only against selected tenants", "A"),
                    Q("How should a manager respond to an emergency maintenance issue?", "Take prompt steps to protect people and property and document actions taken", "Wait for written approval even if immediate action is needed to prevent harm", "Ask the tenant to repair it without guidance", "Close the request without inspection", "A"),
                    Q("What best supports tenant retention?", "Responsive communication, consistent service, and fair application of policies", "Changing lease terms without notice", "Ignoring recurring maintenance issues", "Making exceptions based on personal preference", "A")
                ])
        };

    public static async Task SeedAsync(ApplicationDbContext dbContext)
    {
        var courses = await dbContext.Courses
            .Include(course => course.Units)
            .OrderBy(course => course.Title)
            .ToListAsync();

        foreach (var course in courses)
        {
            if (!QuestionSets.TryGetValue(course.Title, out var questionSet))
            {
                continue;
            }

            var units = course.Units
                .Where(unit => !unit.Title.Contains("Course Introduction", StringComparison.OrdinalIgnoreCase))
                .OrderBy(unit => unit.OrderIndex)
                .ToList();
            if (units.Count < 2)
            {
                throw new InvalidDataException($"Course '{course.Title}' needs at least two instructional units before exams can be seeded.");
            }

            var finalExam = await dbContext.CourseExams
                .Include(exam => exam.Questions)
                .Where(exam => exam.CourseId == course.Id
                    && exam.Kind == CourseExamKinds.Standard
                    && exam.UnitId == null
                    && exam.IsRequired)
                .OrderBy(exam => exam.Title)
                .FirstOrDefaultAsync();
            if (finalExam is null || finalExam.Questions.Count == 0)
            {
                throw new InvalidDataException($"Course '{course.Title}' needs a configured Final Exam before supplemental exams can be seeded.");
            }

            var existingSupplementalExams = await dbContext.CourseExams
                .Where(exam => exam.CourseId == course.Id && exam.Kind != CourseExamKinds.Standard)
                .ToListAsync();

            if (!existingSupplementalExams.Any(exam => exam.Kind == CourseExamKinds.Practice))
            {
                var practiceExam = new CourseExam
                {
                    CourseId = course.Id,
                    Kind = CourseExamKinds.Practice,
                    Title = "Practice Exam",
                    PassPercent = finalExam.PassPercent,
                    IsRequired = false
                };
                foreach (var question in finalExam.Questions.OrderBy(question => question.OrderIndex))
                {
                    practiceExam.Questions.Add(CopyQuestion(question));
                }

                dbContext.CourseExams.Add(practiceExam);
            }

            var intermediateCount = course.DurationHours >= 30 || units.Count >= 5 ? 2 : 1;
            if (questionSet.SecondIntermediateQuestions is null)
            {
                intermediateCount = 1;
            }

            for (var index = 0; index < intermediateCount; index++)
            {
                var unitIndex = Math.Clamp((index + 1) * units.Count / (intermediateCount + 1), 0, units.Count - 1);
                var unit = units[unitIndex];
                if (existingSupplementalExams.Any(exam =>
                    exam.Kind == CourseExamKinds.Intermediate && exam.UnitId == unit.Id))
                {
                    continue;
                }

                var questions = index == 1
                    ? questionSet.SecondIntermediateQuestions ?? questionSet.IntermediateQuestions
                    : questionSet.IntermediateQuestions;
                var examOrder = existingSupplementalExams.Count(exam => exam.Kind == CourseExamKinds.Intermediate) + 1;
                var intermediateExam = new CourseExam
                {
                    CourseId = course.Id,
                    UnitId = unit.Id,
                    Kind = CourseExamKinds.Intermediate,
                    OrderIndex = examOrder,
                    Title = $"Intermediate Exam {examOrder}",
                    PassPercent = finalExam.PassPercent,
                    IsRequired = true,
                    MaxRetakesPerLearner = finalExam.MaxRetakesPerLearner,
                    RetakeCooldownMinutes = finalExam.RetakeCooldownMinutes
                };

                foreach (var question in questions.Select((item, questionIndex) => new ExamQuestion
                {
                    Prompt = item.Prompt,
                    OptionA = item.OptionA,
                    OptionB = item.OptionB,
                    OptionC = item.OptionC,
                    OptionD = item.OptionD,
                    CorrectOption = item.CorrectOption,
                    OrderIndex = questionIndex + 1
                }))
                {
                    intermediateExam.Questions.Add(question);
                }

                dbContext.CourseExams.Add(intermediateExam);
                existingSupplementalExams.Add(intermediateExam);
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private static ExamQuestion CopyQuestion(ExamQuestion question) => new()
    {
        Prompt = question.Prompt,
        OptionA = question.OptionA,
        OptionB = question.OptionB,
        OptionC = question.OptionC,
        OptionD = question.OptionD,
        CorrectOption = question.CorrectOption,
        FeedbackText = question.FeedbackText,
        OrderIndex = question.OrderIndex
    };

    private static IntermediateQuestion Q(
        string prompt,
        string optionA,
        string optionB,
        string optionC,
        string optionD,
        string correctOption) =>
        new(prompt, optionA, optionB, optionC, optionD, correctOption);

    private sealed record CourseExamQuestionSet(
        IReadOnlyList<IntermediateQuestion> IntermediateQuestions,
        IReadOnlyList<IntermediateQuestion>? SecondIntermediateQuestions = null);

    private sealed record IntermediateQuestion(
        string Prompt,
        string OptionA,
        string OptionB,
        string OptionC,
        string OptionD,
        string CorrectOption);
}
