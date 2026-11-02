# Classroom reliability and public-search protection

## Priority and scope

### Final-exam certificate paths - 2026-10-26

The signed-in synthetic certificate page uses a download-first presentation:
**Download certificate (PDF)** is the primary action. Email-copy controls and
send-status copy are omitted from the certificate page; the test email endpoint
remains available for delivery testing. Verification/code steps retain their authentication-specific headings.
The heading is **Your certificate**, with class title, student, state and hours
read from the same certificate payload as the PDF. Redundant readiness/sample
explanations were removed from this page; the PDF's non-credit watermark and
protected download remain unchanged.
MailSlurp receipt was verified for a fresh sample certificate email on October 26;
its authenticated download returned a valid 45,511-byte PDF with `no-store`,
while anonymous access redirected to sign-in. This tests the separate synthetic
email journey, not automatic delivery from final-exam certificate issuance.

The passed final-exam result now offers **Review Certificate Details** immediately,
while retaining score, elapsed time and answer review. Passing alone does not
override course completion or applicable attendance requirements.

Pre-licensing requires explicit printed-name confirmation but no commission
record/license number. Continuing education and post-licensing require an owned,
student-confirmed, unrestricted same-state roster selection and an active Learner
with verified email. The server derives state/license, rejects another student's
selection, and prevents changes to an already-issued certificate. Missing records
block submission and offer the existing association lookup, without extending its
search restrictions to any other workflow. Students resume through My Courses.

Identity snapshots and certificate reconciliation share one transaction. New
categorized courses cannot silently issue from account-profile names/licenses.
Historical issued certificates retain their identity policy; unclassified courses
retain legacy behavior. Sample courses remain ineligible for official certificates.
Certificate processing/reporting review is unchanged.
Validation: 57 focused enrollment/roster/certificate service tests passed, web
build passed, and an isolated browser regression exercised all three paths
(pre-licensing, licensee without a record, licensee with a confirmed record).
It verifies persisted enrollment/certificate snapshots and reload behavior,
with no horizontal overflow at 1280, 768 or 320 pixels. The browser test also
caught and now covers input-before-interactivity loss and snapshot persistence
when exam initialization clears EF tracking during concurrency recovery.
Integrated-browser inspection confirmed the current Florida demo record and
compact certificate-page heading; no actual learner exam outcomes were altered.

### User-approved search scope - 2026-10-26

The search limitations and CAPTCHA policy are authorized for **students
registering through Create Your Licensee Account and existing students
searching to associate a license record with their signed-in account**.
The user explicitly approved the existing-account association case on
2026-10-26. They do not apply to Guest account registration or constitute a
general application-wide search policy.

Do not extend these restrictions to administrator/instructor/broker searches, regulatory-directory searches,
course searches or any other workflow without the user's explicit request.
Do not reuse the registration search guard or CAPTCHA session in another
workflow by default. Classroom reliability safeguards are separate from
these licensee-signup/association search restrictions.

The registration component and guarded search service already serve both
approved cases; no separation or exemption for signed-in association is needed.
Sharing that component or service with another workflow does not authorize
extending the restrictions beyond these two cases.

Signed-in students can reach the association search through **Account ->
License Records**. The link is shown for Learner accounts only while the
Development license-record/email feature is enabled, matching the destination's
availability. It does not change submission eligibility or administrator review.

Account now displays saved license selections in a License Profile section,
including available credential dates, education fields, firm affiliations,
source freshness and association review status. Confirmation saves the existing
student-confirmed association and redirects to Account; simply finding/selecting
a result does not save it. Details are read from the request's referenced roster
record, not copied over the user's name, email, phone or verified license fields.
Missing fields remain omitted and affiliation contact emails remain excluded.
The current confirmation requirements are described below.
Update 2026-10-26: existing-account association no longer requires the configured
pilot inbox. Active signed-in students with verified email can use **Confirm** to
save a student-confirmed record and return to Account. New-account registration
and email delivery retain their existing allowlist; record validation and
duplicate protection remain enforced. Earlier demo ineligibility notes describe the prior
behavior, not this updated association rule.
The user clarified that email verification must remain mandatory before an
existing student confirms/saves a selection. The temporary allowance for
unverified submissions was removed. Routine administrator approval was then
removed at the user's request: the student confirms their own commission record.
Only the current demo student's email was manually marked verified at the
user's explicit request, with an audit entry; this was not an email-link
verification or a general bypass.
Account now displays **Confirmed by student**, not verified identity/ownership.
New signup selections wait only for email verification; verification atomically
confirms those selections without an administrator. Existing Pending/Approved
selections for active, verified students are transitioned by the
`ConfirmStudentRosterAssociations` data migration. Rejected and unverified
selections are not promoted; historical review fields remain intact. This
data-policy change cannot be automatically reversed into old admin decisions.
The regression tests check that unverified existing students save nothing,
verified submissions confirm immediately, signup verification confirms selections,
and the migration preserves rejected/unverified selections.
Validation after this policy change: 30 roster/email-journey tests passed; web
build passed (seven existing warnings in unrelated pages). Startup applied the
policy migration. A separate database read and the integrated browser confirmed
that both existing BK698763 demo selections are now persisted as Confirmed and
shown as **Confirmed by student**, with all 13 available details retained.
This is a saved association to an imported roster snapshot, not a fresh regulator
lookup or proof of license ownership. New signups' pending requests also appear
after they verify and sign in.

Account presentation now includes a compact identity/email-verification summary,
an actionable "Not added" state with Find / Confirm / Save steps, and separate
password and email-preference cards. Search results, pre-confirmation and saved
profiles share the same license view: identity and license number, key credential
facts, credential details, brokerage/affiliations and source freshness. Available
details remain visible rather than being removed or hidden behind an expander.
Florida's shared record view links to the official DBPR licensee search interface
(`https://www.myfloridalicense.com/wl11.asp?mode=0&SID=`), where students can search
by name, license number, city/county or license type. The captured Qlik single-object
source URL failed with "Single url not valid" and is not used as the student lookup.
Stored import provenance remains unchanged; other states retain their source links.
User requirement for each state's rollout: provide a verified, working official
commission/regulator lookup interface where students can filter and search, not
an incomplete embedded report, import download or assumed individual-record link.
Keep import provenance separate from the student-facing lookup destination.
Florida is implemented; other states must be checked and wired as they are reached.
The pre-confirmation screen explicitly says "Not confirmed yet"; saved selections
separate student confirmation, email verification required and rejected associations
from the regulator's license status. Admins handle corrections/disputes through
audited rejection, not routine approval.
For the presentation demo, a separate synthetic student was created through the
normal registration form and its email manually marked verified with an audit
entry, as authorized. No general verification bypass was added.
The earlier integrated-browser demo (before removing routine approval) exercised the empty Account state, search result,
unconfirmed selection, Confirm redirect and saved Pending Review profile after
a full reload. A separate database read confirmed one pending selection and no
ownership links for that demo student. All 13 available detail values remained
visible. Account and confirmation views were checked at 1280, 768 and 320px
without horizontal overflow; the desktop Account title lettering aligns with
the sidebar title within one pixel. Email-preference save/revert was exercised
by keyboard. The 21 roster service tests and web build passed.

Signed-in selection now navigates to a dedicated **Confirm Your License Record**
step instead of revealing an inline panel below the results. The step revalidates
the selected record, shows its available details and either offers confirmation
or explicitly reports account ineligibility. Back navigation retains the
completed search. Selecting alone still never saves a license association.

Integrated-browser demonstration on 2026-10-26: signed in using the existing
demo learner, opened Licensee Records, searched and selected a Florida record
without creating a new account or submitting an association. Searches 1-3
returned the record; search 4 was blocked because real Turnstile keys remain
unconfigured. The demo account is not the pilot's eligible configured recipient,
so its selection correctly displayed the existing submission-eligibility
restriction. No eligibility bypass, account creation or license link was made.

The priority is many students reliably taking classes, not maximizing public
license-search throughput. Search is a separate, lower-priority workload.
There is no application-wide semaphore limiting the number of students in class.
These changes are correctness and availability safeguards, not a certification
of any particular concurrent-student capacity.

The roster-registration/email pilot still has its existing environment and
new-account/email-recipient restrictions. Cleaner learner-facing wording does not
enable production registration or establish license ownership.

## Classroom safeguards implemented

### Isolated and observed attendance saves

- Attendance startup, each heartbeat and session closure use their own DI
  scope and database context. The heartbeat no longer overlaps UI operations on
  the circuit's shared EF context.
- A single awaited `PeriodicTimer` loop replaces unobserved, fire-and-forget
  database callbacks. A slow heartbeat cannot start another heartbeat in that
  loop.
- Component disposal cancels and awaits the loop before attempting closure.
  Closure errors are logged rather than becoming unobserved task failures.
- Startup/heartbeat failures are logged and shown as an attendance-save error,
  not silently treated as successful attendance.
- Start, pulse and end perform read/modify/write within database transactions.
  Previously tracked sessions are reloaded under that transaction.
  Concurrent starts reuse one active user/course session; concurrent pulses do
  not credit the same elapsed interval twice.
- Existing policy is unchanged: one pulse credits at most two minutes, elapsed
  time is floored to whole minutes, and inactivity is not unlimited credit.
  This does not establish learner presence or replace regulatory attendance
  controls.

### Durable lesson progress

- Lesson-progress writes use a fresh operation scope from the lesson page.
  The completion row, enrollment aggregate, resulting certificate-state work
  and audit writes are committed together. Errors roll back the transaction.
- Existing enrollment/progress entities are reloaded before modification.
  Recalculation reads persisted completed lessons for the current course.
- Manual progress updates and completion reconciliation also use transactions.
- If a lesson save fails, the page logs and displays the failure, does not
  mark the lesson complete locally, and does not advance through that action.
  Duplicate in-flight saves from that page do not advance it.
- Certificate requirements, identity checks, required-exam rules and attendance
  requirements remain in force.

### Exam consistency

- Final/supplemental exam submission uses a fresh operation scope.
- Scoring, attempt-count/number decisions, cooldown and retake checks, proctor
  consumption, answers and audit writes execute within one transaction.
- A repeated submission with the same supplied start time for the same user,
  exam and content version is explicitly rejected. This protects the current
  lesson-page submission path, not an arbitrary client-generated idempotency
  key: callers omitting the start time still rely on attempt/cooldown policy.
- Unit-exam pass-row creation/update is transactional, retaining its existing
  unique user/course/key constraint.
- The page disables repeat submissions while an exam submission is in flight,
  observes failures and displays an error instead of disconnecting the circuit
  on an unhandled submission exception.
- Exam persistence and the subsequent completion reconciliation are separate
  transactions. If reconciliation fails after submission, the attempt remains
  durable. Refresh exam history before retrying; do not blindly submit again.
- Practice exams retain their existing unlimited-attempt/no-cooldown policy;
  replaying the exact same supplied exam session is still rejected.

### Database-specific guarantee

The current provider is SQLite. Its normal non-deferred write transaction takes
the writer reservation before these read/modify/write decisions. Independent
contexts therefore cannot both decide against the same old writable state.
This uses the database's existing single-writer serialization; it does not
create a separate global classroom admission limit.

Busy/lock waits and failures remain possible. These are not infinite retries or
a promise that every concurrent write will succeed. Do not reuse a context
after a failed transaction without resolving its tracked state; lesson-page
operation scopes are disposed after the operation.

If switching providers, explicitly revalidate transaction isolation, write
conflicts and uniqueness with the same independent-connection tests. Do not
assume SQLite's behavior transfers unchanged to SQL Server or PostgreSQL.

## Public lookup safeguards implemented

The singleton `RosterSearchProtection` is enforced in the application service,
including calls from Blazor events, not only HTTP endpoints.

Default admission policy, per application process:

| Protection | Default |
|---|---|
| Concurrent roster search/discovery operations | 2 |
| Waiting queue | None: reject immediately |
| Operations per service scope/circuit | 8 per fixed 60-second window |
| Operations across all scopes | 60 per fixed 60-second window |
| Search cooperative cancellation budget | 5 seconds |
| Slow-operation warning | 2 seconds |
| Results page | 20 parent records |

State-list discovery and filter-option discovery share admission with searches.
Internal filter validation does not acquire another admission slot.
The per-scope window is not a verified per-person or per-IP identity. A new
circuit resets that scope's window but does not reset the process-wide budget.
The weak-key scope bookkeeping does not retain disconnected circuits forever.
These conservative defaults protect classroom resources; they are not a
registration-throughput promise. Change them only after measured testing.

Rejected operations show a normal retry message. Search completion times,
slow operations and rejections are logged without names, license numbers,
email addresses or search terms in the protection log events.

The page suppresses overlapping search-button actions. The last completed
search is reused on return from account creation. A refreshed URL is searched
during prerender and the bounded result set is transferred through protected
server-interactive component state, avoiding the same search again at hydration.
Record selection is still revalidated independently before account creation.

### CAPTCHA after three searches

#### Progress note - 2026-10-26

- User-approved policy implemented: three CAPTCHA-free searches per connection,
  then a fresh server-verified Cloudflare Turnstile check for every search.
- Verified with 37 passing focused tests, a successful web build and browser
  checks, including account-page return and mobile layout.
- Temporary public test keys were removed after validation. No real Turnstile
  keys have been configured; search 4 onward is currently blocked.
- **Next action:** create/configure the real Cloudflare widget, store its keys
  securely, allowlist the intended hostnames and verify the real-key flow.
  Never leave dummy keys enabled as production protection.
- Existing search throttling stays enabled. Classroom operations are unchanged.
- Connection scope is a Blazor circuit; a full reload/new circuit can receive
  another free allowance. CAPTCHA is an additional layer, not complete bot or
  denial-of-service protection.

Cloudflare Turnstile is integrated into license-record search only:

- Searches 1, 2 and 3 in the Blazor circuit do not require CAPTCHA.
- Search 4 and **every** later search require a fresh, single-use token,
  verified server-side before any roster search query runs. Passing a check
  does not grant a CAPTCHA-free time window or reset the counter.
- Search submissions and pagination queries count. Typing, state/filter-option
  discovery, selecting a result and returning to cached results do not.
  Malformed input rejected before search admission does not use a free search.
- The counter is scoped to the server circuit, not component UI state. Clear,
  filter changes and in-circuit navigation do not reset it. Prerender state
  transfers the count alongside cached results without counting hydration twice.
  Reconnecting the same retained circuit keeps the count. A new circuit,
  including a full document reload, can obtain a new allowance; this is not a
  durable per-person quota. Existing process-wide limits remain essential.
- Turnstile Siteverify must return success, the `roster-search` action and an
  explicitly allowed hostname. Missing, forged, expired or replayed tokens
  cannot authorize a protected search. The widget discards its token after
  each submission. Verification failures do not reset or increase the counter.
- Missing configuration or a provider failure blocks protected searches with
  a normal unavailable/retry message. There is no fail-open fallback.
- The server sends the token and configured secret to Cloudflare, not the
  license/name search or account details. Do not log tokens or secrets.
- Existing workload/rate limits still apply, including during verification.
  No CAPTCHA is introduced into lessons, attendance or exam submission.

Configure these keys using .NET user-secrets for local development or secret
settings in the deployment:

| Setting | Purpose |
|---|---|
| `Turnstile:SiteKey` | Public widget site key |
| `Turnstile:SecretKey` | Private Siteverify key; never commit it |
| `Turnstile:AllowedHostnames:0` (and subsequent indices) | Exact hostname without scheme, path or port |

Create the widget in Cloudflare and restrict it to the actual site hostnames.
Use `localhost` for a separately configured local widget; use only production
hostnames for production. Allow outbound HTTPS to
`challenges.cloudflare.com`; any deployment CSP must also allow Turnstile's
script and frame resources. Consider Cloudflare's privacy/accessibility terms
in the site's privacy notice.

Cloudflare's public dummy keys may be used **temporarily in Development** with
`Turnstile:UseTestKeys=true`. They do not protect against bots. That explicit
mode requires a testing-key response marker instead of the production action;
the hostname must still match the configured allowlist. Startup rejects test
mode and known dummy keys outside Development. Do not leave test keys enabled
in the deployed application. Real keys are not supplied by this implementation;
without configuration only the first three searches are available.

Reference: [Cloudflare server-side validation](https://developers.cloudflare.com/turnstile/get-started/server-side-validation/).

### Important deadline limitation

The five-second cancellation token is cooperative. Microsoft.Data.Sqlite
executes SQLite work synchronously and does not provide a reliable wall-clock
CPU interruption guarantee through this token. The admission slot remains held
until the actual query exits; it is not released by abandoning an executing
query in the background. A slow query can therefore exceed five seconds.

This is **not** a hard query timeout or complete denial-of-service protection.
Name/firm substring filtering and full result counts still need execution-plan
measurement against representative data. Before exposing this at scale,
implement and validate an indexed search strategy or isolate search behind a
separately resource-limited worker/database with an enforceable execution
deadline. The current application does not configure that worker.

## Verification performed

The focused application suite covers enrollment, exams, roster behavior and
search admission. New synthetic tests use temporary file-backed SQLite
databases and separate contexts/connections, with concurrent operations running
on separate tasks. They do not stress the real roster or student database.

New assertions include:

- Eight synthetic students completing the same course concurrently retain
  independent progress; another student's untouched enrollment stays at zero.
- Concurrent completion of different required lessons retains both rows and
  produces exactly 100% enrollment progress.
- Concurrent attendance starts produce one active session; four simultaneous
  pulses credit a 65-second interval only once.
- A pulse after closure explicitly fails rather than claiming a save.
- Concurrent replay submissions create exactly one attempt/answer set.
- Distinct simultaneous exam sessions cannot exceed a one-attempt policy.
- Concurrent unit-exam passes retain one passed progress row.
- An injected audit-storage failure rolls back lesson and aggregate progress.
- A third concurrent lookup is rejected immediately; disposing a lease twice
  cannot incorrectly release an extra slot.
- The ninth operation in a scope is rejected, the window resets at 60 seconds,
  and new scopes cannot bypass the 60-operation global budget.

These small deterministic tests are correctness checks, **not a load test**.
The classroom/search baseline passed **74 tests, zero failures**. The subsequent
CAPTCHA-focused suite passed **37 tests, zero failures**; the web project built
successfully. Existing unrelated compiler/analyzer warnings remain.
The restarted local application returned HTTP 200. A browser check confirmed
that selecting a record opens account creation and returning retains the
license field, result and antiforgery form token. Server logs confirmed that
the initial search result was reused at interactive hydration.
Turnstile browser checks used public dummy keys temporarily: no widget on
searches 1-2, the widget appeared after search 3, verified search 4 succeeded,
and a later search without a token was blocked. Another verified search and
account-page return retained the CAPTCHA requirement. The responsive widget
fit a 320px viewport. Dummy keys were then removed and the app restarted;
searches beyond three were confirmed blocked without configuration. These
checks do not validate a real production widget/domain/key configuration.

Run the focused suite from `src`:

```powershell
dotnet test .\Lms.Application.Tests\Lms.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~EnrollmentServiceTests|FullyQualifiedName~ExamServiceTests|FullyQualifiedName~RosterRegistrationServiceTests|FullyQualifiedName~RosterSearchProtectionTests|FullyQualifiedName~RosterSearchCaptchaTests" -v quiet
dotnet build .\Lms.Web\Lms.Web.csproj --no-restore -v quiet
```

## Remaining production work / release checklist

1. **Hosting-edge abuse controls:** configure WAF/bot and request/connection
   limits before traffic reaches ASP.NET or Blazor circuits. Configure trusted
   proxies explicitly; never trust arbitrary forwarded client-IP headers.
   The existing login HTTP limiter is separate and does not protect all
   SignalR events.
2. **Multiple instances:** counters here are process-local and reset on
   restart. Distributed admission/quotas need shared storage or edge controls.
   Sticky routing, shared data-protection keys and supported SignalR scale-out
   must be designed and tested for the actual hosting arrangement.
3. **Database capacity:** SQLite allows only one writer and is not a
   high-write, multi-instance production capacity guarantee. Benchmark it;
   choose a managed server database if measured write contention warrants it.
   Revalidate isolation before migration. Separate roster search load from
   classroom transactions before a large public rollout.
4. **Circuit lifetime:** the wider application still has scoped services using
   circuit-scoped contexts. The operation isolation above covers the changed
   lesson-page writes/attendance path, not every service in the application.
   Audit other overlapping callbacks before claiming universal isolation.
5. **Multiple tabs:** active attendance sessions are shared by user/course.
   Closing one tab can end that shared session; another tab will receive an
   attendance error and must refresh. Independent tab leases, cross-tab
   presence and offline/disconnection recovery are not implemented here.
6. **Timed assessments:** existing UTC/local-storage timer behavior is
   preserved, not redesigned. Server-owned exam sessions, authoritative
   deadlines and durable answer drafts require separate implementation if
   mandated by the course rules. Client start times are not proof of attendance.
7. **Controlled mixed-workload test:** use an isolated production-like copy,
   synthetic users and representative courses. Mix lesson reads, video/static
   delivery, staggered heartbeats, completion saves, timed exams, reconnects
   and hostile search traffic. Include synchronized exam finishes and
   database/worker failures. Never load-test the real student database.
8. **Measure before advertising capacity:** define peak concurrent students
   and acceptable p95/p99 read/save/submission latency, error/lock rates,
   zero lost/duplicate progress or attempts, recovery time, CPU, memory,
   connection/circuit counts and storage I/O. Agree numeric release thresholds
   for the selected hardware/hosting before testing.
9. **Operations:** collect search rejection/slow-operation logs and attendance,
   progress and exam failure logs. Add alerts and latency/error dashboards,
   backup/restore drills and a documented recovery procedure. Do not log
   credentials, exam answers or unnecessary public-record PII.

No numerical concurrent-student capacity is asserted until this checklist is
validated against the intended production deployment.
