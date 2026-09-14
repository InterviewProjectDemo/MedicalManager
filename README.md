# MedicalManager

Blazor Web App (.NET 10, interactive server) for patients and doctors to track blood pressure, sugar readings, medications, and appointments.

## How to run

### Docker (app + PostgreSQL)

Two containers: Blazor app and a TLS-only PostgreSQL database. Full Windows steps: [DOCKER.md](DOCKER.md).

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\New-DockerEnv.ps1
docker compose up --build -d
```

Open [http://localhost:8080](http://localhost:8080). PHI fields are encrypted with AES-256-GCM in the app; the app-to-database connection requires TLS.

### AWS (ECS Fargate + RDS)

See [aws/README.md](aws/README.md). After `aws configure`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Deploy-Aws.ps1
```

### Local development (SQLite)

From this folder:

```bash
dotnet restore
dotnet run
```

Then open the HTTPS URL printed in the terminal (typically `https://localhost:7286`). Sign in — the dashboard is the home page after login.

You can also open `MedicalManager.slnx` in Visual Studio or Cursor and press F5.

SQLite is created and migrated on startup (`Data/app.db`). Demo users and sample clinical data are seeded automatically.

## Demo logins

| Role | Email | Password |
|------|-------|----------|
| Patient | `patient@medicalmanager.local` | `Patient123!` |
| Doctor | `doctor@medicalmanager.local` | `Doctor123!` |

Patients can also register a new account from **Register**. Doctor accounts are seeded (not self-registered).

## What was built

- **ASP.NET Core Identity** with roles `Patient` and `Doctor`
- **SQLite + EF Core** for local development; **PostgreSQL** in Docker
- **AES-256-GCM** application encryption for names, phones, notes, photos, and other PHI
- **TLS** between the app container and the database container
- Clinical records linked to the Identity user, plus a `PatientProfile`
- Color-coded medical UI (teal / calm blue, green / amber / red status badges)

### Pages

| Page | Route | Who | What it does |
|------|-------|-----|----------------|
| Login | `/Account/Login` | Anyone | Sign in; demo credentials shown |
| Register | `/Account/Register` | Anyone | Creates a **Patient** account |
| Dashboard | `/` | Signed-in | Charts and widgets only (BP, sugar, appointments, medications) |
| Blood pressure | `/blood-pressure` | Signed-in | List, add, update readings |
| Sugar | `/sugar` | Signed-in | List, add, update glucose (fasting / after meal / random) |
| Medications | `/medications` | Signed-in | List, add, update name, dosage, frequency, notes |
| Appointments | `/appointments` | Signed-in | List, add, update title, with whom, start/end, location, notes, status |
| Patients | `/patients` | **Doctor** | Pick a patient, then the same pages scoped to that patient |

Doctors without a selected patient are sent to the patient picker. Patients can only open their own data. Navigation keeps the selected patient in the query string so doctors can move between pages without losing context.

### Domain model

- `ApplicationUser` — Identity user (`FullName`, role)
- `PatientProfile` — extra patient details (DOB, phone, notes)
- `BloodPressureReading` — systolic / diastolic, optional pulse, date/time
- `SugarReading` — value, type (fasting / after meal / etc.), date/time
- `Medication` — name, dosage, frequency, notes
- `Appointment` — title, with whom, start/end, location, notes, status
