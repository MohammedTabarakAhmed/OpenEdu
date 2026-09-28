# OpenEdu

**OpenEdu** is an academic eLearning platform. It brings a training institute's student
records, courses and online learning together in one web application.

## What it does

- **Accounts and security**: sign-in with optional two-factor authentication, roles for
  administrators, registrars, instructors and learners, and a full audit trail.
- **Student records**: programmes, courses, class sections and schedules, learners and
  enrolments.
- **Online learning**: course content, file resources, assignments, attendance and grading.
- **Certificates**: PDF certificates on completion, with public verification by code.
- **Reports**: participation and results.
- **Two languages**: English and Arabic, including right-to-left layout.

## Built with

.NET 10 (ASP.NET Core, Entity Framework Core), SQL Server, and Angular with Bootstrap.

## Quick start

```powershell
dotnet run --project src/Api/OpenCampus.Api
npm --prefix client install
npm --prefix client start
```

Then open <http://localhost:4200>. Prerequisites, configuration, architecture and the full
API are in [`BRIEFER.md`](BRIEFER.md).
