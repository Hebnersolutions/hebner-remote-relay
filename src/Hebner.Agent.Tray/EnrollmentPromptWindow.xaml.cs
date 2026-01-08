using System;
using System.Windows;

namespace Hebner.Agent.Tray;

public partial class EnrollmentPromptWindow : Window
{
    public EnrollmentPromptWindow()
    {
        InitializeComponent();
        TxtToken.Focus();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnEnroll_Click(object sender, RoutedEventArgs e)
    {
        var raw = TxtToken.Text ?? "";
        var clean = raw.Trim().ToUpperInvariant();
        if (!EnrollmentTokenStore.ValidateToken(clean))
        {
            MessageBox.Show("Invalid token format. Use pattern AAAA-BBBB-CCCC-DDDD.", "Hebner Solutions Remote Support");
            return;
        }

        var ok = EnrollmentTokenStore.SaveToken(clean);
        if (!ok)
        {
            MessageBox.Show("Unable to save enrollment token. Check permissions.", "Hebner Solutions Remote Support");
            return;
        }

        DialogResult = true;
        Close();
    }
}
