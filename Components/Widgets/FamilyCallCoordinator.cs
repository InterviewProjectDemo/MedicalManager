namespace MedicalManager.Components.Widgets;

public sealed class FamilyCallCoordinator
{
    public event Func<Task>? OpenFamilyCallRequested;
    public event Func<Task>? OpenPatientCallRequested;
    public event Func<Task>? OpenMonitorRequested;

    public Task RequestOpenAsync() =>
        OpenFamilyCallRequested?.Invoke() ?? Task.CompletedTask;

    public Task RequestPatientCallAsync() =>
        OpenPatientCallRequested?.Invoke() ?? Task.CompletedTask;

    public Task RequestMonitorAsync() =>
        OpenMonitorRequested?.Invoke() ?? Task.CompletedTask;
}
