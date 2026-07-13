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

        // ── Single instance, always enforced ───────────────────────────────
        // Two windows against the same projects.json can silently destroy each other's
        // changes: ProjectManager loads the whole file into memory once at startup and
        // never refreshes it, so a save from one window can be clobbered by a later save
        // from another window still holding a stale in-memory copy of the same project (or,
        // for project reordering, of the *entire* project list). This used to be a user
        // choice ("Allow multiple copies"); it isn't safe to offer until the app can actually
        // support concurrent windows without corrupting data, so a second launch always just
        // switches to the running window instead of opening a new one.
        var instanceGuard = new SingleInstanceGuard();
        if (!instanceGuard.IsFirstInstance)
        {
            instanceGuard.SignalExistingInstance();
            instanceGuard.Dispose();
            return;
        }

        try
        {
            RunApplication(appSettingsManager, instanceGuard);
        }
        finally
        {
            instanceGuard.Dispose();
        }
    }

    private static void RunApplication(AppSettingsManager appSettingsManager, SingleInstanceGuard instanceGuard)
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

        instanceGuard.ListenForActivation(() =>
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
