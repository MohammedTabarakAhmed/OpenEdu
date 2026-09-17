# OpenCampus

Academic eLearning Platform for Horizon Training Institute, implemented against
`docs/opencampus-e-learning-design-v1.docx` (HTI-OC-SDD v1.0). Section references
below are to that document.

## Prerequisites

| Component | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet --version` |
| `dotnet-ef` global tool | 10.0.x | `dotnet tool install --global dotnet-ef` (only needed to add migrations) |
| SQL Server Express LocalDB | 2022 (16.x) | Instance `MSSQLLocalDB`; starts automatically on first connection |
| Node.js / npm | 24.x / 11.x | |
| HTTPS development certificate | | `dotnet dev-certs https --trust` (once per workstation) |

No global Angular CLI is required; the workspace-local CLI is used through `npm` scripts.

## Build and run

From a clean clone, three commands take the solution to a running, populated system (DEP-12):

```powershell
dotnet run --project src/Api/OpenCampus.Api      # restores, builds, applies migrations, serves https://localhost:7161
npm --prefix client install                       # restores client dependencies
npm --prefix client start                         # serves http://localhost:4200, proxying /api to the host
```

Open `http://localhost:4200` and sign in. On first run in `Development` or `Demonstration` the host provisions the
reference data (roles, permission catalogue), the demonstration identities and the demonstration academic structure
(4 programmes, 12 courses, 12 sections with sessions and grade schemes, 40 learner records, ~126 enrolments) and the demonstration
course content (three units per live section with published pages, links and a file resource, plus one unpublished draft), and — because no credential may be
held in source (section 9.2) — generates the initial passwords and writes them to
`src/Api/OpenCampus.Api/data/provisioning/credentials.txt` (excluded from source control; the log records only the path).
To choose the passwords instead, set `Provisioning__AdministratorPassword` and `Provisioning__DemonstrationPassword`
before the first run.

| Account | Role | Shell |
|---|---|---|
| `admin` | Administrator | `/admin` |
| `registrar` | Registrar | `/admin` (restricted) |
| `instructor1` … `instructor3` | Instructor | `/instructor` |
| `learner01` … `learner40` | Learner | `/learner` |

### Environments (section 9.3)

| `ASPNETCORE_ENVIRONMENT` | Database | Migrations on startup | Reference data | Demonstration data | OpenAPI |
|---|---|---|---|---|---|
| `Development` (default for `dotnet run`) | `OpenCampus.Dev` | Yes | Yes | Yes | `/openapi/v1.json` |
| `Demonstration` | `OpenCampus.Demo` | Yes | Yes | Yes | No |
| `Test` (integration tests) | `OpenCampus.Test` | Applied and dropped by the test fixture | By the fixture | No | No |
| any other | configured | No (apply with `dotnet ef database update`) | Yes | No | No |

Connection strings live in `appsettings.{Environment}.json` and use integrated security; no credential is held in the repository.
Override with the `ConnectionStrings__OpenCampus` environment variable.

### Tests

```powershell
.\test.ps1                        # the full suite by a single command (TST-04)
dotnet test                       # server only: unit and integration tests
npm --prefix client test          # client only: unit tests
```

Integration tests create, migrate, provision and drop `OpenCampus.Test` once per run through a shared
collection fixture (TST-01, TST-02). Override the test connection string with the `OPENCAMPUS_TEST_CONNECTION`
environment variable. The section 12 reference table forbids `UnitTests` from referencing an Infrastructure
project, so tests of infrastructure implementations (hashing, tokens, TOTP) live in `IntegrationTests`.

### Adding a migration

Each module has its own context and migration history. Always name the context and the module project:

```powershell
dotnet ef migrations add <Name> --context SisDbContext --project src/Sis/OpenCampus.Sis.Infrastructure --startup-project src/Api/OpenCampus.Api --output-dir Persistence/Migrations
```

## Architecture summary

A modular monolith (AD-01) with three modules — Identity, SIS, LMS — each realised as
Domain, Application and Infrastructure projects, composed only in the `OpenCampus.Api` host (MB-05).
Project references conform exactly to the section 12 table; domain projects reference nothing but `OpenCampus.SharedKernel` (LR-01).

```
src/
  SharedKernel/                 Entity base, Result, domain exceptions, SequentialGuid
  Identity/  Domain | Application | Infrastructure (IdentityDbContext, schema `identity`)
  Sis/       Domain | Application | Infrastructure (SisDbContext,      schema `sis`)
  Lms/       Domain | Application | Infrastructure (LmsDbContext,      schema `lms`)
  Api/                          Host: composition root, middleware, health, OpenAPI
tests/
  OpenCampus.UnitTests          Domain and application; no database
  OpenCampus.IntegrationTests   Host against an isolated database
client/                         Angular single-page application (core / layouts per role / features)
```

One SQL Server database holds one schema per module, each with its own `__EFMigrationsHistory` table (AD-02, DC-07, DC-08).

### Identity module (Increment 2)

| Layer | Contents |
|---|---|
| Domain | `User` (credential, lockout and MFA state transitions), `Role`, `Permission`, `UserRole`/`RolePermission` (composite keys), `UserSession` (refresh-credential generations grouped by family), `AuditEvent` (immutable), `AuditEventTypes` |
| Application | `AuthenticationService` (login, MFA, refresh/rotation, logout, principal, password, MFA enrolment), `UserAdministrationService` (users, roles, sessions, audit read), `IdentityProvisioner`, request/response contracts with one FluentValidation validator each (18.2), repository and security abstractions, `Permissions` catalogue (Appendix C), `TokenOptions`/`AccountProtectionOptions`/`PasswordHashingOptions`/`ProvisioningOptions` (Appendix B) |
| Infrastructure | `IdentityDbContext` and entity configurations, repositories, `Pbkdf2PasswordHasher`, `FileSigningKeyProvider`, `JwtAccessTokenIssuer`, `RefreshTokenGenerator`, `Rfc6238TotpService`, `DataProtectionMfaChallengeIssuer`, `ProtectedStringConverter` (MfaSecret at rest) |
| Host | `AuthController` (`api/v1/auth`), `UsersController` (`api/v1/users`), `RolesController`, `AuditController`, JWT bearer validation, deny-by-default fallback policy and one named policy per permission code, `[HasPermission]`, fixed-window rate limiter, `GlobalExceptionHandler` (18.3), `ValidationFilter` (18.2), `RefreshCookie`, `HttpCurrentUser` |
| Client | `core/auth` (`SessionService`, `authInterceptor`, guards), `core/i18n` (string resources, language/direction), `features/auth/login`, role layouts |

### SIS module (Increment 3)

| Layer | Contents |
|---|---|
| Domain | `Programme`, `Course`, `CourseSection` (aggregate owning `Session` and `GradeComponent`; BR-01, BR-03, BR-14, BR-16; Draft → Open → Closed / Cancelled), `Learner`, `Enrolment` (BR-02; `Reinstate` after withdrawal), `GradeEntry` and `Certificate` (schema only until Increments 5–6), `BusinessRules`, enumerations for the 13.6 reference sets |
| Application | `ProgrammeService`, `CourseService`, `SectionService` (transitions, sessions, grade scheme), `LearnerService`, `EnrolmentService` (transcript), `LearnerSelfService` (catalogue, self-enrolment scoped to the caller), contracts with one validator each (18.2), repository abstractions, `IUserDirectory` (contract to Identity, MB-02), `ICurrentUser`, `SisProvisioner` (DEP-11) |
| Infrastructure | `SisDbContext` (actor stamping, logical-deletion filter), entity configurations with the 13.3 indexes, repositories with server-side paging and batched lookups, migration `SisEntities` |
| Host | `ProgrammesController`, `CoursesController`, `SectionsController`, `LearnersController`, `EnrolmentsController`, `CatalogueController`, `MyEnrolmentsController`; `IdentityUserDirectory` (host-side implementation of the SIS→Identity contract, MB-05); rule violations → 422 with the rule reference |
| Client | `features/admin` (programmes, courses, sections and section detail, learners and learner detail, users), `features/learner` (catalogue, my enrolments), `shared` (paged collection state, page controls, field errors, confirmation, locale date and bilingual pipes) |

### LMS module (Increment 4)

| Layer | Contents |
|---|---|
| Domain | `CourseContent` (aggregate owning `ContentItem` and, through it, `Resource`; BR-15 via `ItemsVisibleToLearners` / `FindItemForLearner` / `EnsureVisibleToLearner`; item types Page, Link, File as the 13.6 reference set), `Assignment`, `Submission`, `AttendanceRecord` (+ `AttendanceStatus`), `Announcement` (schema only until later increments), `BusinessRules` |
| Application | `ContentService` (hierarchy, items, publication, upload, removal), `ResourceService` (authorised download), `LearnerContentService`, `SectionScopeResolver` (SEC-12: Manager / EnrolledLearner / None per section), `ISectionAccess` (contract to SIS, MB-02), `ICurrentUser` (with `HasPermission`), `IFileStore` + `StorageOptions` (18.5, Appendix B "Storage"), `LmsErrors`, `KnownPermissions`, contracts with one validator each (18.2), `LmsProvisioner` (DEP-11) |
| Infrastructure | `LmsDbContext` (actor stamping, logical-deletion filter), entity configurations with the 13.4 indexes, `CourseContentRepository` (aggregate loaded in one split query; logical removal of the hierarchy), `LocalFileStore` (owner-derived layout, generated names, SHA-256 while writing, size bound during the write, path-escape refusal), migration `LmsEntities` |
| Host | `ContentController` (`api/v1/sections/{id}/content`, `api/v1/content/…`), `ResourcesController` (`api/v1/resources/{id}/download`), `MySectionsController` (`api/v1/me/sections/teaching` / `enrolled`); `SisSectionAccess` (host-side implementation of the LMS→SIS contract, MB-05); framework-level multipart and request-body limits from `Storage:MaxUploadSizeBytes`; no static-file middleware (SEC-24) |
| Client | `features/content` (`SectionContentPage` for both shells, `MySectionsPage`, `ContentApi`, `BlobSaver`), `features/instructor` routes, learner "My courses" routes; `shared/brand` (mark) and the token-based theme in `styles.scss` |

### Configuration (Appendix B)

| Section | Keys | Notes |
|---|---|---|
| `ConnectionStrings` | `OpenCampus` | Integrated security in the checked-in environment files; override by environment variable |
| `DataProtection` | `KeyDirectory` | Key ring for MfaSecret encryption and MFA challenges; `keys/` is excluded from source control |
| `Tokens` | `Issuer`, `Audience`, `AccessTokenLifetime` (≤ 15 min, SEC-03), `RefreshTokenLifetime` (≤ 14 days, SEC-05), `SigningKeyPath` (RSA PEM, created on first run, SEC-04), `MfaChallengeLifetime` | Validated at startup; an out-of-range value stops the host |
| `AccountProtection` | `LockoutThreshold`, `LockoutDuration` (SEC-14), `RateLimitWindow`, `RateLimitPermittedRequests` (SEC-16) | |
| `PasswordHashing` | `Iterations` (≥ 100 000; default 600 000) | Persisted with each hash so it can be raised later (SEC-01) |
| `Storage` | `RootPath` (relative to the content root or absolute; `data/files` by default, git-ignored, never served statically), `MaxUploadSizeBytes` (enforced by the framework body limits and again while writing), `PermittedExtensions` (allow-list, `.ext` form, case-insensitive) | Validated at startup (SEC-22, SEC-24, 18.5) |
| `Provisioning` | `ReferenceDataEnabled`, `DemonstrationDataEnabled`, `AdministratorUserName`, `AdministratorEmail`, `AdministratorPassword`, `DemonstrationPassword`, `CredentialsFilePath` | Passwords are optional and are generated when absent |
| `Serilog` | Minimum level, sinks, retention | |

## Recorded design decisions (section 1.4)

| # | Decision | Rationale |
|---|---|---|
| 1 | SQL Server Express **LocalDB** rather than a full Express instance | Section 7 permits either; LocalDB has the smallest footprint on a single workstation (CON-01). |
| 2 | Primary keys generated by `SharedKernel.SequentialGuid` | DC-01 requires application-generated sequential identifiers. `Guid.CreateVersion7()` places the timestamp in the leading bytes, but SQL Server orders `uniqueidentifier` by the trailing bytes, so v7 values fragment a clustered index. `SequentialGuid` places a monotonic counter in bytes 10–15 and 8–9 (the layout EF Core's own generator uses) and keeps the leading bytes random. |
| 3 | Audit stamping and the logical-deletion query filter are implemented in each module's `DbContext` | DC-02 and DC-03 must be applied centrally, but the section 12 reference table is exhaustive and permits no shared persistence project, and the shared kernel may not reference EF Core (LR-01). Each module therefore owns its persistence behaviour, which is also what the Appendix E extraction path requires. |
| 4 | Audit actor (`CreatedBy`/`ModifiedBy`) is stamped from the bearer token's `sub` claim via `ICurrentUser` | DC-02 requires central population; the host implements the abstraction declared in the application layer. Rows written by anonymous flows (a failed login) carry a null actor. |
| 5 | Solution file uses the `.slnx` format | Default for the .NET 10 SDK; tooling is identical. |
| 6 | Shouldly is the assertion library | FluentAssertions 8+ moved to a commercial licence (CON-02); Shouldly is BSD-3-Clause. |
| 7 | `Microsoft.AspNetCore.Hosting.Diagnostics` is logged at `Warning` in Development | Its "Request starting/finished" entries are emitted outside the middleware pipeline and therefore cannot carry the correlation identifier (NFR-13). The Serilog request-logging line provides the per-request summary with the identifier. |
| 8 | Migrations are applied automatically on startup only in `Development` and `Demonstration` | DEP-11/DEP-12 require a populated system on first run in those environments; other environments apply migrations explicitly. |
| 9 | The Angular dev server proxies `/api` to the host | Section 9.2 requires the client to share an origin with the API or use a development-time reverse proxy; no cross-origin policy is configured. |
| 10 | Client `shared/` area is created when the first reusable component belongs in it | Git does not track empty directories; the section 17.1 organisation is otherwise in place. |
| 11 | `UserSession` carries a `FamilyId` in addition to the 13.2 attributes | SEC-07 requires that replay of an invalidated refresh credential invalidate "the entire session family". Each rotation revokes the current row and inserts a successor sharing the family; revocation (logout, administrative, deactivation) applies to the family. |
| 12 | `AuditEvent` does not derive from `Entity`, and its `UserId` is not a foreign key | SEC-32 forbids modification or deletion; the type therefore carries no modification or logical-deletion state, is exposed read-only, and is never cascaded by account changes. |
| 13 | `UserRole` and `RolePermission` are plain join rows without audit columns | 13.2 specifies composite primary keys; the rows are immutable (created or removed, never modified) and every change is itself an audit event (`role.assigned`, `role.removed`). |
| 14 | `MfaSecret` is encrypted at rest with ASP.NET Core Data Protection through an EF value converter | 13.2 requires protection at rest; the key ring lives in the configured `keys/` directory (9.2). |
| 15 | The MFA enrolment response carries the shared secret and provisioning URI **once** | SEC-02 forbids returning an MFA secret; provisioning an authenticator is impossible without it. This is the sole, deliberate exception: the secret is returned only from `POST api/v1/auth/mfa/enrolment`, only before confirmation, and never again. |
| 16 | Default role → permission mappings are the implementation's (Appendix C leaves them to be provisioned) | Derived from the role descriptions of 2.3: Administrator holds the full catalogue; Registrar holds SIS read plus learner, enrolment and report capabilities; Instructor holds content, assessment, attendance and grading; Learner holds read and self-service capabilities. Instructor and Learner scope is further constrained per SEC-12 in later increments. |
| 17 | The refresh cookie is scoped to `Path=/api/v1/auth` | SEC-05 requires HttpOnly, Secure and SameSite=Strict; restricting the path additionally keeps the credential off every other request. |
| 18 | Bearer validation uses `ClockSkew = 0` | SEC-03 caps the lifetime at 15 minutes; the default 5-minute skew would extend it. |
| 19 | Every presentation of an invalidated refresh credential raises `session.reuse_detected` | SEC-07 literal reading; a legitimate successor presented after its family was revoked is also recorded, which is the evidence an operator needs. |
| 20 | Initial passwords are generated and written to `data/provisioning/credentials.txt` when not configured | 9.2 forbids credentials in source and SEC-02 forbids writing passwords to the log; DEP-11/DEP-12 require a usable system with no manual data entry. |
| 21 | Integration tests raise the auth rate limit on the shared host and lower PBKDF2 to its 100 000 floor | The suite would otherwise trip SEC-16 on itself; SEC-16 is verified on a separately configured host, and both values remain valid configurations. |
| 22 | Section-14 violations are raised by the aggregate as `BusinessRuleViolationException` carrying the rule reference and translated by the single handler to 422 with `title` = `BR-nn` | 11.3 places invariants in the aggregate; 18.3 requires one translation mechanism; API-07 requires rule violations to be distinguishable, and the reference lets a client and a tester tell BR-01 from BR-02. |
| 23 | The SIS→Identity contract (`IUserDirectory`) is implemented in the host project | MB-02 says "implemented within the providing module", but the section 12 reference table is exhaustive and gives Identity.Infrastructure no reference to Sis.Application. The host is the only project permitted to see both (MB-05); the adapter reads Identity solely through its published repository contract, never its schema (MB-01). |
| 24 | Delivery modes, section, enrolment and learner statuses and gender are enumerations declared in the domain and persisted as bounded strings, not lookup tables | 13.6 requires these sets to be provisioned rather than entered manually; they are fixed by the design (BR-03 names "Open", BR-12 names "At Risk") and participate in invariants, so a table would add a join and a foreign key without adding any editable data. They need no provisioning step and cannot drift from the code that enforces them. |
| 25 | Re-enrolment after withdrawal reinstates the existing `Enrolment` row | 13.3 specifies a unique index on (LearnerId, SectionId), so a second row for the pair is impossible. `Enrolment.Reinstate` applies the same preconditions as creation (BR-02, BR-03, BR-01) and resets `EnrolledAtUtc`. |
| 26 | Administrative enrolment listings require `sis.learner.read` in addition to `sis.enrolment.read` | Appendix C: a code grants capability, not scope. The Learner role holds `sis.enrolment.read` for its own records (`api/v1/me/enrolments`); without the second policy a learner could list any learner's enrolments through the administrative routes (SEC-12). Instructor access to assigned sections arrives with the SEC-12 handlers of Increment 4. |
| 27 | BR-16 (instructor session overlap) is implemented in Increment 3; BR-04 (weightings total 100 to Open) is not | Section 19 assigns scheduled session management to Increment 3 and BR-04 to Increment 5. Building session scheduling without its governing rule would leave a known-invalid state reachable; opening a section without a complete scheme is the state Increment 5 closes. |
| 28 | `NationalId` is returned only by single-learner retrieval, never in collection responses | 18.1 forbids logging national identifiers; keeping them out of paged listings limits their spread to the screen that needs them. |
| 29 | Cross-module references and logical deletion (13.5): a deactivated instructor or learner account keeps its identifier on `CourseSection.InstructorUserId` / `Learner.UserId` | Records retain their history; display resolves through `IUserDirectory` and shows the account as inactive (or unresolved if absent). New references to inactive accounts are refused at creation with a field-keyed 400. |
| 30 | The LMS→SIS contract (`ISectionAccess`) is declared in `Lms.Application` and implemented in the host (`SisSectionAccess`) over SIS repositories | Same reasoning as decision 23: the section 12 table gives no module a reference to another's application layer (MB-04), so the host is the only place the adapter can live (MB-05). It is the sole source of "is this user the assigned instructor?" and "does this user hold an active enrolment?" — the two facts SEC-12 needs — and is consulted on every request, never cached (18.4 forbids caching authorisation-relevant data). |
| 31 | Resource-level authorisation (SEC-12) is resolved per section into one of three scopes — Manager (assigned instructor, or holder of `sis.section.write`), EnrolledLearner, None — before any LMS data is touched; None is reported as 404 | A permission code grants capability, not scope (Appendix C). Routing the check through one resolver keeps every controller action on the same rule and lets unit tests prove the matrix without a database. 404 rather than 403 follows API-06: an outsider must not learn that a section has content. |
| 32 | Administrators manage content through the SIS capability `sis.section.write`, restated verbatim in `Lms.Application.KnownPermissions` | The LMS cannot reference the Identity permission catalogue (MB-04). "May administer sections" is the natural authority over a section's content; using an existing code avoids inventing one outside Appendix C. |
| 33 | BR-15 is enforced by filtering (learner views contain published items only) and, for a direct request, by answering 404 rather than 422 | 18.3 maps "not visible" to 404 and API-06 forbids disclosing existence; a 422 titled `BR-15` would confirm that an unpublished item exists. The aggregate still exposes `EnsureVisibleToLearner` (throws BR-15) so the rule has a unit-level refusal test (TST-03). |
| 34 | Content item types are the fixed enumeration Page, Link, File | 13.6 lists "content item types" as reference data without enumerating them; these three cover text, external and file-backed content. As with decision 24 they are stored by name and need no lookup table. A Link body must be an absolute `http(s)` address so no `javascript:` or `data:` scheme can be published (SEC-20). |
| 35 | Stored files are laid out as `content/{sectionId}/{itemId}/{guid}.{ext}` under `Storage:RootPath`; the database keeps the relative path, size, content type and SHA-256; the client name is kept for display only | 18.5 (deterministic hierarchy from the owning identifiers, relative path only, storage abstraction in the application layer), SEC-23 (system-generated names; the display name is reduced to its leaf so a hostile `..\..\x.pdf` cannot influence the path) and SEC-26 (hash recorded, returned as `X-Content-SHA256` on download). Files are deleted only after the database commit that removes their record, and an uncommitted upload deletes its file, so neither orphans nor dangling records arise. |
| 36 | The client is themed through Bootstrap's CSS custom properties from six neutral tokens in the single global stylesheet, with IBM Plex Sans / IBM Plex Sans Arabic self-hosted | 17.1 requires component-scoped styling with global overrides minimised and justified: mapping tokens onto `--bs-*` variables re-themes every component without per-component rules. Self-hosting the fonts keeps every asset same-origin (no third-party stylesheet or font request; simpler CSP under SEC-28) and makes the Arabic face available offline. |

## Anonymous endpoints (section 15.4)

A fallback policy requires an authenticated caller on every endpoint that carries no explicit declaration (SEC-10).
The endpoints below are the only ones marked `[AllowAnonymous]`; the first three are additionally rate-limited (SEC-16).

| Endpoint | Justification |
|---|---|
| `POST /api/v1/auth/login` | Credential authentication — listed in section 15.4. |
| `POST /api/v1/auth/mfa/verify` | Multi-factor verification — listed in section 15.4. |
| `POST /api/v1/auth/refresh` | Session refresh — listed in section 15.4. The credential travels only in the cookie. |
| `GET /api/health` | Health status — listed in section 15.4. |
| `GET /openapi/v1.json` | Machine-readable interface description (API-08). **Development environment only**; not mapped in any other environment. |

Certificate verification (section 15.4) is added in Increment 6.

### Interface summary (Increment 4)

| Route | Policy | Purpose |
|---|---|---|
| `GET api/v1/sections/{id}/content` | `lms.content.read` | The section's content hierarchy in the caller's scope (SEC-12): every item for managers, published items only for enrolled learners (BR-15); 404 otherwise |
| `POST api/v1/sections/{id}/content` | `lms.content.write` | New content unit (managers only) |
| `GET/PUT/DELETE api/v1/content/{id}` | `lms.content.read` / `lms.content.write` | Content unit retrieval, amendment, logical deletion (stored files removed after the commit) |
| `POST api/v1/content/{id}/items` · `PUT/DELETE {itemId}` · `PUT items/reorder` | `lms.content.write` | Content item management |
| `POST api/v1/content/{id}/items/{itemId}/publish` · `/unpublish` | `lms.content.publish` | Publication (422 for a file item without a resource) |
| `POST api/v1/content/{id}/items/{itemId}/resources` (multipart `file`) | `lms.content.write` | Resource upload (SEC-22 allow-list and size, field-keyed 400; SEC-23 generated name; SEC-26 hash); 201 with `Location` of the download |
| `DELETE api/v1/content/{id}/items/{itemId}/resources/{resourceId}` | `lms.content.write` | Resource removal (file deleted after the commit) |
| `GET api/v1/resources/{id}/download` | `lms.content.read` | Authorised download (SEC-25): entitlement verified before streaming; `Content-Disposition` carries the display name; `X-Content-SHA256` the digest; 404 outside the caller's scope or for an unpublished item requested by a learner |
| `GET api/v1/me/sections/teaching` | `lms.content.write` | Sections assigned to the caller as instructor |
| `GET api/v1/me/sections/enrolled` | `lms.content.read` | Sections in which the caller holds an active enrolment |

### Interface summary (Increment 3)

| Route | Policy | Purpose |
|---|---|---|
| `GET api/v1/programmes[/{id}]` · `POST` · `PUT {id}` · `DELETE {id}` | `sis.programme.read` / `sis.programme.write` | Programme listing (search, `isActive`, `sort`), creation, amendment, logical deletion (409 while courses exist) |
| `GET api/v1/courses[/{id}]` · `POST` · `PUT {id}` · `DELETE {id}` | `sis.course.read` / `sis.course.write` | Course listing (search, `programmeId`, `sort`), creation, amendment, deletion (409 while sections exist) |
| `GET api/v1/sections[/{id}]` · `POST` · `PUT {id}` · `DELETE {id}` | `sis.section.read` / `sis.section.write` | Section listing (search, `courseId`, `programmeId`, `status`, `deliveryMode`, `instructorUserId`, `sort`), creation, amendment, deletion (422 BR-14) |
| `POST api/v1/sections/{id}/open` · `/close` · `/cancel` | `sis.section.open` | State transitions |
| `POST/PUT/DELETE api/v1/sections/{id}/sessions[/{sessionId}]` | `sis.section.write` | Scheduled session management (422 BR-16) |
| `POST/PUT/DELETE api/v1/sections/{id}/grade-components[/{componentId}]` | `sis.section.write` | Grade scheme definition |
| `GET api/v1/sections/instructors` | `sis.section.write` | Instructor-role accounts available for assignment |
| `GET api/v1/sections/{id}/enrolments` | `sis.enrolment.read` + `sis.learner.read` | Enrolment listing by section |
| `GET api/v1/learners[/{id}]` · `POST` · `PUT {id}` | `sis.learner.read` / `sis.learner.write` | Learner listing (search by number or name, `status`, `sort`), creation for a Learner-role account, amendment |
| `GET api/v1/learners/{id}/enrolments` | `sis.enrolment.read` + `sis.learner.read` | Enrolment listing by learner |
| `GET api/v1/learners/{id}/transcript` | `sis.report.read` | Transcript |
| `GET api/v1/learners/unlinked-users` | `sis.learner.write` | Learner-role accounts without a record |
| `GET api/v1/enrolments[/{id}]` · `POST` · `DELETE {id}` | `sis.enrolment.read`/`write` + `sis.learner.read` | Administrative enrolment (422 BR-01/BR-02/BR-03) and withdrawal |
| `GET api/v1/catalogue[/{sectionId}]` | `sis.section.read` | Catalogue of Open sections (search, `programmeId`, `deliveryMode`, `sort`) |
| `GET/POST api/v1/me/enrolments` · `DELETE {id}` · `GET transcript` | `sis.enrolment.read` / `sis.enrolment.write` | Learner self-service, scoped to the caller's learner record (404 otherwise) |

### Interface summary (Increment 2)

| Route | Policy | Purpose |
|---|---|---|
| `POST api/v1/auth/logout`, `GET api/v1/auth/me`, `PUT api/v1/auth/password`, `POST api/v1/auth/mfa/enrolment`, `POST api/v1/auth/mfa/enrolment/confirm` | authenticated | Session termination, principal, password change, MFA enrolment |
| `GET api/v1/users`, `GET api/v1/users/{id}` | `identity.user.read` | Paged listing (search, `isActive`, `sort`), retrieval |
| `POST api/v1/users`, `PUT api/v1/users/{id}` | `identity.user.write` | Creation (201 + Location), amendment |
| `DELETE api/v1/users/{id}`, `POST api/v1/users/{id}/activate` | `identity.user.deactivate` | Logical deactivation (204), reactivation |
| `PUT api/v1/users/{id}/roles` | `identity.role.assign` | Replace the role set |
| `GET/DELETE api/v1/users/{id}/sessions[/{sessionId}]` | `identity.session.revoke` | Enumerate and revoke sessions |
| `GET api/v1/roles` | `identity.user.read` | Roles with their permission codes |
| `GET api/v1/audit` | `identity.audit.read` | Paged, filterable, read-only audit trail |

## Delivery status

Increment 1 — Foundation: complete. Increment 2 — Identity: complete. Increment 3 — Academic structure: complete.
Increment 4 — Content delivery: complete (section 13.4 entities; content management and resource handling; section 16.5
in full; SEC-12 resource-level authorisation for instructors and learners over the LMS→SIS contract; BR-15; instructor
and learner interfaces; visual identity). See `ACCEPTANCE.md` for the step log and `DEPENDENCIES.md` for the dependency
register (DEL-04).

Carried forward to later increments: BR-04 and the assessment SEC-12 cases (Increment 5), announcements (mandatory scope
not assigned to an increment by section 19; schema delivered), 18.4 reference-data caching (with the first cacheable read
path), 16.6 transport headers and the security test report (Increment 6), full localisation coverage per 17.4 (Increment 6).
