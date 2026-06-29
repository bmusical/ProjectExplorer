using ProjectExplorer.Core.Interfaces;
using ProjectExplorer.Core.Services;
using ProjectExplorer.Shell;
using ProjectExplorer.Shell.Services;
using ProjectExplorer.WinForms.Helpers;

namespace ProjectExplorer.WinForms;

internal static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Enable modern DPI awareness
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Set up services
        var repository = new JsonProjectRepository();
        var projectManager = new ProjectManager(repository);

        // Initialize — load projects from storage
        projectManager.InitializeAsync().GetAwaiter().GetResult();

        // Shell services
        IShellIconProvider shellIconProvider = new ShellIconProvider();

        // If no projects exist, create a sample project for demonstration
        if (projectManager.Projects.Count == 0)
        {
            var sampleProject = projectManager.CreateProjectAsync("Sample Project", "A sample project to get you started").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sampleProject.Id, "Assets").GetAwaiter().GetResult();
            projectManager.CreateCollectionAsync(sampleProject.Id, "Documentation").GetAwaiter().GetResult();
        }

        var mainForm = new MainForm(projectManager, shellIconProvider);
        mainForm.ListViewItemSorter = new ListViewColumnSorter();

        Application.Run(mainForm);
    }
}
