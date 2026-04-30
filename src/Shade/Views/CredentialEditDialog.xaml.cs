using System;
using System.Windows;
using Shade.Models;

namespace Shade.Views;

public partial class CredentialEditDialog : Window
{
    private readonly Credential? _editTarget;
    private bool _authKeyVisible;

    public CredentialEditDialog(Credential? credential)
    {
        InitializeComponent();
        _editTarget = credential;
        TitleText.Text = credential is null ? "New Profile" : "Edit Profile";

        if (credential is { } c)
        {
            NameBox.Text = c.Name;
            ScriptIDBox.Text = c.ScriptID;
            AuthKeyBox.Password = c.AuthKey;
            AuthKeyVisible.Text = c.AuthKey;
            CFCheck.IsChecked = c.UsesCloudflare;
        }
    }

    private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
    {
        _authKeyVisible = !_authKeyVisible;
        if (_authKeyVisible)
        {
            AuthKeyVisible.Text = AuthKeyBox.Password;
            AuthKeyVisible.Visibility = Visibility.Visible;
            AuthKeyBox.Visibility = Visibility.Collapsed;
            EyeIcon.Text = "🚫";
        }
        else
        {
            AuthKeyBox.Password = AuthKeyVisible.Text;
            AuthKeyVisible.Visibility = Visibility.Collapsed;
            AuthKeyBox.Visibility = Visibility.Visible;
            EyeIcon.Text = "👁";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(NameBox.Text)
            ? $"Profile {App.State.Settings.Credentials.Count + 1}"
            : NameBox.Text.Trim();
        var sid = ScriptIDBox.Text.Trim();
        if (string.IsNullOrEmpty(sid)) return;

        var key = _authKeyVisible ? AuthKeyVisible.Text : AuthKeyBox.Password;
        var cf  = CFCheck.IsChecked == true;

        if (_editTarget is { } existing)
        {
            existing.Name = name;
            existing.ScriptID = sid;
            existing.AuthKey = key;
            existing.UsesCloudflare = cf;
        }
        else
        {
            var cred = new Credential
            {
                Name = name,
                ScriptID = sid,
                AuthKey = key,
                UsesCloudflare = cf,
            };
            App.State.Settings.Credentials.Add(cred);
            App.State.Settings.ActiveCredentialID = cred.Id;
        }
        App.State.SaveSettings();
        // Re-raise PropertyChanged so the dashboard repaints.
        App.State.OnChanged(nameof(AppState.Settings));
        Close();
    }
}
