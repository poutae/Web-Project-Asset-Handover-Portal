# Web Project Asset & Handover Portal

Multi-tenant portal where agencies manage client projects, assets, documentation, milestones and deployments, and hand them over to clients.

## Technology stack
- **Backend:** C# 14, .NET 10, ASP.NET Core Minimal APIs, EF Core 10, SQL Server, FluentValidation
- **Frontend:** React 19, TypeScript, Vite, React Router, TanStack Query, Tailwind CSS
- **Tests:** xUnit, Vitest, Playwright

## Repository layout
```
src/Portal.Api            ASP.NET Core Minimal API host
src/Portal.Domain         Entities and domain rules
src/Portal.Infrastructure EF Core, storage, queue, providers
tests/Portal.Api.Tests    xUnit tests
web/                      React app
scripts/                  PowerShell helper scripts
```

## Local setup (Windows)
Prerequisites: .NET 10 SDK, Node 22+, a local Microsoft SQL Server (the default `.env.example` uses Windows authentication against `localhost`).
```powershell
./scripts/setup.ps1   # creates .env from .env.example, restores packages
./scripts/db-update.ps1  # applies EF Core migrations to the database in .env
npm run dev           # API on :5080, web on :5173 (proxies /api)
```
Edit `.env` (never committed) and set `ConnectionStrings__Default` to your SQL Server instance. All configuration uses .NET environment-variable names (`ConnectionStrings__Default`, `Portal__PublicBaseUrl`, `Portal__MaxUploadMb`, `Deployment__Enabled`, `Storage__Provider`). Never put secrets in `VITE_*` variables.

Useful commands: `npm run build`, `npm test`, `npm run lint`, `npm run format:check`, `./scripts/check-secrets.ps1`.

## Authentication & OAuth 2.0
- `POST /api/auth/signup` creates a user plus a new organization (the user becomes its Owner) and signs in; `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me`.
- Passwords are hashed with ASP.NET Core's `PasswordHasher` (PBKDF2). Sessions use an HttpOnly, SameSite=Lax cookie (`Secure` outside Development). Nothing is stored in localStorage.
- The portal is an OAuth 2.0 / OpenID Connect server (OpenIddict): `/connect/authorize` and `/connect/token`, authorization-code flow with mandatory PKCE, plus refresh tokens. The first-party client `portal-web` is registered at startup. API endpoints accept either the session cookie or an OAuth bearer token.
- Outside Development, set `Oidc__SigningCertificatePath` / `Oidc__EncryptionCertificatePath` (+ passwords) to PFX files kept outside the repo; the app refuses to start without them. In Development, keys are ephemeral, so tokens are invalidated on restart.
- Auth endpoints are rate limited per IP (`Portal__Auth__RateLimitPerMinute`, default 20).

## Testing
`dotnet test` needs a SQL Server: each run creates and drops a throw-away `PortalTests_*` database. By default it uses `Server=localhost;Trusted_Connection=True`; set `PORTAL_TEST_SQL` (connection string without a database) to override. CI uses a SQL Server service container.

## Git workflow
`main` is protected. Every change: branch (`feature/`, `fix/`, `chore/`, `docs/`, `test/`, `ci/`) → Conventional Commits → push → Pull Request → CI → approval → merge → delete branch. Never commit to `main` directly (the single bootstrap commit is the only exception).

## Decisions & Trade-offs
- **Layered monorepo** (Api / Domain / Infrastructure / web). Simple enough for one team, clear boundaries for growth.
- **Authentication/authorization:** the portal is its own **OAuth 2.0 / OpenID Connect server** (planned: OpenIddict, authorization-code + PKCE for the SPA, same-site secure cookies, no tokens in localStorage). Trade-off: more to build and secure than delegating to an external provider, but the portal controls identity and can issue tokens to future integrations.
- **Multi-tenancy:** shared database, `OrganizationId` on tenant-owned rows, EF global query filters, plus server-side membership checks on every endpoint.
- **Queue/workers:** SQL Server-backed job table (claimed with `UPDLOCK, READPAST`), processed by a separate worker process. Needs no extra infrastructure and is safe with multiple instances; replaceable behind an interface (e.g. RabbitMQ) later.
- **Realtime:** Server-Sent Events behind an `IEventBus`, initially SQL-backed so multiple API instances work; Redis can replace it later.
- **Files:** `IFileStorage` abstraction; local disk provider for development, S3-compatible storage for production.
- **Secrets in project environments:** encrypted at rest with ASP.NET Data Protection; never returned to unauthorized users.
- **Docker is not used** (owner's decision, to keep the project simple). Best practice for running untrusted client builds would be rootless containers on a dedicated build host. Without Docker, the **isolation technology for deployment workers is still open** and will be decided before any deployment execution is implemented (likely a separate worker machine/VM with a low-privilege OS user and per-job workspaces). Deployment execution will not be faked in the meantime.
- **Database:** developers use their own local SQL Server. CI integration tests will use SQL Server provided by GitHub Actions.

## Known limitations
Early stage: database, organizations/users/memberships and authentication exist. There is no login UI yet (the authorize endpoint redirects to `/login`, which the React app will provide), no invitations, and tenancy query filters, projects, files, realtime, offline sync and deployments are not implemented yet.
