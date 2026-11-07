using Microsoft.UI.Xaml;

namespace Maber.App;

public partial class App : Application
{
    public static Window? Main { get; private set; }

    public App() => this.InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        this.UnhandledException += (_, e) =>
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "maber-crash.log"),
                $"[{DateTime.Now:HH:mm:ss}] {e.Message}\n{e.Exception}\n\n");
            e.Handled = false;
        };
        Main = new MainWindow();
        Main.Activate();
    }
}
