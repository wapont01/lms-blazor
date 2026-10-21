# Full-roster development benchmark

Loads all 32,054 credentials in the registered Idaho, Indiana, Kentucky and Washington development datasets into a **new isolated SQLite database**, using the current `ApplicationDbContext` schema and indexes. Optionally adds the full dated California import and its affiliations through the existing importer. It does not load small smoke-test samples instead of full datasets and never connects to the application's existing database.

## Run

From the repository root in Windows PowerShell:

```powershell
dotnet run --project .\src\tools\Lms.RosterBenchmark\Lms.RosterBenchmark.csproj --configuration Release -- 'C:\path\to\lms-blazor' 'C:\path\to\new-benchmark-output' --include-california
```

Omit `--include-california` for the four-state baseline. The output directory must not already exist; previous results are never overwritten. Generate and validate the registered full inputs first with [Build-FullLicenseDevelopmentData.ps1](../../docs/ENTITY_CAMPAIGN/Build-FullLicenseDevelopmentData.ps1). Existing application dependencies are reused.

Outputs are `roster-benchmark.db` and `benchmark-results.json`, retained at the supplied output path. Do not commit databases containing licensee data. The database is a benchmark fixture, not a replacement for the application's database.

## Verification and measurements

- Checks registered file hashes, exact credential identities, state import counts and duplicate import prevention.
- Keeps original published names, statuses and available authoritative dates in the application tables.
- Retains complete original development-record JSON and exact business types in a benchmark-only evidence table; verifies every stored source payload.
- Links only the registered explicitly evidenced Kentucky Principal Broker relationship. Unknown relationships are not invented. Optional California importer preserves its source role/appointment records and affiliations.
- Records import durations, database bytes after each state, application-table counts, runtime environment and representative SQLite query plans.
- Uses the actual `RegulatoryDirectoryService` admin license-list, firm-target and snapshot-summary queries, plus indexed identity/relationship lookups. Each has one unmeasured warm-up and 20 measured sequential iterations, with nearest-rank p50/p95 and output count checks.
- Performs approval only inside the isolated database, solely to exercise approved-snapshot admin queries. No operational eligibility, outreach permission or production approval is asserted.

The schema is created with `EnsureCreated`, so this is not validation of historical database migrations. It contains no course/user/payment workload, no concurrent administrators and no browser/network rendering measurements. Source parsing and setup are excluded from per-state application import durations. Import durations are for this harness, not a live admin upload endpoint.

The application firm model does **not** yet retain regulator or exact business-license type as an identity component. The evidence table prevents source loss here, but it does not fix live application identity. Discipline/restrictions, full jurisdiction coverage and other source fields are likewise not all represented in application entities. Live importer integration needs those schema/UX decisions first; do not reuse this fixture's approval as authorization to load the live database.

`GetCampaignFirmTargetsAsync` currently materializes all matching firms/affiliations before sorting and applying `take`. The benchmark deliberately measures that existing behavior without substituting a faster proxy or changing output ordering/renewal semantics. A limit of 100 returned rows is not a database pagination guarantee.

The recent inputs cover full saved exports, not necessarily statewide completeness: Washington is Seattle-only and Indiana is bounded captures. Older downloaded state rosters are not registered yet. Database growth is real for the loaded source records, but these results are not a nationwide 22-million-record test or evidence of a performance SLA.
