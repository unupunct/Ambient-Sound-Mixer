using System.Windows;

namespace AmbientSoundMixer.App;

public partial class PromptWindow : Window
{
    public PromptWindow(string prompt, string initial)
    {
        InitializeComponent();
        Prompt.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    public string Value => Input.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0) return;
        DialogResult = true;
    }

    public static string? Ask(Window? owner, string prompt, string initial = "")
    {
        var w = new PromptWindow(prompt, initial) { Owner = owner };
        return w.ShowDialog() == true ? w.Value : null;
    }
}
