using System.Windows;

namespace HotasController.App;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string initialText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        InputBox.Text = initialText;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Text => InputBox.Text;

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
}
