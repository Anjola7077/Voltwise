using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Voltwise;

public partial class App : Application
{
public App()
{
InitializeComponent();

    // Restore the saved theme before creating the main page.
    string savedTheme = Preferences.Get(
        "voltwise_theme",
        "System"
    );

    UserAppTheme = savedTheme switch
    {
        "Dark" => AppTheme.Dark,
        "Light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        "light" => AppTheme.Light,
        _ => AppTheme.Unspecified
    };
}

protected override Window CreateWindow(
    IActivationState? activationState)
{
    return new Window(new MainPage());
}

}