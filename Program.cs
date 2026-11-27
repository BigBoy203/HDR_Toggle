namespace HdrToggle;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "--list" or "--set" or "--rate" or "--fps")
        {
            Cli.Run(args);
            return;
        }

        using var mutex = new Mutex(initiallyOwned: true, "HdrToggle_SingleInstance", out bool isFirstInstance);
        bool afterUpdate = args.Contains(Updater.AfterUpdateArg);
        if (!isFirstInstance && afterUpdate)
        {
            // Started by the version this one just replaced: it is on its way out, so wait
            // for it to let go rather than handing over to it.
            try { isFirstInstance = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { isFirstInstance = true; }
        }
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

        Updater.CleanUpAfterUpdate();

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(startHidden: args.Contains("--tray"), justUpdated: afterUpdate));
    }
}
