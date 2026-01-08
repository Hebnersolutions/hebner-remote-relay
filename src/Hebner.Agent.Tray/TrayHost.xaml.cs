using System;
using System.IO;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;

namespace Hebner.Agent.Tray;

public partial class TrayHost : Window
{
    private readonly TaskbarIcon _tb = new();

    public TrayHost()
    {
        InitializeComponent();

        ConfigureLogging();

        Log.Information("Tray starting up");

        _tb.ToolTipText = "Hebner Solutions Remote Support";
        _tb.Icon = System.Drawing.SystemIcons.Shield; // TODO: replace with branded .ico
        _tb.TrayMouseDoubleClick += (_,__) => OpenMain();

        _tb.ContextMenu = new System.Windows.Controls.ContextMenu();
        var open = new System.Windows.Controls.MenuItem { Header = "Open" };
        open.Click += (_,__) => OpenMain();
        var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exit.Click += (_,__) => Application.Current.Shutdown();
        _tb.ContextMenu.Items.Add(open);
        _tb.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _tb.ContextMenu.Items.Add(exit);

        HandleEnrollmentOnStartup();
    }

    private void ConfigureLogging()
    {
        try
        {
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HebnerRemoteSupport");
            var logsDir = Path.Combine(baseDir, "logs");
            Directory.CreateDirectory(logsDir);
            // Use rolling by day. Serilog will append to today's file.
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(logsDir, "tray-.log"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
        catch
        {
            // If logging setup fails, don't crash the tray.
            try { Log.CloseAndFlush(); } catch { }
        }
    }

    private void HandleEnrollmentOnStartup()
    {
        var token = EnrollmentTokenStore.LoadToken();
        if (!string.IsNullOrEmpty(token))
        {
            Log.Information("Enrollment present: {token}", EnrollmentTokenStore.MaskToken(token));
            return;
        }

        Log.Information("Enrollment token missing; showing prompt");

        // Show modal prompt on startup
        try
        {
            var prompt = new EnrollmentPromptWindow();
            var res = prompt.ShowDialog();
            if (res == true)
            {
                var saved = EnrollmentTokenStore.LoadToken();
                Log.Information("Enrollment succeeded: {token}", EnrollmentTokenStore.MaskToken(saved));
            }
            else
            {
                Log.Information("Enrollment prompt cancelled by user");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error showing enrollment prompt");
        }
    }

    private void OpenMain()
    {
        var w = new MainWindow();
        w.Show();
        w.Activate();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tb.Dispose();
        base.OnClosed(e);
        try { Log.CloseAndFlush(); } catch { }
    }
}
