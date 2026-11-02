# Commission roster sources

This is the canonical home for locally collected roster source files. Keep
rosters separate from the course, statute and licensing-reference documents in
`src/docs`.

## Organized source baseline (2026-10-24)

The catalog contains 461 files (1,346,246,541 bytes) organized into 76 capture
folders: 48 states, Puerto Rico and one shared multi-state collection. All copies
were SHA-256 verified, and a repeated apply copied zero files. Alabama, District
of Columbia and Wisconsin have no captured roster source files to organize.
This is source-file coverage, not the number of imported or searchable states.

## Import pass (2026-10-24)

The canonical importer registered current snapshots for 26 jurisdictions, with
599,646 individual credential records, 50,712 firm records and 157,364 published
firm/person affiliations. These totals cover this import pass, not all records
already in the application, and do not count historical superseded snapshots.
They are credentials, not a deduplicated count of people.

The running roster selector was verified to offer **47 states plus Puerto Rico**.
Alabama and Wisconsin have no captured roster; New Mexico has firms only. DC also
has no captured roster. Browser checks exercised Arizona, Colorado, Ohio and
Puerto Rico, including full numeric-part matching in Arizona and combined
name/type/firm filtering in Ohio. Results remain paginated at 20 with explicit
student confirmation. The web build and 29 focused import/search tests passed.

See [the initial import report](import-results-20261024.json) and
[the supplemental report](import-results-20261024-supplement.json) and
[the current gap-recovery report](import-results-20261024-gaps.json) for
per-jurisdiction counts, immutable source hashes, explicit exclusions, warning
row numbers and snapshot IDs. The supplemental pass adds Mississippi companies
and branches and a new Ohio snapshot with published primary-firm references.
Ohio effective/renewal dates are not treated as original license issuance.
Earlier snapshots and user links were not replaced.

The gap-recovery pass added 53 source-supported firms: New Jersey now has 74
(52 additional single-name company credentials, with verified profiles taking
precedence), and Louisiana has 69 (one exact repeated name/AKA value collapsed
without truncating distinct aliases). Connecticut's mixed broker sample
reconciles seven already verified firms; 13 holders remain unclassified.
Six New Jersey credentials with multiple aliases (12 source rows) still need
verified legal-name profiles. Individual and affiliation totals did not change.
All three replacement snapshots received owner-authorized interim approval;
repeat-import safety and unchanged unrelated table counts were verified.
The 34 focused import/search tests passed.

Interim acceptance follows the workspace owner's explicit instruction; it is
audited without impersonating an administrator. Future runs default to
`PendingReview` unless `--approve-interim` is explicitly supplied. Approval does
not establish student ownership, create account links or change certificates.

Remaining coverage limits:

- Alabama, District of Columbia and Wisconsin have no captured roster source.
- New Mexico has 5,116 business credentials but no captured individual roster,
  so it is not a licensee-search option.
- Connecticut imports the 20-person Hartford salesperson sample and verified
  firms. Its mixed person/business broker-category capture is retained for
  classification rather than classifying holders by their names.
- Other city-, county- or surname-limited captures remain limited samples.
  Importing them does not establish statewide completeness.
- Missing identifiers/names, overlength fields and source test/delete
  placeholders are excluded explicitly with source row references. Appraiser,
  cemetery and other non-real-estate categories are not treated as brokers.
- Rhode Island has 197 source expirations that repeat an internal record ID
  instead of a date; those dates remain unknown, with warnings.
- Only explicit firm/person relationships with imported endpoints are linked.
  Person/person and firm/firm edges cannot be represented as firm/person
  affiliations and remain in source evidence along with unresolved endpoints; no
  broker-in-charge or account ownership is inferred.

## Import command

From `src`, preview all registered source mappings without changing the DB:

```powershell
dotnet run --project .\tools\Lms.RosterImport -- .\data\rosters .\Lms.Web\App_Data\lms.db .\preflight-report.json
```

To apply, add `--apply`. Add `--approve-interim` only with explicit owner
authorization for that import. Use a new report path each time. The tool requires
an existing migrated SQLite database and makes a consistent SQLite backup before
writing. Each jurisdiction is transactional; previously committed jurisdictions
survive a later failure and are skipped on a repeat. Reports are checkpointed
after each jurisdiction and marked `Complete` only after all succeed.

Source SHA-256 verification, count reconciliation, repeat-import checks and
unchanged user/enrollment/certificate/account-link/request counts are mandatory.
The importer never refreshes or deletes a reviewed snapshot in place. Bump the
parser version/fingerprint for mapping changes and create a new snapshot.
Snapshots retain provenance through the import audit/report and canonical
manifest; source payloads stay local, not in an ad hoc database evidence table.

Capture dates remain unknown for 123 files (`undated`). Seven files have no
single established official URL: five shared multi-source assets, a West Virginia
ARELLO capture and a Wyoming appraisal-discipline file. Missing metadata is left
explicit rather than invented. Cataloged supporting material may include firm,
appraiser or discipline records; these are not automatically individual
real-estate licensee rosters.

```text
src/data/rosters/
  source-catalog.csv
  inventory.csv
  <state-or-territory-slug>/
    <YYYY-MM-DD-or-undated>/
      manifest.json
      raw/
      prepared/
  multi-state/
    <YYYY-MM-DD-or-undated>/
      manifest.json
      raw/
      prepared/
```

- **raw**: unchanged commission downloads, public-search captures and original
  evidence. Preserve original filenames and necessary source subdirectories.
- **prepared**: derived import datasets, filtered samples, relationship files
  and their preparation metadata. These are not substitutes for raw evidence.
- **manifest.json**: jurisdiction, source capture date, official source URL,
  coverage limitations, original location, byte length and SHA-256 for every
  stored file. An unknown capture date is `undated`; never substitute a copy date.
- **source-catalog.csv**: machine-readable registry driving source organization.
- **inventory.csv**: human-facing source coverage and import-gap inventory.
  Coverage and import status describe the inventory date, not a live DB query.
- **multi-state**: datasets inherently spanning multiple states. Do not duplicate
  those files into every state folder or imply nationwide coverage.

## Organizing and verifying files

Run from the repository root using Windows PowerShell. The execution-policy flag
applies only to that process; it does not change the machine's policy.

```powershell
# Validate the catalog and preview totals without copying anything.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\tools\Organize-RosterSources.ps1

# Copy missing files, verify SHA-256 and write per-capture manifests.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\tools\Organize-RosterSources.ps1 -Apply

# Require all canonical files and manifests to exist and match.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\tools\Organize-RosterSources.ps1 -VerifyOnly
```

Run the isolated organizer regression checks without accessing real roster data:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\src\tools\Test-RosterSourceOrganization.ps1
```

The organizer never overwrites differing source content, moves/deletes legacy
files, imports records, changes snapshot approval, or writes to the database.
Repeating it with unchanged sources copies nothing. A changed capture belongs in
a new dated folder, not over an existing immutable source. When two captures
occur on the same date, extend the folder convention and organizer explicitly
before recording them; do not replace the first capture.

## Compatibility and version control

Existing importers still reference files under `src/docs`. Their legacy files
remain intact during this transition; canonical copies are hash-verified
against them. New import work should resolve sources from this dedicated
store. Retire legacy copies only after their importers and tests are migrated.

Commit tooling, documentation and metadata catalogs, not licensee datasets.
The `raw` and `prepared` directories are ignored by Git. Keep local data backed
up separately; Git is not a roster backup. Do not place credentials, personal
contact data or licensee rows in catalogs/manifests.

The application database remains `src/Lms.Web/App_Data/lms.db`. Its approved
snapshots are what search uses; the presence of a source file here does not
mean it has been imported, approved, or verified as belonging to a student.
