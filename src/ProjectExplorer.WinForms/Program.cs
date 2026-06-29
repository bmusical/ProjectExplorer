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

        // ── License check ──────────────────────────────────────────────────
        var licenseManager = new LicenseManager();
        var license = licenseManager.GetCurrentLicense();

        if (!license.IsUsable)
        {
            // Trial expired — force registration dialog before showing the app
            using var reg = new RegistrationDialog(licenseManager, license);
            reg.ShowDialog();
            license = reg.ResultLicense;

            if (!license.IsUsable)
            {
                // User closed without activating — exit
                return;
            }
        }

        // ── Set up services ────────────────────────────────────────────────
        var repository     = new JsonProjectRepository();
        var projectManager = new ProjectManager(repository);

        projectManager.InitializeAsync().GetAwaiter().GetResult();

        IShellIconProvider shellIconProvider = new ShellIconProvider();

        if (projectManager.Projects.Count == 0)
        {
            var sample = projectManager.CreateProjectAsync("Sample Project", "A sample project to get you started").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Assets").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sample.Id, "Documentation").GetAwaiter().GetResult();
        }

        var mainForm = new MainForm(projectManager, shellIconProvider, licenseManager, license);
        mainForm.ListViewItemSorter = new ListViewColumnSorter();

        Application.Run(mainForm);
    }
}
