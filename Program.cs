using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MedicalManager.Components;
using MedicalManager.Components.Account;
using MedicalManager.Data;

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

var configuredConnection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
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
var connectionString = sqliteBuilder.ConnectionString;
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
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

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
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
builder.Services.AddScoped<MedicationResearchService>();
builder.Services.AddScoped<ProfilePhotoService>();

var app = builder.Build();
app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsLock\"");
    }
    catch (Microsoft.Data.Sqlite.SqliteException)
    {
        // Lock table may not exist yet on first run.
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
    app.UseHttpsRedirection();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.MapGet("/health", () => Results.Ok("ok"));

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
