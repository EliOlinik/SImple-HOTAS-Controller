namespace HotasController.App.Services;

/// <summary>Asks the user for a line of text. Kept behind an interface so view models stay UI-free.</summary>
public interface IPromptService
{
    /// <summary>Returns the entered text, or null when the user cancels.</summary>
    string? PromptText(string title, string message, string initialText = "");
}
