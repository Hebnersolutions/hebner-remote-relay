using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using Hebner.Agent.Shared;

namespace Hebner.Agent.Tray;

public partial class MainWindow : Window
{
    public ViewModel VM { get; } = new();
    private readonly IpcClient _ipcClient;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = VM;

        _ipcClient = new IpcClient();
        VM.DeviceId = LoadDeviceId();

        var token = EnrollmentTokenStore.LoadToken();
        VM.EnrollmentTokenMasked = EnrollmentTokenStore.MaskToken(token);
        VM.StatusText = string.IsNullOrEmpty(token) ? "Not enrolled" : "Online (service running)"; // TODO: query service via IPC
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void EndSession_Click(object sender, RoutedEventArgs e)
    {
        // TODO: send IPC to service to end session
        MessageBox.Show("Stub: would end session via IPC to the service.", Brand.ProductName);
    }

    private void SetPassword_Click(object sender, RoutedEventArgs e)
    {
        var pwd = Pwd.Password ?? "";
        if (pwd.Length < 8)
        {
            MessageBox.Show("Use at least 8 characters.", Brand.ProductName);
            return;
        }

        var hash = HashPassword(pwd);
        SaveUnattendedHash(hash);

        MessageBox.Show("Unattended password set (hash saved). Next: service must register it with broker policy.", Brand.ProductName);
        Pwd.Password = "";
    }

    private static string LoadDeviceId()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HebnerRemoteSupport", "device-id.txt");
        return File.Exists(path) ? File.ReadAllText(path).Trim() : "(not found)";
    }

    private static string HashPassword(string pwd)
    {
        // Simple PBKDF2 hash. In production, store salt+hash and version the format.
        var salt = RandomNumberGenerator.GetBytes(16);
        var pbkdf2 = new Rfc2898DeriveBytes(pwd, salt, 120_000, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(32);
        return $"pbkdf2-sha256$120000${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    private static void SaveUnattendedHash(string hash)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "HebnerRemoteSupport");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "unattended-hash.txt"), hash);
    }
}

public sealed class ViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string _statusText = "Starting...";
    private string _deviceId = "";
    private bool _unattendedEnabled;
    private string _enrollmentTokenMasked = "Not enrolled";

    public string StatusText { get => _statusText; set { _statusText = value; Changed(nameof(StatusText)); } }
    public string DeviceId { get => _deviceId; set { _deviceId = value; Changed(nameof(DeviceId)); } }
    public bool UnattendedEnabled { get => _unattendedEnabled; set { _unattendedEnabled = value; Changed(nameof(UnattendedEnabled)); } }
    public string EnrollmentTokenMasked { get => _enrollmentTokenMasked; set { _enrollmentTokenMasked = value; Changed(nameof(EnrollmentTokenMasked)); } }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new(n));
}
