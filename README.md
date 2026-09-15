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
reference data (roles, permission catalogue) and the demonstration identities, and — because no credential may be
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

### Configuration (Appendix B)

| Section | Keys | Notes |
|---|---|---|
| `ConnectionStrings` | `OpenCampus` | Integrated security in the checked-in environment files; override by environment variable |
| `DataProtection` | `KeyDirectory` | Key ring for MfaSecret encryption and MFA challenges; `keys/` is excluded from source control |
| `Tokens` | `Issuer`, `Audience`, `AccessTokenLifetime` (≤ 15 min, SEC-03), `RefreshTokenLifetime` (≤ 14 days, SEC-05), `SigningKeyPath` (RSA PEM, created on first run, SEC-04), `MfaChallengeLifetime` | Validated at startup; an out-of-range value stops the host |
| `AccountProtection` | `LockoutThreshold`, `LockoutDuration` (SEC-14), `RateLimitWindow`, `RateLimitPermittedRequests` (SEC-16) | |
| `PasswordHashing` | `Iterations` (≥ 100 000; default 600 000) | Persisted with each hash so it can be raised later (SEC-01) |
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

Increment 1 — Foundation: complete. Increment 2 — Identity: complete (sections 13.2 and 16.1–16.3 in full; authentication,
session and user administration interfaces; client session handling per 17.2). See `ACCEPTANCE.md` for the step log and
`DEPENDENCIES.md` for the dependency register (DEL-04).

Carried forward to later increments: SEC-12 resource-level authorisation (Increments 4–5), 16.5 file handling (Increment 4),
16.6 transport headers and the security test report (Increment 6), full localisation coverage per 17.4 (Increment 6).
