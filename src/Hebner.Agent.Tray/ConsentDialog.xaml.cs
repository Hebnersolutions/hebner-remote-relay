using System;
using System.Windows;
using Hebner.Agent.Shared;

namespace Hebner.Agent.Tray;

public partial class ConsentDialog : Window
{
    public event Action<bool>? ConsentResult;

    public ConsentDialog(ConsentRequestMessage request)
    {
        InitializeComponent();
        DataContext = request;
    }

    private void BtnAllow_Click(object sender, RoutedEventArgs e)
    {
        ConsentResult?.Invoke(true);
        Close();
    }

    private void BtnDeny_Click(object sender, RoutedEventArgs e)
    {
        ConsentResult?.Invoke(false);
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // If dialog is closed without clicking buttons, deny consent
        ConsentResult?.Invoke(false);
        base.OnClosing(e);
    }
}
