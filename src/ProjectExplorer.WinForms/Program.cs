using AutoUpdaterDotNET;
using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Models;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Shell;
using ProjectExplorer.Shell.Services;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

internal static class Program
{
    // Hosted on GitHub Releases — update this URL when you move to a custom domain
    private const string UpdateCheckUrl =
        "https://raw.githubusercontent.com/bmusical/ProjectExplorer/main/updates/updates.xml";

    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // ── Set up services ────────────────────────────────────────────────
        var repository     = new JsonProjectRepository();
        var projectManager = new ProjectManager(repository);

        projectManager.InitializeAsync().GetAwaiter().GetResult();

        // ── License check ──────────────────────────────────────────────────
        var licenseManager = new LicenseManager();
        var license = licenseManager.GetCurrentLicense(projectManager.Projects);

        IShellIconProvider shellIconProvider = new ShellIconProvider();

        if (projectManager.Projects.Count == 0)
        {
            var sample = projectManager.CreateProjectAsync("Sample Project", "A sample project to get you started").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Assets").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Documentation").GetAwaiter().GetResult();
        }

        var mainForm = new MainForm(projectManager, shellIconProvider, licenseManager, license);
        mainForm.ListViewItemSorter = new ListViewColumnSorter();

        // Check for updates ~5 seconds after startup so the main window is visible first
        mainForm.Shown += (s, e) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 5000 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                AutoUpdater.AppTitle = "Project Nest";
                AutoUpdater.RunUpdateAsAdmin = false;  // PrivilegesRequired=lowest install
                AutoUpdater.ShowSkipButton = true;
                AutoUpdater.ShowRemindLaterButton = true;
                AutoUpdater.Start(UpdateCheckUrl);
            };
            timer.Start();
        };

        Application.Run(mainForm);
    }
}
