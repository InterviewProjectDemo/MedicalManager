namespace MedicalManager.Components.Widgets;

public sealed class FamilyCallCoordinator
{
    public event Func<Task>? OpenFamilyCallRequested;

    public Task RequestOpenAsync() =>
        OpenFamilyCallRequested?.Invoke() ?? Task.CompletedTask;
}
