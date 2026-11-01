using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace SimSmsGateway;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        App.ThemeChanged += UpdateSystemBars;
        App.SystemBarsRefreshRequested += UpdateSystemBars;
        UpdateSystemBars();
    }

    protected override void OnResume()
    {
        base.OnResume();
        UpdateSystemBars();
    }

    protected override void OnDestroy()
    {
        App.ThemeChanged -= UpdateSystemBars;
        App.SystemBarsRefreshRequested -= UpdateSystemBars;
        base.OnDestroy();
    }

    // Every page uses the Dashboard's background, so the status bar matches it in light and dark themes.
    private void UpdateSystemBars()
    {
        RunOnUiThread(() =>
        {
            if (Window is null)
            {
                return;
            }

            var dark = App.IsDark;
#pragma warning disable CA1422 // Validate platform compatibility; still honoured below API 35.
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor(dark ? "#0A1115" : "#F3F6F9"));
            Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor(dark ? "#111B21" : "#FFFFFF"));
#pragma warning restore CA1422
            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (controller is not null)
            {
                controller.AppearanceLightStatusBars = !dark;
                controller.AppearanceLightNavigationBars = !dark;
            }
        });
    }
}
