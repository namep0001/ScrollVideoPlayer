using System.Windows;

namespace ScrollVideoPlayer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        base.OnStartup(e);
    }
}
