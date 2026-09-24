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
    notificationOptions.Twilio.AccountSid = FirstNonEmpty(
        notificationOptions.Twilio.AccountSid,
        Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID"));
    notificationOptions.Twilio.AuthToken = FirstNonEmpty(
        notificationOptions.Twilio.AuthToken,
        Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN"));
    notificationOptions.Twilio.FromNumber = FirstNonEmpty(
        notificationOptions.Twilio.FromNumber,
        Environment.GetEnvironmentVariable("TWILIO_FROM_NUMBER"));
});
builder.Services.AddHttpClient("Twilio", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddHttpClient("Travel", client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MedicalManager/1.0 (appointment-reminders)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<OnboardingVoiceService>();
builder.Services.AddSingleton<TravelTimeService>();
builder.Services.AddSingleton<TwilioNotificationSender>();
builder.Services.AddScoped<AppointmentReminderService>();
builder.Services.AddHostedService<AppointmentReminderWorker>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    NameSpeaker.SelfCheck();
    AppointmentReminderCopy.SelfCheck();
    AppointmentReminderSchedule.SelfCheck();
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

app.Run();

static string FirstNonEmpty(string? configured, string? fromEnvironment) =>
    !string.IsNullOrWhiteSpace(configured) ? configured.Trim()
    : !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment.Trim()
    : "";
