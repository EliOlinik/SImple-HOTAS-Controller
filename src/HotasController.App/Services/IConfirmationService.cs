namespace HotasController.App.Services;

/// <summary>Asks the user to confirm a risky action. Kept behind an interface so view models stay UI-free.</summary>
public interface IConfirmationService
{
    bool Confirm(string title, string message);
}
