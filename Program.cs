namespace HdrToggle;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--list" or "--set")
        {
            Cli.Run(args);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, "HdrToggle_SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Another instance is running; ask it to show its window.
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(TrayApplicationContext.ShowEventName);
                showEvent.Set();
            }
            catch
            {
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(startHidden: args.Contains("--tray")));
    }
}
