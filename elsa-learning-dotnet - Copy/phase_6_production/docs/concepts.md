# Phase 6 — Production: Concepts

Covers: `level_10_security`, `level_11_production_ready`

## 1. What this phase is about

Everything so far ran on one machine with a permissive setup. Production adds three questions: **who may call the API** (authentication and authorization), **where state lives** (a real database), and **what changes when more than one node runs** (locking, caching, scheduling). The goal is not to become an operator overnight but to know what each production concern requires so nothing surprises you.

## 2. Key concepts

| Term | Meaning | Where you see it |
|------|---------|------------------|
| Elsa Identity | Built-in users, roles and applications with JWT tokens | `.UseIdentity(...)`, `.UseDefaultAuthentication()` |
| JWT bearer | Token-based login used by Studio (ElsaIdentity provider) | `Authorization: Bearer …` |
| API key | Machine-to-machine credential tied to an Elsa Identity *application* | `Authorization: ApiKey <key>` |
| Role / permission | Roles map to `permissions` claims that gate API endpoints | Role definition in identity config |
| Secrets management | Keys, passwords and signing keys kept out of source control | user-secrets, environment variables, vault |
| CORS | Which browser origins may call the API | `AddCors(...)` |
| Production persistence | A server database (e.g. PostgreSQL) instead of a local SQLite file | `ef.UsePostgreSql(...)`-style provider switch |
| Distributed runtime | Runtime mode for several nodes sharing state | Distributed hosting guide |
| Distributed lock | Ensures only one node resumes a given bookmark / processes a given job | `IDistributedLockProvider` |
| Distributed cache | Keeps definition caches consistent across nodes | Cache invalidation via a message bus |
| Clustered scheduler | Ensures a timer fires once, not once per node | Quartz.NET clustering |
| Health checks | Liveness/readiness probes for orchestrators | Opt-in probes in recent releases |

## 3. How the pieces fit together

```
 Browser (Studio) ──JWT──┐
 MVC app (API key) ──────┼──► Elsa API  ── permissions check ──► engine
 Other service (API key)─┘                       │
                                                 ├─ PostgreSQL (definitions, instances, bookmarks)
 Node A ─┐                                       ├─ Lock provider (DB / Redis / blob lease)
 Node B ─┼── shared state + locks + cache ───────┤
 Node C ─┘                                       └─ Clustered scheduler (timers fire once)
```

If every node shares the same database but the lock provider is local to each node, two nodes can resume the same bookmark. That is why shared storage *and* a shared lock provider go together.

## 4. Worked example

**Level 10.** You define an Elsa Identity application with a limited role and an API key. The MVC app calls the Elsa API with `ApiKey …` from configuration (not from source control), CORS lists exactly the Studio origin, and the signing key and admin password come from secrets. A call without a key is rejected, and a key without the needed permission is refused.

**Level 11.** Persistence moves from SQLite to PostgreSQL (started by a small `docker-compose.yml` that exists only in this level). You then read through the distributed-hosting requirements — distributed runtime, shared lock provider, cache invalidation, clustered scheduling — and add health checks and structured logging. Running several nodes is explored conceptually; a working cluster is optional.

## 5. Common pitfalls

- **`AllowAnyOrigin()` in production.** Fine for a lab; use exact origins for real deployments.
- **Default or hard-coded credentials.** In recent releases default admin credentials are development-only; outside Development, username, password and API key must be configured explicitly.
- **Raw API keys in config.** Store hashed keys and salts where the docs say so, and plan rotation before issuing a key.
- **Local filesystem lock in a cluster.** The default lock provider is for development and single-node use; clusters need a shared provider (database, Redis, blob lease).
- **Scheduled work firing on every node.** Without clustered scheduling, a timer on three nodes can send three reminders.
- **Migrating data casually.** Moving from SQLite to PostgreSQL does not carry existing instances over; treat it as a fresh start in the course.
- **Conflicting doc statements.** One authentication page describes API keys as something you implement yourself, while the API-key page describes them as built into Elsa Identity with `UseDefaultAuthentication()`. Check the page for your Elsa version before relying on either.

## 6. Self-check questions

1. Why does Studio use JWT while a service-to-service call may use an API key?
2. What does a role grant, concretely?
3. Why is a shared database not enough for multi-node safety?
4. Which production setting changes the most when you go from one node to three?
5. Where should the signing key and API keys live?

**Answers.** (1) Studio is an interactive user session; API keys suit non-interactive callers. (2) Permissions, emitted as `permissions` claims that API endpoints check. (3) Without a shared lock provider two nodes can act on the same bookmark; scheduling and caches also need coordination. (4) The lock provider, plus the scheduler and cache invalidation. (5) Outside source control: secrets store, environment variables or a vault.

## 7. Official docs to read next

- Security: https://docs.elsaworkflows.io/guides/security
- Authentication & authorization: https://docs.elsaworkflows.io/guides/authentication
- API keys: https://docs.elsaworkflows.io/guides/authentication/api-keys
- Production hardening: https://docs.elsaworkflows.io/guides/security/production-hardening
- Distributed hosting: https://docs.elsaworkflows.io/hosting/distributed-hosting
- Clustering: https://docs.elsaworkflows.io/guides/clustering
- Runtime coordination storage: https://docs.elsaworkflows.io/guides/architecture/runtime-coordination-storage
- Configuration management: https://docs.elsaworkflows.io/guides/deployment/configuration-management
