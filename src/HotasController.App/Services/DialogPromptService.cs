using System.Windows;

namespace HotasController.App.Services;

public sealed class DialogPromptService : IPromptService
{
    public string? PromptText(string title, string message, string initialText = "")
    {
        var window = new TextPromptWindow(title, message, initialText);
        var owner = Application.Current?.MainWindow;
        if (owner is { IsVisible: true } && !ReferenceEquals(owner, window)) window.Owner = owner;
        return window.ShowDialog() == true ? window.Text : null;
    }
}
