using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Components;
using MedicalManager.Components.Account;
using MedicalManager.Data;
using MedicalManager.Data.Notifications;
using MedicalManager.Data.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var dataDirectory = Environment.GetEnvironmentVariable("MEDICALMANAGER_DATA_DIR");
if (string.IsNullOrWhiteSpace(dataDirectory))
{
    dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
}

Directory.CreateDirectory(dataDirectory);
var keysDirectory = Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory))
    .SetApplicationName("MedicalManager");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IPhiProtector, PhiProtector>();

var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var usePostgreSql = configuredConnection.Contains("Host=", StringComparison.OrdinalIgnoreCase);

Action<DbContextOptionsBuilder> configureDb = options =>
{
    if (usePostgreSql)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        options.UseNpgsql(configuredConnection, npgsql =>
        {
            npgsql.EnableRetryOnFailure(5);
            npgsql.CommandTimeout(30);
        });
    }
    else
    {
        var sqliteBuilder = new SqliteConnectionStringBuilder(configuredConnection);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MEDICALMANAGER_DATA_DIR")))
        {
            sqliteBuilder.DataSource = Path.Combine(dataDirectory, "app.db");
        }
        else if (!Path.IsPathRooted(sqliteBuilder.DataSource))
        {
            sqliteBuilder.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqliteBuilder.DataSource);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(sqliteBuilder.DataSource)!);
        options.UseSqlite(sqliteBuilder.ConnectionString);
    }

    if (builder.Environment.IsDevelopment())
    {
        options.ConfigureWarnings(w =>
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    }
};

// Factory for Blazor Server page/layout queries that run in parallel on one circuit.
builder.Services.AddDbContextFactory<ApplicationDbContext>(configureDb);
builder.Services.AddDbContext<ApplicationDbContext>(configureDb, optionsLifetime: ServiceLifetime.Singleton);
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>>();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<IAppEmailSender>(sp => sp.GetRequiredService<SmtpEmailSender>());
builder.Services.AddSingleton<IEmailSender<ApplicationUser>>(sp => sp.GetRequiredService<SmtpEmailSender>());
builder.Services.AddScoped<HealthRecordService>();
builder.Services.AddScoped<DoctorService>();
builder.Services.AddScoped<MedicationScheduleService>();
builder.Services.AddScoped<PatientAccessService>();
builder.Services.AddScoped<LabReportService>();
builder.Services.AddScoped<LabEditAuthorizationService>();
builder.Services.AddHttpClient("OpenFda", client =>
{
    client.BaseAddress = new Uri("https://api.fda.gov/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.Configure<ResourceSearchOptions>(
    builder.Configuration.GetSection(ResourceSearchOptions.SectionName));
builder.Services.AddHttpClient("ResourceSearch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MedicalManager/1.0 (patient-education)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json, application/xml, text/plain");
});
builder.Services.AddScoped<MedicationResearchService>();
builder.Services.AddScoped<ResourceSearchService>();
builder.Services.AddScoped<ProfilePhotoService>();
builder.Services.AddScoped<DashboardLayoutService>();

builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SectionName));
builder.Services.PostConfigure<NotificationOptions>(notificationOptions =>
{
    notificationOptions.Pinpoint.Region = FirstNonEmpty(
        notificationOptions.Pinpoint.Region,
        Environment.GetEnvironmentVariable("AWS_REGION"),
        Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION"),
        "us-east-1");
    notificationOptions.Pinpoint.AccessKeyId = FirstNonEmpty(
        notificationOptions.Pinpoint.AccessKeyId,
        Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID"));
    notificationOptions.Pinpoint.SecretAccessKey = FirstNonEmpty(
        notificationOptions.Pinpoint.SecretAccessKey,
        Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));
    notificationOptions.Pinpoint.OriginationIdentity = FirstNonEmpty(
        notificationOptions.Pinpoint.OriginationIdentity,
        Environment.GetEnvironmentVariable("PINPOINT_ORIGINATION_IDENTITY"));
    notificationOptions.Twilio.AccountSid = FirstNonEmpty(
        notificationOptions.Twilio.AccountSid,
        Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID"));
    notificationOptions.Twilio.ApiKeySid = FirstNonEmpty(
        notificationOptions.Twilio.ApiKeySid,
        Environment.GetEnvironmentVariable("TWILIO_API_KEY_SID"));
    notificationOptions.Twilio.ApiKeySecret = FirstNonEmpty(
        notificationOptions.Twilio.ApiKeySecret,
        Environment.GetEnvironmentVariable("TWILIO_API_KEY_SECRET"));
    notificationOptions.Twilio.FromNumber = FirstNonEmpty(
        notificationOptions.Twilio.FromNumber,
        Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER"));
    if (builder.Environment.IsProduction()
        && !string.Equals(Environment.GetEnvironmentVariable("MEDICALMANAGER_TRAVEL_LOOKUP"), "true", StringComparison.OrdinalIgnoreCase))
    {
        notificationOptions.Travel.Enabled = false;
    }
});
builder.Services.AddHttpClient("Travel", client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MedicalManager/1.0 (appointment-reminders)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddHttpClient("Twilio", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<OnboardingVoiceService>();
builder.Services.AddSingleton<TravelTimeService>();
builder.Services.AddSingleton<TwilioNotificationSender>();
builder.Services.AddSingleton<FamilyVoiceCallService>();
builder.Services.AddScoped<AppointmentReminderService>();
builder.Services.AddScoped<MedicationReminderService>();
builder.Services.AddScoped<ToDoReminderService>();
builder.Services.AddHostedService<AppointmentReminderWorker>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    NameSpeaker.SelfCheck();
    AppointmentReminderCopy.SelfCheck();
    AppointmentReminderSchedule.SelfCheck();
    MedicationReminderCopy.SelfCheck();
    ToDoReminderCopy.SelfCheck();
    QuickCallCopy.SelfCheck();
    _ = Task.Run(async () =>
    {
        var voice = app.Services.GetRequiredService<OnboardingVoiceService>();
        var sample = await voice.SynthesizeAsync("Hello. I'm glad you're here.");
        var voiceLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OnboardingVoice");
        if (sample is { Length: > 200 })
            voiceLog.LogInformation("Human onboarding voice is ready ({Bytes} bytes).", sample.Length);
        else
            voiceLog.LogWarning("Human onboarding voice check did not return audio.");
    });
}
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=(self)";
    if (!app.Environment.IsDevelopment()
        && !context.Request.Path.StartsWithSegments("/health")
        && !context.Request.IsHttps)
    {
        var httpsUrl = $"https://{context.Request.Host}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        context.Response.Redirect(httpsUrl, permanent: true);
        return;
    }

    await next();
});

app.Services.GetRequiredService<IPhiProtector>().SelfTest();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\"");
    }
    catch (Exception)
    {
        // Lock table may not exist yet on first run (SQLite or PostgreSQL).
    }
}

await IdentitySeeder.SeedAsync(app.Services);

if (app.Environment.IsDevelopment() && !usePostgreSql)
{
    using var verifyScope = app.Services.CreateScope();
    var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var patient = await verifyDb.Users.FirstOrDefaultAsync(user => user.Email == IdentitySeeder.DemoPatientEmail);
    if (patient is null)
        throw new InvalidOperationException("Account email lookup failed after field encryption.");

    var connection = verifyDb.Database.GetDbConnection();
    await connection.OpenAsync();
    try
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """SELECT "Email", "FullName" FROM "AspNetUsers" WHERE "Id" = @id""";
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = patient.Id;
        command.Parameters.Add(id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Encrypted account row was not readable.");
        var storedEmail = reader.GetString(0);
        var storedName = reader.GetString(1);
        if (!storedEmail.StartsWith(PhiProtector.DeterministicPrefix, StringComparison.Ordinal)
            || !storedName.StartsWith(PhiProtector.StringPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Account identifiers are still plaintext in the database.");
    }
    finally
    {
        await connection.CloseAsync();
    }

    var reading = await verifyDb.BloodPressureReadings.OrderBy(x => x.Id).FirstOrDefaultAsync();
    if (reading is null || reading.Systolic < 60)
        throw new InvalidOperationException("Encrypted blood pressure reading did not round-trip.");
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.MapPost("/onboarding-speech", async (
    OnboardingSpeechRequest request,
    OnboardingVoiceService voice,
    CancellationToken cancellationToken) =>
{
    var audio = await voice.SynthesizeAsync(request.Text, cancellationToken);
    return audio is null
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.File(audio, "audio/mpeg");
}).RequireAuthorization().DisableAntiforgery();

app.MapPost("/voice/family", async (HttpRequest request, FamilyVoiceCallService voice) =>
{
    var ticket = request.HasFormContentType
        ? (await request.ReadFormAsync())["ticket"].ToString()
        : "";
    return Results.Text(voice.ClaimTwiml(ticket), "text/xml");
}).DisableAntiforgery();

app.MapGet("/health", () => Results.Ok("ok"));
app.MapGet("/health/encryption", (IPhiProtector protector) =>
{
    protector.SelfTest();
    return Results.Ok(new
    {
        status = "ok",
        algorithm = "AES-256-GCM",
        fieldEncryption = true,
        databaseTls = usePostgreSql
    });
});

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGet("/profile-photo/{userId}", async (
    string userId,
    HttpContext context,
    ProfilePhotoService photos) =>
{
    var currentUserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(currentUserId))
        return Results.Unauthorized();

    var isSelf = string.Equals(currentUserId, userId, StringComparison.Ordinal);
    var isDoctor = context.User.IsInRole(AppRoles.Doctor);
    if (!isSelf && !isDoctor)
        return Results.Forbid();

    var photo = await photos.GetPhotoDataAsync(userId);
    if (photo is null)
        return Results.NotFound();

    return Results.File(photo.Value.Data, photo.Value.ContentType);
}).RequireAuthorization();

app.MapGet("/family-photo/{userId}", async (
    string userId,
    HttpContext context,
    ProfilePhotoService photos) =>
{
    var currentUserId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrWhiteSpace(currentUserId))
        return Results.Unauthorized();

    var isSelf = string.Equals(currentUserId, userId, StringComparison.Ordinal);
    var isDoctor = context.User.IsInRole(AppRoles.Doctor);
    if (!isSelf && !isDoctor)
        return Results.Forbid();

    var photo = await photos.GetFamilyPhotoDataAsync(userId);
    if (photo is null)
        return Results.NotFound();

    return Results.File(photo.Value.Data, photo.Value.ContentType);
}).RequireAuthorization();

app.Run();

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }

    return "";
}
