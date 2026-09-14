# Run MedicalManager with Docker on Windows

This stack is two containers:

| Container | Image | Role |
|-----------|--------|------|
| `medicalmanager-app` | .NET 10 ASP.NET (Blazor Server) | Web application |
| `medicalmanager-db` | PostgreSQL 16 | Database (TLS required) |

The app never talks to Postgres over the public internet. Traffic stays on the Compose network and **must use TLS** (`SSL Mode=Require`). Patient-identifying fields are also encrypted in the application with **AES-256-GCM** before they are written, and decrypted when they are read.

## 1. Install Docker Desktop for Windows

1. Install [Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/).
2. During setup, enable the **WSL 2** backend (recommended).
3. Start Docker Desktop and wait until it says **Docker Desktop is running**.
4. Open **PowerShell** and confirm:

```powershell
docker version
docker compose version
```

Both commands should print a version. If they fail, start Docker Desktop and retry.

## 2. Open the project folder

```powershell
cd C:\Users\navee\MedicalManager
```

Use your actual project path if it is different.

## 3. Create secrets (once)

This writes a private `.env` file with a random Postgres password and a 32-byte AES key.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\New-DockerEnv.ps1
```

- Do not commit `.env`.
- Do not reuse the development key from `PhiProtector` in production.
- If you already have a `.env`, the script leaves it alone.

To create the file by hand, copy `.env.example` to `.env` and replace the password and key. A 32-byte key as Base64:

```powershell
$bytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
[Convert]::ToBase64String($bytes)
```

## 4. Build and start both containers

```powershell
docker compose up --build -d
```

The first build downloads the .NET 10 and PostgreSQL images and can take several minutes. Later starts are faster.

Watch startup:

```powershell
docker compose ps
docker compose logs -f app
```

Wait until the app log shows the Kestrel listening message and `docker compose ps` shows both services as healthy.

## 5. Open the application

In a browser on the same Windows machine:

[http://localhost:8080](http://localhost:8080)

Demo logins (seeded automatically):

| Role | Email | Password |
|------|--------|----------|
| Patient | `patient@medicalmanager.local` | `Patient123!` |
| Doctor | `doctor@medicalmanager.local` | `Doctor123!` |

Confirm field encryption and the database TLS path:

[http://localhost:8080/health/encryption](http://localhost:8080/health/encryption)

You should see `"status":"ok"`, `"algorithm":"AES-256-GCM"`, and `"databaseTls":true`.

## 6. Everyday commands

```powershell
docker compose up -d
docker compose stop
docker compose down
docker compose logs -f
docker compose ps
```

`docker compose down` stops containers but **keeps** the Postgres and app volumes (clinical data and Data Protection keys).

To delete stored data as well (irreversible):

```powershell
docker compose down -v
```

## 7. What the images do

**Application (`Dockerfile`)**

- Multi-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` then `mcr.microsoft.com/dotnet/aspnet:10.0`
- `PublishReadyToRun` for faster startup
- Listens on port 8080
- Connection string is injected by Compose and requires TLS to `db`
- `MEDICALMANAGER_PHI_KEY` is the AES-256 key used by `PhiProtector`

**Database (`docker/postgres`)**

- PostgreSQL 16
- TLS 1.2+ with a container-generated server certificate
- `pg_hba.conf` rejects non-TLS remote connections
- Memory settings sized for a typical Windows Docker Desktop VM
- Port 5432 is **not** published to Windows. Only the app container can reach it.

Local `dotnet run` without Docker still uses SQLite (`Data/app.db`) from `appsettings.json`.

## 8. Windows notes

- Keep Docker Desktop set to **Linux containers**.
- If port 8080 is already in use, change the left side in `docker-compose.yml` (`"8081:8080"`).
- Antivirus or VPN tools sometimes block the Docker subnet. Pause them if containers cannot talk to each other.
- After you change `.env`, recreate: `docker compose up -d --force-recreate`.
- Changing `MEDICALMANAGER_PHI_KEY` after data exists makes existing ciphertext unreadable. Back up the key.
- For anything beyond localhost, put a reverse proxy with HTTPS in front of port 8080. This compose file is two services only, so the browser connection to the app is HTTP on your machine.

## 9. Performance choices

- PostgreSQL connection pooling (`Maximum Pool Size=50`)
- Retry on transient DB errors
- ReadyToRun publish
- Postgres `shared_buffers` / `work_mem` / WAL compression
- Numeric vitals stay queryable for charts; notes, names, phones, photos, and similar PHI are encrypted at rest
