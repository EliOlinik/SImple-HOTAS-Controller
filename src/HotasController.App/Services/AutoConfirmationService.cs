namespace HotasController.App.Services;

/// <summary>Always confirms. For fakes and tests.</summary>
public sealed class AutoConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message) => true;
}
