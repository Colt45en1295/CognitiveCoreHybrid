using System.Windows;

namespace CognitiveCore.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CognitiveCore");
        Directory.CreateDirectory(appData);
    }
}
