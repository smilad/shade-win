using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shade.Models;

namespace Shade.Views;

public partial class CredentialPickerDialog : Window
{
    public CredentialPickerDialog()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        ProfileList.Items.Clear();
        var s = App.State.Settings;
        var strategy = s.LbStrategy;
        var lbOn = s.EnableLoadBalancing;

        var filtered = s.Credentials.Where(c =>
        {
            if (!lbOn) return true;
            return strategy switch
            {
                LBStrategy.CFOnly => c.UsesCloudflare,
                LBStrategy.NormalOnly => !c.UsesCloudflare,
                _ => true,
            };
        });

        foreach (var c in filtered)
        {
            ProfileList.Items.Add(BuildRow(c, lbOn));
        }
    }

    private FrameworkElement BuildRow(Credential c, bool lbOn)
    {
        var isActive = lbOn ? c.IsEnabledForLB : (c.Id == App.State.Settings.ActiveCredentialID);
        var accent = (Brush)(c.UsesCloudflare ? FindResource("AccentOrange") : FindResource("AccentPurple"));

        var dock = new DockPanel { Margin = new Thickness(10, 8, 10, 8) };

        var trash = new Button
        {
            Style = (Style)FindResource("LinkButton"),
            Padding = new Thickness(6),
            Content = new TextBlock { Text = "🗑", Foreground = (Brush)FindResource("AccentRed"), FontSize = 12 },
        };
        trash.Click += (_, _) =>
        {
            App.State.Settings.Credentials.Remove(c);
            if (App.State.Settings.ActiveCredentialID == c.Id)
                App.State.Settings.ActiveCredentialID = App.State.Settings.Credentials.FirstOrDefault()?.Id;
            App.State.SaveSettings();
            App.State.OnChanged(nameof(AppState.Settings));
            Render();
        };
        DockPanel.SetDock(trash, Dock.Right);

        var pencil = new Button
        {
            Style = (Style)FindResource("LinkButton"),
            Padding = new Thickness(6),
            Content = new TextBlock { Text = "✎", Foreground = (Brush)FindResource("Secondary"), FontSize = 12 },
        };
        pencil.Click += (_, _) =>
        {
            var dlg = new CredentialEditDialog(c) { Owner = this };
            dlg.ShowDialog();
            Render();
        };
        DockPanel.SetDock(pencil, Dock.Right);

        var radio = new TextBlock
        {
            Text = lbOn
                ? (isActive ? "✅" : "⬜")
                : (isActive ? "🟢" : "⚪"),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        var nameStack = new StackPanel();
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(new TextBlock
        {
            Text = c.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Primary"),
        });
        if (c.UsesCloudflare)
        {
            var badge = new Border
            {
                Background = (Brush)new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0xFB, 0x92, 0x3C)),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Child = new TextBlock { Text = "CF", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = accent },
            };
            nameRow.Children.Add(badge);
        }
        nameStack.Children.Add(nameRow);
        nameStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(c.ScriptID) ? "No Script ID set"
                 : (c.ScriptID.Length > 26 ? c.ScriptID.Substring(0, 26) + "…" : c.ScriptID),
            Style = (Style)FindResource("MonoText"),
            FontSize = 10,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var inner = new DockPanel { Margin = new Thickness(0) };
        inner.Children.Add(radio);
        inner.Children.Add(nameStack);

        var selectButton = new Button
        {
            Background = isActive
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26,
                    ((SolidColorBrush)accent).Color.R,
                    ((SolidColorBrush)accent).Color.G,
                    ((SolidColorBrush)accent).Color.B))
                : (Brush)FindResource("GlassFill"),
            BorderBrush = isActive
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x4D,
                    ((SolidColorBrush)accent).Color.R,
                    ((SolidColorBrush)accent).Color.G,
                    ((SolidColorBrush)accent).Color.B))
                : (Brush)FindResource("GlassStroke"),
            BorderThickness = new Thickness(1),
            Style = (Style)FindResource("GhostButton"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6),
            Content = inner,
        };
        selectButton.Click += (_, _) =>
        {
            if (App.State.Settings.EnableLoadBalancing)
            {
                c.IsEnabledForLB = !c.IsEnabledForLB;
            }
            else
            {
                App.State.Settings.ActiveCredentialID = c.Id;
            }
            App.State.SaveSettings();
            App.State.OnChanged(nameof(AppState.Settings));
            if (!App.State.Settings.EnableLoadBalancing) Close();
            else Render();
        };

        var outerGrid = new Grid();
        outerGrid.Children.Add(selectButton);
        outerGrid.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Children = { pencil, trash },
        });

        return new Grid
        {
            Margin = new Thickness(0, 0, 0, 6),
            Children = { outerGrid },
        };
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CredentialEditDialog(null) { Owner = this };
        dlg.ShowDialog();
        Render();
    }
}
