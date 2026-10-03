# VarinsEdu

VarinsEdu is a multi-institution school management PWA. It digitizes access control (cafeteria, exit permits, parking), visitor registration, vehicle registry and internal inventory, with reports and an audit log. First deployment: Liceo de Moravia (TCU project).

## Tech stack

- Frontend: React + Vite + TypeScript (PWA)
- Backend: ASP.NET Core
- Database: PostgreSQL
- Infrastructure: Docker Compose

## Repository structure

```text
backend/    ASP.NET Core API
frontend/   React PWA
docs/       Design documents
```

## Prerequisites

- Docker Desktop
- .NET SDK (latest LTS)
- Node.js (LTS)
- Git

## Getting started

1. Copy the environment template: `cp .env.example .env` (PowerShell: `Copy-Item .env.example .env`).
2. Start the database: `docker compose up -d`.
3. Check that it is healthy: `docker compose ps`.

## Conventions

- Branches: `main` plus short-lived `feature/*` branches merged through pull requests.
- Commits: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`, `test:`).

## Data privacy

Real student data must never be used in development or committed to this repository. Use fake seed data only.