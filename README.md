# LMS Starter for Blazor

This starter project provides a simple foundation for a real estate certification LMS built with Blazor.

## What is included
- A Blazor web app project structure
- A domain model for courses, modules, and lessons
- A simple in-memory course service
- Starter pages for home, courses, course detail, admin, instructor, and broker areas

## Project structure
- [src/Lms.Web](src/Lms.Web)
- [src/Lms.Application](src/Lms.Application)
- [src/Lms.Domain](src/Lms.Domain)

## Next steps
1. Add real persistence with EF Core and a database.
2. Replace the in-memory service with a database-backed service.
3. Implement authentication and role-based authorization.
4. Build lesson progress, quizzes, certificate issuance, and reporting flows.

## Product roadmap options
- Business-priority variants roadmap: [src/docs/business-priority-variants-roadmap.md](src/docs/business-priority-variants-roadmap.md)
- SSO provider profiles: [src/docs/sso-provider-profiles.md](src/docs/sso-provider-profiles.md)
