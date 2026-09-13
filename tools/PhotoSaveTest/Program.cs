using System.Security.Claims;
using MedicalManager.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var dbPath = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Data", "app.db"));

var services = new ServiceCollection();
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={dbPath};Cache=Shared"));
services.AddIdentityCore<ApplicationUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
services.AddScoped<PatientAccessService>();
services.AddScoped<AuthenticationStateProvider, TestAuthStateProvider>();
services.AddScoped<ProfilePhotoService>();

await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();

var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var photos = scope.ServiceProvider.GetRequiredService<ProfilePhotoService>();
var auth = (TestAuthStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();

var patient = await users.FindByEmailAsync(IdentitySeeder.DemoPatientEmail)
    ?? throw new InvalidOperationException("Demo patient not found.");

auth.SetUser(new ClaimsPrincipal(new ClaimsIdentity([
    new Claim(ClaimTypes.NameIdentifier, patient.Id),
    new Claim(ClaimTypes.Name, patient.Email!),
    new Claim(ClaimTypes.Role, AppRoles.Patient)
], authenticationType: "test")));

var png = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

var (success, error, url) = await photos.SaveBytesAsync(null, png, "image/png", "test.png");
Console.WriteLine(success ? $"Saved: {url}" : $"Failed: {error}");

var loaded = await photos.GetPhotoDataAsync(patient.Id);
Console.WriteLine(loaded is null ? "Read back: missing" : $"Read back: {loaded.Value.Data.Length} bytes");

sealed class TestAuthStateProvider : AuthenticationStateProvider
{
    private ClaimsPrincipal _user = new(new ClaimsIdentity());

    public void SetUser(ClaimsPrincipal user) => _user = user;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_user));
}
