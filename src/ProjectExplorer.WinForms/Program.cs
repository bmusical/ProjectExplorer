using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Shell;
using ProjectExplorer.Shell.Services;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var appSettingsManager = new AppSettingsManager();
        var appSettings = appSettingsManager.Load();

        // ── Focus on Run: Prevent multiple copies ──────────────────────────
        // Only "Prevent" launches register/check the single-instance guard, so an "Allow"
        // launch never blocks and never gets signaled by a later "Prevent" launch either —
        // each launch simply acts on its own current setting.
        SingleInstanceGuard? instanceGuard = null;
        if (appSettings.FocusOnRun == FocusOnRunMode.PreventMultipleCopies)
        {
            instanceGuard = new SingleInstanceGuard();
            if (!instanceGuard.IsFirstInstance)
            {
                instanceGuard.SignalExistingInstance();
                instanceGuard.Dispose();
                return;
            }
        }

        try
        {
            RunApplication(appSettingsManager, instanceGuard);
        }
        finally
        {
            instanceGuard?.Dispose();
        }
    }

    private static void RunApplication(AppSettingsManager appSettingsManager, SingleInstanceGuard? instanceGuard)
    {
        // ── Set up services ────────────────────────────────────────────────
        var repository     = new JsonProjectRepository();
        var projectManager = new ProjectManager(repository);

        projectManager.InitializeAsync().GetAwaiter().GetResult();

        // ── License check ──────────────────────────────────────────────────
        var licenseManager = new LicenseManager();
        var license = licenseManager.GetCurrentLicense(projectManager.Projects);

        IShellIconProvider shellIconProvider = new ShellIconProvider();
        IShellThumbnailProvider shellThumbnailProvider = new ShellThumbnailProvider();
        IShellPropertiesProvider shellPropertiesProvider = new ShellPropertiesProvider();

        if (projectManager.Projects.Count == 0)
        {
            var sample = projectManager.CreateProjectAsync("Sample Project", "A sample project to get you started").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Assets").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Documentation").GetAwaiter().GetResult();
        }

        var mainForm = new MainForm(projectManager, shellIconProvider, shellThumbnailProvider, shellPropertiesProvider,
            licenseManager, license, appSettingsManager);
        mainForm.ListViewItemSorter = new ListViewColumnSorter();

        instanceGuard?.ListenForActivation(() =>
        {
            if (mainForm.IsHandleCreated)
                mainForm.BeginInvoke(mainForm.RestoreAndActivate);
        });

        // Check for updates ~5 seconds after startup so the main window is visible first
        mainForm.Shown += (s, e) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                mainForm.CheckForUpdates(silent: true);
            };
            timer.Start();
        };

        Application.Run(mainForm);
    }
}
