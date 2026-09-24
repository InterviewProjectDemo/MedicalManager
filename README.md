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

### Email (welcome + password reset)

Registration sends a welcome email; forgot-password sends a reset link. Both come from `medmgr.us@gmail.com` via Gmail SMTP (`smtp.gmail.com:587`, STARTTLS).

1. Enable 2-Step Verification on the Gmail account.
2. Create an [App Password](https://support.google.com/accounts/answer/185833) for Medical Manager.
3. Configure locally (pick one):
   - User secrets: `dotnet user-secrets set "Email:Smtp:Password" "your-app-password"`
   - Environment: `Email__Smtp__Password=your-app-password` (or add `EMAIL_SMTP_PASSWORD` to `.env` for Docker)

If no password is configured in Development, emails are not sent; the app logs a warning and writes the plain-text body to the console. Production (ECS/Docker) should always set the password — see [aws/README.md](aws/README.md).

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
| Appointments | `/appointments` | Signed-in | List, add, update title, purpose, with whom, start/end, location, notes, status. One day before a visit, the patient gets a reminder call and a text. One hour before, they get another call. |
| Patients | `/patients` | **Doctor** | Pick a patient, then the same pages scoped to that patient |

Doctors without a selected patient are sent to the patient picker. Patients can only open their own data. Navigation keeps the selected patient in the query string so doctors can move between pages without losing context.

### Domain model

- `ApplicationUser` — Identity user (`FullName`, role)
- `PatientProfile` — extra patient details (DOB, phone, home address, name pronunciation, notes)
- `BloodPressureReading` — systolic / diastolic, optional pulse, date/time
- `SugarReading` — value, type (fasting / after meal / etc.), date/time
- `Medication` — name, dosage, frequency, notes
- `Appointment` — title, purpose, with whom, start/end, location, notes, status

### Appointment reminders

Scheduled visits trigger two reminders when a Twilio phone number is configured (`TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_FROM_NUMBER`):

- **One day before:** a phone call and a text message.
- **One hour before:** a phone call.

The call uses a warm voice, says the patient's first and last name (or the pronunciation saved on the profile), then the day, time, purpose, who they are seeing, and the location. If the profile has a home address and the visit location can be mapped, the call and text also include the drive time and distance. Patients can turn reminders off from the profile page. The phone number comes from the patient profile, then the account phone.
