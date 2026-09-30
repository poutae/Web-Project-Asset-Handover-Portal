# Web Project Asset & Handover Portal

Multi-tenant portal where agencies manage client projects, assets, documentation, milestones and deployments.

Stack: C# 14 / .NET 10, ASP.NET Core Minimal APIs, EF Core 10, SQL Server; React 19, TypeScript, Vite, React Router, TanStack Query, Tailwind CSS; xUnit, Vitest, Playwright.

## Git workflow
`main` is protected: all changes go through feature/fix/chore/docs/test/ci branches and Pull Requests with passing CI.

## Decisions & Trade-offs
- **Authentication/authorization:** OAuth 2.0 (details pending).
