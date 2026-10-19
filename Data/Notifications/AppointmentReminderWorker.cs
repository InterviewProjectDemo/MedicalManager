using Microsoft.Extensions.Options;

namespace MedicalManager.Data.Notifications;

public sealed class AppointmentReminderWorker(
    IServiceScopeFactory scopes,
    IOptions<NotificationOptions> options,
    ILogger<AppointmentReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var appointments = scope.ServiceProvider.GetRequiredService<AppointmentReminderService>();
                await appointments.ProcessDueAsync(stoppingToken);
                var medications = scope.ServiceProvider.GetRequiredService<MedicationReminderService>();
                await medications.ProcessDueAsync(stoppingToken);
                var todos = scope.ServiceProvider.GetRequiredService<ToDoReminderService>();
                await todos.ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Reminder check failed.");
            }

            var seconds = Math.Clamp(options.Value.PollIntervalSeconds, 15, 300);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
