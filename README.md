# LMS Starter for Blazor

This starter project provides a simple foundation for a real estate certification LMS built with Blazor.

## What is included
- A Blazor web app project structure
- A domain model for courses, units, and lessons
- A simple in-memory course service
- Starter pages for home, courses, course detail, admin, instructor, and broker areas

## Project structure
- [src/Lms.Web](src/Lms.Web)
- [src/Lms.Application](src/Lms.Application)
- [src/Lms.Domain](src/Lms.Domain)

## Classroom reliability and search availability

See [ROBUSTNESS.md](ROBUSTNESS.md) for implemented classroom transaction and
attendance safeguards, public-lookup admission limits, synthetic concurrency
checks, and the remaining hosting/load-testing requirements. These safeguards
do not certify a numerical concurrent-student capacity.
License-record search allows three submissions per Blazor circuit without
CAPTCHA; every subsequent search requires a fresh, server-verified Cloudflare
Turnstile check. Configure real keys and exact allowed hostnames as described
in [ROBUSTNESS.md](ROBUSTNESS.md). Without configuration, protected searches
are blocked; no test keys or verification bypass are enabled by default.

## Interface terminology

Use **My Courses** for the student's course area, **Student/Students** for people
in administrative, broker, and instructor screens, **Name** on certificate
verification, and **your account** in customer-facing account instructions.
Internal `Learner` roles, identifiers, URLs, real names, and historical records
are unchanged. Unnamed accounts use **Guest**; earlier exact **Guest Learner** and
**Guest Student** placeholders are normalized to **Guest** at startup.
The demo login is **william.guest@wlr.com** (formerly
**william.learner@wlr.com**); startup renames the existing account in place,
preserving its ID, password, enrollments, certificates, and other associations.
Demo passwords are unchanged. **Guest** is a display name, not anonymous checkout
or a new permission role.

The login page includes a prominent **For Guests** link under the centered **Create an Account** heading. Registration
from a roster is offered separately on login as **For Licensees**
when the development email/roster pilot is enabled, not on Guest registration.
The licensee signup page explicitly leads with **Create Your Licensee Account**,
labels the search **Licensee Records**, and shows **Finish Creating Your Account**
on a separate continuation page after **This is my license record** is selected.
The selected record is revalidated on that page, including on refresh; a back link
returns to license records with the previous state, search fields, filters and
results page restored. Within the same signup session, returning reuses the
already loaded results rather than reloading the document and rerunning the
search. Refresh still reconstructs the search from its URL.
Searching alone does not create an account; the **Create Account**
form retains email verification and separate license-association review.
Every state with an imported, approved licensee roster supports a full license number or a
partial name (at least 3 characters), with optional state-roster license types.
Every state's roster search accepts the full number with or without its letter
prefix (for example, `BK3125137` or `3125137`), based on the stored license number,
not the license-type label.
Partial numbers do not match, and leading zeros remain significant.
The **More filters** section shows a brokerage/firm search only when the latest
approved snapshot contains firm affiliations: the current Florida capture does;
the current North Carolina capture does not. City/county filters are not offered
because individual locations are not imported. All entered filters combine,
inactive records remain discoverable, and results are paginated at 20 per page.
Each result highlights its state and shows labeled license details, available
dates, related credentials, education fields and published firm affiliations.
Missing fields are omitted; firm addresses are labeled as firm addresses, not
the individual's location. Contact email addresses are not exposed in signup.
No records load until an explicit search. Students confirm a candidate using
**This is my license record** before the existing single-record pilot submission;
multi-record submission and the completion-stage linking workflow are not changed.
The state selector lists states with approved imported licensee records, not all
50 states. License-type options and firm-filter availability come from each state's
latest approved snapshot. Roster records are commission-sourced; only account,
email, and certificate testing is synthetic. Coverage and freshness are not
assumed to be complete or identical to a future formally requested roster.
On 2026-10-24, the workspace owner authorized interim approval of all 12
remaining pending imported snapshots, pending a future definition of formal
roster-approval criteria. At that stage the local database exposed licensee rosters for
23 states, including the larger Texas import. Each transition is recorded as
`regulatory-snapshot.approved` with the owner's instruction in the audit details;
this is not student record confirmation or account-link approval. Future imports
retain their existing review defaults and are not automatically approved.

The subsequent canonical-source import pass added current snapshots for 26
jurisdictions (599,646 individual credential records, 50,712 firms and 157,364
published affiliations across that pass). Source-specific samples remain samples.
New Mexico's capture is business-only; Alabama, Wisconsin and DC have no captured
roster. Connecticut's mixed broker-category holders are not classified by name.
Puerto Rico is supported in roster selection and roster-request review without
changing course eligibility or pretending it is a US state. See the
[current import report](src/data/rosters/import-results-20261024-gaps.json)
and [roster storage/import guide](src/data/rosters/README.md) for exclusions,
coverage limits and reproducible commands. Existing snapshots and account links
remain intact.
The captured-gap recovery added 53 firms (52 New Jersey, one Louisiana) without
adding individual records or inferred relationships. Connecticut's mixed broker
sample reconciles seven verified firms but leaves 13 holders unclassified;
six New Jersey multi-alias company credentials still need verified profiles.
Repeat-import safety, unchanged unrelated counts and 34 focused tests passed.
The running selector now has **47 states plus Puerto Rico**; New Mexico remains
absent from individual-license search because its captured roster is business-only.

Roster source storage is organized separately from course/legal references under
[`src/data/rosters`](src/data/rosters/README.md), using
`<state-or-territory>/<capture-date>/raw` and `prepared`, with SHA-256 provenance
manifests. The source catalog drives hash-verified copies; legacy `src/docs`
files remain during importer migration so existing imports/tests keep working.
The [source inventory](src/data/rosters/inventory.csv) records coverage and
remaining import gaps as of 2026-10-24. Organizing files does not import or approve
records, and the live database remains `src/Lms.Web/App_Data/lms.db`.

The login keeps the sidebar, with a soft translucent card, Guest/Licensee icon
tiles above the provider choices, and a show/hide password control. Google and
Microsoft buttons say **Continue with**.
Registration
requires an email, password, and matching confirmation, preserves the requested
local destination, and signs in the new account. The form is antiforgery-protected
and uses the login rate limiter. Existing-email and invalid-input errors return
to the registration form. Registration uses static server rendering, and the
login link loads a fresh document, so antiforgery tokens are present and typed
values are not reset by interactive hydration. Ordinary registration does not mark email or license
ownership verified; the configured synthetic email pilot still requires explicit
email verification. Editing the account name is not yet available.

New and changed passwords require at least **8 characters**, with no uppercase,
lowercase, number, or symbol rules. Spaces and passphrases are allowed; entirely
whitespace passwords are rejected. This policy applies to self-registration,
admin-created accounts, password changes, and resets. Existing password hashes,
sign-in methods, rate limiting, and lockout protection are unchanged.

## Password recovery

**Forgot password?** appears below Demo accounts on login. Recovery uses static,
antiforgery-protected forms and the login rate limiter. Requests show the same
confirmation for unknown, disabled, and provider-only accounts; Google/Microsoft
users recover through their provider. Eligible password accounts receive a
30-minute, single-use reset link. Only a token hash bound to the current password
hash and email is stored. Resending after the one-minute cooldown revokes older
links; changing the password or email also invalidates them. Five invalid token
attempts block that link. Failed delivery revokes the token and is logged/audited
without disclosing account existence on the public page.

Resetting applies the same 8-character password policy, clears password lockout
and forced-change flags, preserves email verification and two-step settings, and
revokes pending email challenges. It does not sign the user in automatically.
Recovery pages use `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.

Set `PasswordRecovery:Enabled=true` and `PasswordRecovery:PublicBaseUrl` to the
trusted public HTTPS origin, with real SMTP delivery (`EMAIL_DELIVERY_MODE=smtp`)
configured. Startup rejects enabled recovery with simulated email delivery or an
unsafe origin. Defaults are disabled; disabled forms explicitly direct users to
an administrator. Development also allows a loopback HTTP origin and the
existing allowlisted MailSlurp transport. Local user-secrets enable recovery at
`http://localhost:5000`; the sandbox sends only to its configured synthetic inbox,
not arbitrary customer addresses. Production SMTP/domain setup is still required.
See `src/Lms.Web/PasswordRecovery.http` for manual requests.
The synthetic MailSlurp inbox was verified end to end: reset email received,
mismatching confirmation rejected, password changed, replay rejected, old password
rejected, and new password plus the existing email-code step signed in as Guest.
No real account password was changed.

## Microsoft federated sign-in

### Status and deferred federation plan (2026-10-24)

Microsoft personal-account federation was verified end-to-end in the integrated
browser: an active Learner and issuer/subject association were persisted without
granting license ownership, enrollments, or credit. Existing password sign-in
remains available.

Initially, additional identity-provider onboarding was deferred because
of payment-card requirements encountered during provider setup. Do not create paid
resources, supply billing information, or start additional provider registrations
without renewed approval. Billing requirements vary by provider and product;
federated sign-in does not universally require a credit card.

When resumed:

1. Google personal-account sign-in is now verified alongside Microsoft; Google
   Workspace sign-in remains to be tested.
2. Consider Apple later; accommodate private relay email addresses.
3. Add organization-specific OpenID Connect or SAML SSO (for example Okta,
   Microsoft Entra ID, or Ping Identity) when a brokerage or school needs it.
4. Keep Facebook optional and lower priority for this professional LMS.
5. Before broader rollout, implement explicit, authenticated account linking and
   recovery. The local pilot keeps provider accounts separate and rejects email
   collisions rather than automatically linking accounts.
6. Define recovery and organization-offboarding policies for learners who lose
   access to an employer-controlled identity.
7. Keep professional-license verification separate from all identity providers.

The user subsequently approved Google setup without billing on 2026-10-24.
Google personal-account sign-in was then verified end-to-end in the integrated
browser. Its active Learner account and Google issuer/subject association persisted
separately from Microsoft. Official certificate, enrollment, exam-attempt, and
regulatory-license-link counts remained unchanged. A forged Google callback was
rejected without issuing an authentication cookie. The web build and 20 focused
federated-account/email-journey tests passed.
Other providers remain deferred. Google and Microsoft remain enabled locally.
The Google app remains in Testing mode, but its basic `openid profile email`
scopes qualify for Google's exception: users need not be on the test-user list.
Workspace administrator policies and local account restrictions can still block
sign-in. Only the configured local callback has been tested.

### Google setup

Google uses the same persistent issuer/subject associations and account-collision
protections as Microsoft. It does not merge accounts by email or grant license
ownership. Configure a Google Auth Platform External app and create a Web application OAuth
client with redirect URI `http://localhost:5000/signin-google`. JavaScript origins
are not needed for this server-side authorization-code flow.

Store `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, and
`Authentication:Google:Enabled=true` in local user-secrets. Keep Microsoft's
configuration unchanged. Restart and use **Sign in with Google** at
`http://localhost:5000/login`. Only `openid profile email` scopes are requested;
tokens are not saved. Production needs HTTPS, registered production callbacks,
and review of Google's publishing/branding requirements. No paid trial or billing
was enabled for the local setup. Never commit downloaded client credentials.
Testing mode normally limits access to listed test users, but Google exempts
Sign in with Google requests using only these basic identity scopes. Adding other
OAuth scopes can remove that exception. See Google's
[audience documentation](https://support.google.com/cloud/answer/15549945).

### Microsoft setup

Register a Web application in Microsoft Entra with **Accounts in any organizational
directory and personal Microsoft accounts**. For local testing, register
`http://localhost:5000/signin-oidc` as its Web redirect URI; leave implicit grants
off. Use the client secret **Value**, not its ID. Store the following in the
canonical web project's .NET user secrets:

- `Authentication:Sso:Enabled`: `true`
- `Authentication:Sso:DisplayName`: `Microsoft`
- `Authentication:Sso:ClientId`: the application ID
- `Authentication:Sso:ClientSecret`: the private client secret value

Open `http://localhost:5000/login` and select **Sign in with Microsoft**. Development
SSO requests from another hostname redirect to that login page to keep correlation
cookies and the registered callback on the same host. The email pilot continues to
use its configured `127.0.0.1` origin; those hosts have separate login cookies.
Production requires HTTPS and an exact HTTPS Web redirect URI on the deployment
host. Configuration is opt-in; password login remains unchanged when enabled or
disabled. Disabling SSO removes its challenge endpoint.

The flow uses authorization code with PKCE, signature/audience/lifetime/issuer,
nonce, and correlation validation. The common Microsoft endpoint accepts work,
school, and personal accounts; the token issuer must exactly match its signed
tenant ID. Provider roles are not trusted for LMS privileges. Tokens are not stored.
Startup applies `AddExternalIdentities`.

A new identity creates a Learner with a persistent issuer/subject association.
Email is contact data, not proof of license ownership or authority to link an
existing account. Existing-email collisions fail closed: sign in with the existing
method instead. There is no automatic account merging or account-linking UI.
Returning identities retain their local name/email/role even if provider claims
change. Disabled, locked, password-reset-required, or email-two-step accounts
cannot bypass their local restrictions through federation. Local password expiry
alone does not expire an external identity's authentication. Provider email claims
do not set the LMS's email-verification timestamp.

No roster identity, ownership approval, enrollment, certificate, or credit is
created by federated sign-in. Microsoft receives the authentication request and
the learner signs in directly there; LMS passwords are not sent to Microsoft.

## State collection statistics

Administrators can open **Admin → State Statistics** (`/admin/regulatory-statistics`)
or follow the link from Regulatory Directory. Select a state and a pending
snapshot to compare its staged records against that state's latest approved
snapshot (retrieved date, imported date, then ID). Comparison refresh is read-only;
it does not approve data, link accounts or collect records. Florida's separate
**Collect now** action downloads official bulk files and stages a complete
PendingReview snapshot. Approved search continues using approved records only.

Comparison changes are derived from matched license identities rather than net
snapshot count subtraction. Reconciliation separates New license, Status changed,
Other updates, Unchanged, and Not observed (never inferred removal). Status changes
include any accompanying detail changes; Other updates excludes status changes.
The paged reconciliation table shows normalized old/new values for changed fields
(50 records per page), with classification counts. Published-status rows separate
new licenses from matched transitions: Entered is blue and Left is red. Exits never
include unobserved licenses, and new license records are not status transitions.
The source-status transition table groups matched changes as approved status →
incoming status, with a license count for each direction.
Florida comparison status labels remain unchanged by default while source
meaning is reviewed. The previously prepared label simplification can be enabled
with `RegulatoryDisplay:SimplifyFloridaStatusLabels`: Current, Active becomes
Active and Current, Inactive becomes Inactive; Current alone becomes Current
(activity unspecified). Stored values, exports and classification are unchanged.
The incoming bulk browser always shows published source values.
Published status labels remain source-specific; formatting changes may require
review and do not establish regulator-confirmed events. New to baseline is not
proof of newly issued licenses. Reports refresh on Refresh statistics, not during
five-second collector polling; incoming snapshots remain pending review.

### Florida: official weekly bulk strategy

The top selection card contains only the state dropdown. Florida's summary
metrics follow the bulk collection controls and license browser, without a
snapshot dropdown, refresh button or review link.
Other states retain their existing comparison controls.
Florida comparisons show the latest synchronized bulk roster against its
previous activated version. Historical collections remain preserved.
The Florida admin panel links only to official bulk sources.
Commission and Appraisal Board download-page links are visible directly below
the collection controls, not hidden inside coverage details.

While a run is active, **Stop collection** cancels the accepted run without
shutting down the LMS. Cancellation rolls back uncommitted staging and retains
the last successful snapshot/archive. Manual collection can start again;
automatic retries are deferred for a full configured refresh interval following
an administrator stop, so the scheduler does not immediately restart it.

The panel displays elapsed time (HH:MM:SS), download completion percentages
(without byte sizes), checked rows/manifest row counts for validation, and staged
record counts with an indeterminate transaction progress bar. Updates poll every
five seconds. Unknown file lengths and overall remaining time are not guessed.
Completed/cancelled/failed runs retain a fixed duration and the last successful
completion timestamp stays distinct from the end of an unsuccessful run.

Both Regulatory Directory and State Statistics show the state bulk panel with
one action button beside the heading: **Collect now** while idle, switching to
**Cancel collection** during an active run. It remains at the same position.
collection state, per-file rows and progress, last completion, next automatic run,
explicit errors and Admin-only original-file/manifest downloads. Status polls
every five seconds without issuing regulator requests. A completed run refreshes
the current roster comparison; no manual approval is required for Florida bulk.

Texas now uses that same coordinator and admin workflow. Select Texas in State
Statistics or in the directory's bulk-roster selector for collection, source
links, full-archive filters, exports and history/recovery. Its official TREC
[statewide CSV](https://data.texas.gov/api/views/s7ft-44qi/rows.csv?accessType=DOWNLOAD)
is linked from the [commission downloads page](https://www.trec.texas.gov/public/high-value-data-sets).
The adapter validates the named 20-column CSV header, exact credential types,
dates, duplicate identities, hashes and the independently queried
[official statewide row count](https://data.texas.gov/resource/s7ft-44qi.json?$select=count(*)).
A changed count between acquisition and verification fails safely rather than
activating an incomplete roster. Count URL, verification time and total are
preserved in the archive manifest.

Broker Individual and Sales Agent rows become people; Broker Company rows become
firms. All published statuses are retained, including expired, suspended, revoked
and unknown values. A synchronized roster is not a declaration that its licenses
are active or eligible. Missing names remain explicitly marked "Name not
published"; no names or missing sponsors are fabricated. Affiliations require
both actual company and person credentials in the same source; individual
sponsors remain related credentials, not invented firms. Original numbers,
suffixes and leading zeros are preserved. Comparison and number search tolerate
source suffix case and the measured trailing space before two sales-agent
suffixes, without allowing partial numeric matches. Instructor, appraisal and
school coverage is not supplied by this Texas dataset. Original source fields
remain available in the CSV and the browser shows county/supervisor/source
relationship details.

`TexasBulk` has independent options, archive/status files, schedules and alert
incidents. Its default LMS refresh interval is seven days and stale threshold
eight days, matching the existing workflow; this is an LMS policy, not a claim
about the regulator's publication cadence. Disable `TexasBulk:WeeklyEnabled`
alongside Florida in isolated tests. Both states share the same in-app/email
retry and recovery semantics below. Texas exports require Admin authorization;
see [TexasBulk.http](src/Lms.Web/TexasBulk.http).
To acquire and activate through the same verified pipeline without opening the UI:
`dotnet run --project src/Lms.Web -- --texas-bulk-collect`.
To reprocess a previously verified archive:
`dotnet run --project src/Lms.Web -- --texas-bulk-import-archive "<directory>"`.
Archived imports retain their original acquisition timestamps and are never
labeled fresh downloads. Development samples are not an approved collection
source. Dataset availability is not consent to outreach or proof of account
ownership; do not infer permitted marketing use.

California uses the same synchronization, collect/cancel, history/restore and
state-isolated alert workflow. Its official DRE sources are the daily
`CurrList.zip` (25-column Windows-1252 CSV, with BOM detection) and binary
`broker_associates_list.xls` (18-column BIFF workbook). Original files, hashes,
row counts, retrieval time, HTTP Last-Modified and ETag are archived. Null
padding observed in Excel license strings is trimmed; eight-digit identities
and leading zeros are preserved. Layout changes, duplicate role/relationship
identities, incomplete ZIPs, mismatched archive hashes, inconsistent expiration
dates and different publication days fail without replacing the current roster.
No independent DRE statewide row-count endpoint is available; displayed source
totals are validated counts of the two downloaded files, not externally certified
counts. Identical source bytes are idempotent and can reactivate after a restore.

California retains separate Broker, Salesperson, Officer and Corporation records,
including multiple officer/corporation relationships under the same license
number. Role totals and distinct license numbers are not counts of unique people.
The source's additional-record, restricted-license and ethics/agency flags are
retained. NBA remains the published status, not an inferred revocation.
Broker-associate-only credentials use **Status not published**, never a guessed
active status. Affiliations require actual matching responsible credentials;
missing firms are not fabricated. The source browser separately displays all
published broker-associate relationships, including those whose responsible
credential is missing from the primary file. Mailing addresses are not imported.
The browser queries the last collected snapshot; restored current versions are
explicitly identified in history and in the current-roster comparison.

`CaliforniaBulk` has independent `WeeklyEnabled` (true), `RefreshDays` (7),
`StaleAfterDays` (8), `AlertsEnabled`, `AlertEmailEnabled` and optional
`ArchiveDirectory` settings. Disable its scheduler alongside Florida/Texas in
isolated hosts. For a bounded fresh collection:
`dotnet run --project src/Lms.Web -- --california-bulk-collect`.
For a validated archived acquisition (not a fresh download):
`dotnet run --project src/Lms.Web -- --california-bulk-import-archive "<directory>"`.
Admin-only exports allow exactly the two original files and `manifest.json`;
see [CaliforniaBulk.http](src/Lms.Web/CaliforniaBulk.http).

**Nevada NRED active lists.** Select Nevada in State Statistics or the directory's
bulk-roster selector. **Collect data** retrieves the 12 supported NRED active
workbooks server-side, validates every workbook/hash/count, retains original
files and source rows, and automatically activates the complete roster.
No administrator approval is required. Failed or cancelled runs retain the
current roster and last successful archive. Identical bytes reuse the same
snapshot; collecting a previously seen version after a restore activates it
without duplicating it or rewriting its original acquisition evidence.

The September baseline and later full-category acquisitions use the same
parser and previous/current comparison. Partial legacy captures are not
treated as full previous rosters. Missing records mean **Not observed**, not
revoked. **License lookup** searches the current published database snapshot
(including the saved baseline before any online collection), with name/number
and type filters and 50-row pages. All 11 numbered-license categories are
available to admins; public registration remains limited to Broker,
Broker-Salesperson and Salesperson. HOA rows are source evidence only.
Company names do not establish licensed firms or affiliations.
The separately published historical Reserve Study Specialists workbook is
outside this integration.

`NevadaBulk:WeeklyEnabled` defaults to `false` (manual collection);
`RefreshDays` defaults to 30. Enable/configure independent automatic updates in
Collection Schedules, using Nevada's `America/Los_Angeles` time zone.
NRED describes its active lists as monthly updates; acquisition time is not a
verified publication date. Downloads require application-server network access
to `red.nv.gov`, independently of the browser. Original XLSX files and the
manifest are Admin-only exports; see [NevadaBulk.http](src/Lms.Web/NevadaBulk.http).

The DRE page's Civil Code 1798.61(b) notice expressly limits use of its
**examinee list** to professional-education information; that list is separate
from the licensee ZIP and broker-associate workbook collected here. The page
does not state that examinee-list limitation for these two files. Examinee,
military-spouse and other files are not currently imported as standalone rosters.
This distinction is not a legal
opinion that every other use is permitted; addresses are not imported, and
synchronization does not expand registration ownership, instructor access or
existing pilot permissions.

The other non-examinee downloads were inspected on October 31, 2026 for schema
and row count only; no individual rows were retained. The daily MLO workbook had
29,085 rows and 20 columns, including DRE license ID/type/status, NMLS ID,
endorsement type/status, sponsor NMLS ID, sponsor name and contact details.
RE 776 says this file includes DRE licensees with `Licensed`/`Licensed NBA`
status and NMLS endorsement status `Approved`/`Approved Inactive`. A broker
working as a sole proprietorship may appear in separate individual and company
endorsement rows, each with a separate NMLS ID, so MLO rows are not unique MLOs.
This is an endorsement dataset linked to DRE licenses, not a separate license
profession. DRE's [MLO endorsement information](https://dre.ca.gov/Licensees/MLOLicense.html)
says NMLS manages these endorsements; the DRE license ID supports a cross-reference
to the California broker/salesperson roster. It is not currently imported.
The weekly-Monday new-licensees workbook had 228 rows and 27 columns with
license identity/type/status, dates, related-license, restriction and
ethics/agency fields substantially overlapping the daily full licensee ZIP.
It is a recent-issuance subset, not an additional statewide roster, and is not
currently imported. These are observed workbook counts, not regulator-published
totals.

The October 31, 2026 official acquisition contains **426,992 primary role rows**
and **25,542 broker-associate relationship rows**. The activated snapshot retains
**427,092 role records** (100 associate-only), **404,342 distinct license numbers**,
**34,742 firms** and **257,600 actual affiliations**. Roles: 288,141 salespersons,
90,622 brokers, 24,172 officers and 24,157 corporations. These are not unique-person
counts. Source status totals are 339,569 Licensed, 87,423 Licensed NBA and 100
Status not published. Florida/Texas current snapshots and all four pre-existing
account links were verified unchanged after activation.

`FloridaBulk:WeeklyEnabled` defaults to `true`, with `RefreshDays: 7`.
Collection Schedules retains expandable **Source** publication-cadence notes per
state, including official evidence, review date, scheduling guidance and saved
capture provenance where available. Verified on November 2, 2026: Florida DBPR
real-estate CSVs are weekly, Texas TREC datasets are daily, California's collected
license ZIP and broker-associate workbook are daily, and Nevada NRED active lists
are monthly. Colorado's selected dataset cadence remains unverified. Unknown
cadences must not be inferred from the LMS interval or a modification timestamp.
Each schedule row visibly explains the recommended interval and trade-off:
faster collection usually repeats unchanged files, while slower collection
delays new data. Weekly checks of Nevada's monthly source only reduce release
detection delay when its publication date is unknown; they do not produce weekly
regulator updates.
Nevada's saved evidence is [Active Licensee Lists PORTAL.txt](src/docs/NEVADA/TXTS/Active%20Licensee%20Lists%20PORTAL.txt),
line 5. It does not specify a publication day/time. Use **1 Month** rather than
**30 Days** for calendar-month collection; these are not equivalent. Notes do not
silently override saved schedules; daily-source collectors can intentionally run
less frequently if the administrator accepts reduced freshness.

`AlertsEnabled` and `AlertEmailEnabled` default to `true`; `StaleAfterDays`
defaults to 8. The scheduler checks alerts every minute before accepting another
run. For each implemented state (Florida, Texas, California, Colorado and Nevada),
active Admin accounts receive in-app notifications and email within the 24-hour
window before the next scheduled collection, after a successful scheduled
collection, for a failed scheduled run, or when the roster exceeds its stale threshold.
Upcoming reminders use the saved Collection Schedules date/time and state time
zone (including daylight-saving rules), plus UTC. Retry delays are reflected in
the effective next collection time. Reminders are not claims about regulator
publication dates. The host checks every minute while running; starting within
the reminder window sends a catch-up reminder, but past-due runs do not generate
advance reminders. Disabling a schedule suppresses alerts; rescheduling prevents
unsent reminders for the old date from being emailed. Already delivered in-app
notifications remain in history.
Successful identical-file collections still notify, explicitly noting that an
existing snapshot may be reused without record changes. Manual successes do not
produce scheduled-success notifications. Scheduled completion evidence persists
through subsequent manual/failed runs and application restarts.
Manual failures and cancellations do not produce scheduled-failure alerts.
Staleness is measured from the last successful acquisition, not roster activation
or restore. Hosts with weekly scheduling disabled do not generate these alerts.
One notification per incident and recipient is persisted across restarts; email
delivery results are audited and failed delivery retries at most hourly, including
after the roster recovers. Alert errors are logged and do not prevent collection.
Email uses the existing host email transport: default log mode simulates delivery;
configure a real transport (for SMTP, `EMAIL_DELIVERY_MODE=smtp` and the existing
`SMTP_*` settings) to deliver messages. Provider acceptance is not proof of inbox
delivery. A crash after provider acceptance but before recording success can cause
a retry; email exactly-once delivery is not guaranteed.
The scheduler operates only while the LMS is running and checks overdue work
on startup. Disable it in isolated tests or hosts that should not fetch data.
`FloridaBulk:ArchiveDirectory` optionally changes the persistent archive location;
the default is under the web host's `App_Data`. No independent scraping process
or localhost collector feed is needed for Florida.

Six fixed official statewide files are acquired: Broker/Sales, corporations,
schools/instructors, appraisal licenses, real-estate CE and appraisal CE.
Broker/Sales, instructors, firms and appraisal licenses are staged; schools,
additional locations and CE events are archived, not invented as licensees.
Regional extracts are not merged into statewide counts. Original source files,
hashes, acquisition metadata and row/layout checks are preserved. A failure
does not publish a partial roster or alter the current roster. Fully validated
records activate atomically on transaction commit. Earlier versions remain
available for history and recovery; missing identities are not deleted or revoked.
Other states without an official bulk adapter and manually imported sources
retain their existing approval workflow.
Internally, activated bulk snapshots use the existing `Approved` eligibility
status so roster searches, registrations and account-link consumers continue
working without a parallel status system. The Florida UI calls this
**Synchronized**, not approved; no reviewer is assigned.

**History and recovery:** open Regulatory Directory's **Florida roster history
and recovery** panel. Browse versions (20 per page), inspect their licensees
(25 per page), acquisition timestamps and record counts. Restoring requires an
active Admin, a reason and explicit confirmation. The restore is transactional
and audited, and rejects stale confirmations if the current roster changed.
Original source dates, license records, account links and confirmations remain
unchanged. A separate activation timestamp selects the restored version for
statistics, registrations, directory searches, campaigns and instructor checks.
The next successful synchronization becomes current, including a previously
seen archive; identical bytes do not duplicate snapshots. Last-collected archive
downloads remain acquisition evidence and are not relabeled as restored files.

Confirmed Florida instructor associations follow the same exact license
identity into the current roster. Instructor access is checked against that
version's instructor type, status, restriction and expiration, with verified
email still required. Missing, inactive, restricted or expired current licenses
do not grant supplemental access; account roles, school ownership and historical
confirmations are never changed by synchronization or restore.
The first October 31, 2026 archive was staged as **458,910 licensees** and
**38,155 firms**, with **302,159 verified employer/firm affiliations**. Appraisal
management-company credentials are firms, not people; appraisal course/provider
rows remain archived. Initial snapshot ID:
`51cfb357-67d4-6e22-fda5-c69841677267` (automatically synchronized).

Bulk excludes Null & Void and is not a historical all-status roster. Actual
statuses include involuntary inactive, suspended and probation. Absent licenses
are Not observed, never inferred removals. Addresses are mailing addresses;
source county attribution may differ from lookup searches. Source acquisition
time does not prove the weekly publication date. Credential identities preserve
prefixes; BL Broker Sales belongs to the BK family, not a separate license number.

The initial organized source archive and measured lookup comparison are indexed
in [Florida COLLECTION](src/docs/FLORIDA/COLLECTION/README.md).
Historical lookup exports remain available there, with their working store
untouched. Lookup is only a supplementary individual/historical verification
source; its old county-pagination controls no longer drive the Florida admin.

**Browse bulk licenses** searches the full last-successful source
archive, not the old latest-25 sample. Filters cover imported license type,
Broker/Sales qualification, exact county and mailing city (case-insensitive),
and contains matches for name/license or published status. Results are paged
50 at a time with full matching counts. Appraisal retains published status and
county codes in this source browser. School/location and CE rows are excluded.
Editing filters does not alter approved search, acquisition scope, exports or
the previous-versus-current roster comparison. Search applies the form; pagination
retains the applied query until Search is clicked again.

To stage a previously acquired, validated local archive without claiming a
fresh regulator download, run from the repository root:

```powershell
dotnet run --no-launch-profile --project .\src\Lms.Web\Lms.Web.csproj -- --florida-bulk-import-archive ".\src\docs\FLORIDA\COLLECTION\bulk-dbpr\2026-10-31"
```

This bounded command validates the manifest/files and activates the complete
roster, then exits without starting the web scheduler. Reimporting identical
bytes is idempotent; an older previously acquired PendingReview bulk snapshot
is activated only after its archive is revalidated. Existing snapshots rejected
by an administrator are not silently reactivated.
`/admin/florida-bulk/exports/{fileName}` serves only six fixed original CSV
names and `manifest.json` from the last successful archive. Requests require
Admin authorization and use no-store/nosniff headers; unknown or unavailable
archives return explicit errors. See [FloridaBulk.http](src/Lms.Web/FloridaBulk.http).

Other states can still opt in to legacy collection feeds by configuring
`RegulatoryCollections:Sources` as a map from
state name to the local collector's HTTP root URL, for example set the environment
variable `RegulatoryCollections__Sources__Florida=http://127.0.0.1:5011/`.
Only loopback HTTP roots are accepted and redirects are disabled. The collector
must expose `status.json` and `sample.json` using the Florida collector schema,
plus `licenses.csv`, `licenses.json`, and `entries.json`. No live source is
assumed for other states. Legacy collection runs separately; its UI never starts
or restarts it. Legacy download routes require Admin authorization and stream only those
three allowlisted resources through the LMS, with no-store headers. Export
files may lag live counters until the next export batch; unavailable files
return explicit 404/502 errors rather than empty downloads.

The profession/type table separates current approved licenses, incoming licenses,
new-to-baseline licenses, changed matches, and unchanged matches. Current means
approved in the LMS, not active according to the regulator; source statuses are
shown separately. No approved snapshot means an empty baseline, not evidence that
the collected licenses were newly issued. An older pending snapshot is labeled
as such. Counts identify licenses, not people, and do not establish statewide
coverage or source freshness. Samples and incomplete collections remain clearly
qualified; missing incoming records are never treated as removals.

Comparison identity is record group (licensee or firm), trimmed case-insensitive
license type, and license number. California also includes related license number
and type, preserving distinct officer/corporation relationships. Different types remain separate credentials.
Known Florida SL/Sales Associate, BK/Broker/Broker Sales, and
ZH/RE Instructor/Real Estate Instructor aliases share credential identities;
Broker Sales still has its own profession row. Other states' types are not
silently merged. Snapshot timestamps with offsets are displayed in UTC.
Changes cover stored names, levels, statuses, dates, related-license fields,
restrictions, and education fields; firm changes also cover address, DBA, and
qualifying broker. Missing source fields can produce differences without establishing a regulator
change. Affiliation rows and account links are not compared. Identical
duplicate licenses count once; conflicting duplicates block the comparison with
an explicit error. Records without license numbers are excluded and counted.
Only approved and pending-review snapshots participate; rejected snapshots do not.

## MailSlurp development email

### Instructor record visibility

The **Instructor Records** sidebar link (`/instructor/records`) shows existing
instructor account profiles, licenses, and faculty compliance dates. Administrators
can view all instructor accounts (including inactive accounts) and instructor
faculty records across states. Active instructors see only their own account
profile and faculty records explicitly assigned to their LMS account ID.
Accounts without Instructor/Admin access, inactive users, and unauthenticated
users cannot use this service.

Administrators assign or remove the **LMS account owner** on each faculty record.
Existing faculty records start unassigned and remain admin-only until assigned;
names and email addresses never establish access rights. Account details and
faculty records remain separate sources and are displayed separately, without
silently overriding either license number. Ownership changes are audited.
The `AddInstructorRecordOwnership` migration adds the nullable ownership link.
This page is read-only except for administrator ownership assignment. It does
not add document uploads or change course, payout, school-profile editing, or
mailbox permissions.

Instructors are also licensees: an existing instructor account can use the
**For Licensees** lookup/confirmation path and see its saved license selections
in **Account**, subject to the existing Development pilot and email-verification
requirements. Confirmation keeps the same LMS user ID and Instructor role; it
does not create another account or automatically claim faculty records. Faculty
ownership remains admin-assigned.

Existing brokers and learners can add another license from **Account → License
Profile → Add another license**. Confirming an unrestricted, active instructor
record in the latest approved roster adds Instructor authorization alongside the
existing Broker/Learner role; it never replaces the primary role, brokerage license,
enrollments, or brokerage assignments. Recognized roster types are `Instructor`,
`Real Estate Instructor`, and Florida `ZH`, with `Active` or `Current,Active`
status and no past renewal/expiration date. Other licenses and unknown/inactive
statuses do not enable instructor tools. This is still a licensee assertion in the
Development pilot, not independent identity verification or commission approval.

Supplemental Instructor authorization is available only while the Development
pilot is enabled and is recalculated from saved associations on authenticated
HTTP requests, including password and federated sign-in. Rejected associations,
unapproved/superseded snapshots, expired licenses, and unverified or disabled accounts
cannot supply supplemental access. A superseded selection must be confirmed again
from the latest roster. Instructor Records includes these combined-role accounts
and their saved license selections; administrators can assign faculty ownership to
them just as for primary Instructor accounts. Their original brokerage license stays
separate from the added instructor record.

### Instructor mailbox

Instructors and admins can open **Mail** in the sidebar (`/instructor/mail`).
The UI uses `InstructorMailboxService` and the provider-neutral `IMailboxProvider`
contract. The current adapter is MailSlurp; a future Microsoft Graph adapter can
reuse the UI. Switching adapters does not migrate addresses, messages, or drafts.

Mail is disconnected by default. To enable it in **Development**, put these
settings in the web project's .NET user secrets (never commit API keys):

- `MailSlurp:ApiKey`: your private API key
- `InstructorMailbox:Enabled`: `true`
- `InstructorMailbox:Accounts:0:UserId`: the instructor's LMS user GUID
- `InstructorMailbox:Accounts:0:InboxId`: a dedicated MailSlurp inbox GUID
- `InstructorMailbox:AllowedRecipients:0`: an exact synthetic test-recipient address

Repeat account/recipient entries with indices 1, 2, etc. Inbox IDs and user IDs
must each be unique. Do not assign the notification sender inbox or a mailbox
containing another person's mail. Assigning mailboxes and creating provider inboxes
is administrator setup, not an instructor self-service operation. This feature
does not create inboxes, purchase plans, or enable the separate notification
transport. Use synthetic data only.

The mailbox supports paginated Inbox/Sent, explicit refresh, plain-text compose,
one optional CC address, threaded replies, attachments (five files / 5 MB total),
text drafts, and local archive/restore. Drafts and archive flags are stored in the
LMS database by user and provider account; apply the `AddInstructorMailboxState`
migration through the normal application startup. Drafts do not retain attachments:
remove them before saving and reattach before sending. Search filters only the
current page. Incoming HTML is displayed as text, never executed. Downloads are
limited to 5 MB and offered as files rather than embedded HTML.

Both reply recipients and Reply-To addresses must be allowlisted. Replies retain
provider threading; changing recipients requires a new composition. Provider errors
are surfaced, with no simulated success or automatic resend. Check Sent/refresh
after an ambiguous delivery error before retrying. Free-plan limits can prevent
external delivery or attachments; those failures remain visible. Microsoft 365
sign-in, background synchronization, mailbox provisioning, and history migration
are not included in this development adapter.

Live delivery checks can be run with the opt-in `InstructorMailboxLiveTests`.
Set `RUN_LIVE_MAILBOX_TESTS=1`, `MAILBOX_LIVE_API_KEY`,
`MAILBOX_LIVE_INSTRUCTOR_INBOX`, and `MAILBOX_LIVE_COMMISSION_INBOX` in the
test process environment. Use two existing synthetic inboxes only. The test
verifies exact body receipt in both directions. If MailSlurp explicitly rejects
threaded replies under its evaluation restrictions, it reports that limitation
and separately tests a new return email; this is not counted as successful
threading. The application never silently replaces a rejected reply with a new
message. Optional `MAILBOX_LIVE_EVIDENCE_PATH` saves the test outcome without
the API key or mailbox addresses.

MailSlurp is an opt-in Development-only transport for the existing email service.
It replaces SMTP/log delivery only when `MailSlurp:Enabled` is true. Store the
following configuration in the web project's .NET user secrets, never source control:

- `MailSlurp:Enabled`: `true`
- `MailSlurp:ApiKey`: your private API key
- `MailSlurp:SenderInboxId`: an inbox you own
- `MailSlurp:AllowedRecipients:0`: an exact test inbox email address

Add further allowlisted inboxes with indices 1, 2, etc. All other recipients are
blocked and logged as delivery failures; there is no fallback to SMTP or simulated
success. Use synthetic learner data only. Do not transmit actual roster identities,
license details, or certificate data to a hosted test service without an approved
data-handling arrangement. User secrets are outside the repository but unencrypted.

Run a single synthetic email smoke test without starting the web server or seeding
the database:

```powershell
dotnet run --project .\src\Lms.Web\Lms.Web.csproj -- --mailslurp-smoke-test
```

Confirm the uniquely timestamped message arrives in the allowlisted inbox.
The transport supports in-memory PDF attachments as well as existing receipt
attachments.

### Synthetic learner browser journey

Opt in with these additional user secrets:

- `EmailJourney:Enabled`: `true`
- `EmailJourney:TestRecipient`: the one synthetic learner inbox, also in the MailSlurp allowlist
- `EmailJourney:PublicBaseUrl`: your local origin, e.g. `http://127.0.0.1:5000`

Open `/email-test`, sign out of any other account, and register the configured
test inbox. Follow its verification email and explicitly confirm verification.
Then sign in using the normal `/login` page: only this learner must enter the
emailed six-digit code before an authentication cookie is issued. Codes expire
after 10 minutes; verification links expire after 30 minutes. Each is single-use,
replacement requests invalidate older challenges, and five incorrect attempts
invalidate a challenge. Secrets are stored as hashes; pending sign-in is bound
to an encrypted, HttpOnly browser cookie. Registration, confirmation, code, and
certificate-send forms require antiforgery tokens and rate limiting.

After sign-in, `/email-test` emails an authenticated owner-only PDF download link.
The free MailSlurp sandbox does not permit sending attachments. On a plan that
supports them, explicitly set `EmailJourney:IncludeCertificateAttachment` to
`true` to include the synthetic PDF as well (default: `false`). No enrollment, official completion,
roster link, or regulatory credit is created. The Florida DBPR link is a source
reference only, not a claim that the synthetic license exists. Actual roster
identity-claiming and production-wide verification remain out of scope.

Existing learners keep their normal login behavior. The test learner's two-step
flag persists, so disabling this feature does not silently allow that account
to bypass its email step. MailSlurp and the journey cannot be enabled outside
Development. Apply the `AddEmailVerificationChallenges` migration before using
the flow (normal application startup migrates the canonical database).

### Roster-to-account pilot

With the same Development-only email journey enabled, open
`/roster-registration` (also linked from registration and the email test page).
Select a state and enter an exact license number. No identities are loaded
before searching. The lookup uses only that state's latest approved snapshot,
returns at most 20 records, and shows source provenance and retrieval date.
An approved snapshot is a reviewed data import, not proof that an applicant
owns the listed license. Source links open the recorded source/lookup page;
they are not necessarily individual-record permalinks.

Nevada number-only searches accept the full numeric portion after `B.`, `BS.`
or `S.` while retaining leading zeros; partial-number searches do not match.
The isolated `NevadaRosterAccountJourneyUiTests` browser test covers lookup,
selection, signup, captured verification email, automatic confirmation, one-use
verification-token rejection, and the Account license profile. It also checks
that appraiser records remain excluded from public registration. The test uses
a temporary SQLite database, synthetic licenses and an in-memory mailbox;
no NRED access or real email delivery is needed. A test-only authenticated
session opens Account after verification; password-plus-email-code login is
outside this particular test. Run with `RUN_UI_TESTS=1` and filter
`FullyQualifiedName~NevadaRosterAccountJourneyUiTests`.

A new test learner can register with a selected record; the account and pending
request are saved transactionally, then a generic verification email is sent.
An existing test learner signs in with the email code and submits the selection
without creating another account. Failure to deliver verification leaves the
saved request awaiting email verification; signing in requests a replacement.
Roster names and license details remain in the local database and browser;
email/certificate content stays synthetic.

Routine administrator approval is not required. An active existing licensee with
verified email confirms a current, unrestricted commission record; Account shows
**Confirmed by licensee** immediately. Signup selections remain pending only for
email verification, then become confirmed in the email-verification transaction.
Confirmation is a licensee's declaration, not independent proof of identity.
Admins can inspect **Regulatory Directory → Student license associations** with
state/status filters and 20-row pagination, and reject associations for corrections
or disputes with a required, audited note. Duplicate selections and repeated
rejections are blocked. Learners see their latest 20 selections and notes.

These are explicitly **test associations**, including after student confirmation: they do
not populate `UserRegulatoryLicenseLinks`, copy roster names/license fields
into the learner's verified profile, or create credit by themselves. For
licensee certificate details, the student explicitly selects a confirmed
same-state association and confirms their printed name; the license number
is copied server-side from that record. Production identity evidence is a
separate rollout. Existing direct admin linking and other account flows remain
unchanged. Startup applies the `AddRosterAccountRequests` migration and
`ConfirmStudentRosterAssociations`, which converts previous Pending/Approved
associations for active, email-verified students to Confirmed. Unverified and
rejected selections are not promoted. Historical review fields are retained.
The data-policy migration cannot be automatically reversed into old admin decisions.

### Final exam and certificate details

Passing a final exam presents **Review Certificate Details** before answer review.
Pre-licensing students confirm their printed name without needing a commission
record or license number. Continuing-education and post-licensing students must
have a student-confirmed, unrestricted commission record for the course state
and an active, email-verified Learner account. Missing records lead to license
lookup; students can resume certificate details from My Courses.

The server supplies the course state and, for licensees, the selected record's
license number. Printed-name confirmation, course completion, required-exam pass,
and applicable attendance remain separate requirements. Identity confirmation
and certificate creation are transactional; issued identity snapshots cannot be
edited by resubmission. Historical certificates retain their existing identity
policy, and unclassified courses retain the legacy manual identity flow.
Development sample courses never issue official certificates or regulatory credit.
Certificate processing/reporting review is separate from license association
confirmation and remains unchanged.

## Next steps
1. Add real persistence with EF Core and a database.
2. Replace the in-memory service with a database-backed service.
3. Implement authentication and role-based authorization.
4. Build lesson progress, quizzes, certificate issuance, and reporting flows.

## Running the application and choosing the database

Open the repository root in VS Code and select **LMS (canonical app)** in
Run and Debug, or run this command from the repository root:

```powershell
dotnet run --project .\src\Lms.Web\Lms.Web.csproj
```

The HTTP launch profile uses `http://127.0.0.1:5000`. Stop an existing instance
before launching another on the same port. The VS Code build task and both
solutions use the projects under `src`; `LmsStarter.sln` is the VS Code default.
The root-level `Lms.Web` project is retired and rejects build/run attempts with
directions to the canonical project.

By default, SQLite uses `App_Data\lms.db` under the app's content root:
`src\Lms.Web\App_Data\lms.db` for the local launch commands above.
The older `Lms.Web\App_Data\lms.db` is retained but is not automatically used,
merged, migrated, or deleted by this cleanup. Isolated `roster-benchmark.db`
files are separate test fixtures, not application databases.

Intentional database overrides remain supported, in this precedence order:

1. `Lms:DatabasePath` configuration (for example, `Lms__DatabasePath` in the
   environment or `--Lms:DatabasePath` on the command line).
2. The `LMS_DATABASE_PATH` environment variable.
3. The default content-root database.

Relative override paths are resolved against the process working directory.
Startup logs the resolved absolute database path, the setting that selected it,
and the content root **before** migrations or seed data run. Check that message
when using a test database or diagnosing missing data. Launching the published
DLL manually requires the intended content root/working directory; publishing
does not copy local database contents into a deployment.

Course and enrollment workflows use `IExamService` for exams and
`IUnitExamService` for unit exams. Broker blocked-learner lists and learner
activity logs use the same exam terminology. Historical migrations keep their
original assessment/module names so already-applied migration history remains valid.

## Product roadmap options
- Business-priority variants roadmap: [src/docs/business-priority-variants-roadmap.md](src/docs/business-priority-variants-roadmap.md)
- SSO provider profiles: [src/docs/sso-provider-profiles.md](src/docs/sso-provider-profiles.md)

## Local 50-state development catalog

Enable explicitly for local testing only:

```powershell
$env:DevelopmentCatalog__Enabled = "true"
dotnet run --project .\src\Lms.Web\Lms.Web.csproj --urls http://127.0.0.1:5000
```

Startup creates one development copy of every original course for each US state,
reusing the original in its matching state. All originals, including archived and
draft courses, are sources. Originals are not modified. Stable IDs prevent
duplicates on subsequent starts; existing copies are not overwritten.

Copies include units, lessons, exam definitions/questions and unit-exam options,
but no enrollments, progress, attempts, certificates, payments, schedules or approval
history. Copies remain unpublished drafts, with no commission course number, and
are free to test. The explicit development flag makes them visible in the learner
catalog across state restrictions. Sample completion cannot issue certificates or
appear in completion, monthly or cheating-incident regulatory reports. Samples cannot be published or
approved for credit. Never enable this catalog on a public production deployment.

Unset `DevelopmentCatalog__Enabled` (or set it to `false`) and restart to hide
samples from the learner catalog and course retrieval without deleting test data.
Do not rename their reserved `dev-state-` slugs; these identify development samples.

### Local development certificates and roster logins

In the **Development** environment, enable `DevelopmentCertificates__Enabled=true`
and set `DevelopmentCertificates__InitialPassword` to a strong local test password
before starting the canonical app. Never commit that password. The first run
creates up to two learner logins for each of the 50 states using actual roster
professionals. Only unrestricted individual records with an Active, Current, or
Licensed status are selected (case-insensitive); company licenses are excluded.
Missing rosters or insufficient eligible records are skipped, never replaced with
fictional identities. Admins see the 50-state coverage/gap list on the page; the
same report is saved as `coverage.json` beside the PDFs.
Their reserved `@certificate-dev.invalid` addresses cannot
receive real email; marketing is opted out. Existing accounts and passwords
are preserved on subsequent starts, and the password setting can then be removed.

Open **Admin > Certificates** (`/admin/certificates`). The **Sample certificates**
and 50-state coverage report appear below official certificate compliance,
without a separate menu or page. Only admins can browse this list; the download
endpoint still permits a learner to download their own specimen. PDFs are also
saved beside the selected database under `development-certificates`. They use
the supplied FL/NC examples as field/layout references, with prominent
**DEVELOPMENT / NOT VALID FOR CREDIT** markings, simulated dates/hours, no copied
signatures, and no claim of actual course completion or provider approval.
States other than FL/NC use a generic simulated Ethics at Work certificate,
without claiming a state credit category or a state-specific approved template.
PDF filenames include state, learner, course, date, and a unique ID; repeat downloads
of the same specimen intentionally retain the same filename.
Admin certificate lists require a state selection before querying or displaying
learner records. Clearing filters returns to this empty state. Queries filter
in SQLite and return at most 20 records per page, with matching counts and
Previous/Next controls. Select a state, optionally narrow by course, status
(official certificates), or learner/email/license search, then Apply filters.
Both lists also support certificate-number search. Changing state or
applying filters returns to page one. Overall compliance totals use database
aggregates, not a full certificate-list load, and remain independent of filters.
These are separate specimens, not `CompletionCertificates` records, enrollments,
attendance, or exam results; they never enter official verification or regulatory
completion reports. Disabling the flag hides the UI without deleting accounts or
files. Never enable this fixture on a public deployment.

Learner navigation saves lesson completion when continuing past a lesson's last
page, or beginning its boundary exam. Intermediate and practice results keep the
course timer running; refreshing restores the same UTC start instant. The course
overview uses persisted progress, not just the currently displayed lesson.
Sample final-exam success does not offer a certificate. Startup also retires
known legacy `O/L` final-exam duplicates from required gates in source courses
and their existing development copies, retaining definitions and attempt history.

The learner catalog and staff course lists support state filtering and title/description
search. `/state-licensing?state=Alaska` opens a public real-estate licensing reference
page with substantive licensing Q&A for all 50 states, citing saved guides and
official agency instructions. Each answer includes its source references, review
date, and currency limitations. A review of a saved document does not establish
that its rules are current; applicants must confirm current agency instructions.
Internal development notes and LMS course catalog entries are not licensing authorities.
The [saved state-by-state Q&A source assessment](src/docs/state-licensing-qa-source-assessment.md)
records evidence ratings, known gaps, and source-collection priorities for future work.

Admins manage state questions at `/admin/licensing-qa` (Admin > Licensing Q&A).
Public and admin question lists follow the educational track: prelicensing
education, examination, initial licensing/application, postlicensing/first renewal,
ongoing renewal/continuing education, then broker-upgrade education. Within each
stage questions are alphabetical. Public ordering uses only the published question,
not draft edits. Stages are inferred from question wording.
Startup imports the existing researched Q&As into the database as published entries,
including their source notes, without overwriting later edits on restart.
Each entry has separate question, answer, and admin-only source-information inputs.
Save a draft, then publish it explicitly; editing a published entry leaves its
previous published answer visible until the new draft is published. Publishing
requires source information. Unpublishing hides the entry but retains its draft
and sources. Other roles and anonymous visitors receive only published questions
and answers, never source notes or draft text. Saving, publishing, and unpublishing
record the complete previous version (question, answer, private source notes,
published text, and publication status) together with the actor and UTC time.
The entry and audit record are committed atomically. Select a saved Q&A to view
its admin-only audit history below the editor; the editor is the current draft.
These records also appear in Admin > Audit under `licensing.qa` actions.
Legacy events remain visible but have no recoverable previous content; newly
created entries explicitly have no previous version. No revision rollback is performed.
Deleting a saved Q&A requires confirmation and soft-deletes it: the public page
and active admin list exclude it, but the admin-only Deleted list retains its
content, private sources, deletion date, and complete audit history. A
`licensing.qa.delete` event captures the last saved version, actor, and UTC time
atomically with deletion. Deleted entries are read-only and cannot be published,
edited, or recreated by the startup import.
Copied lessons are templates, not state-localized or approved course material.
