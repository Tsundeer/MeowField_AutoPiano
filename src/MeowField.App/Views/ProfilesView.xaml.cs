using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeowField.App;

namespace MeowField.App.Views;

public partial class ProfilesView : UserControl
{
    public ProfilesView()
    {
        InitializeComponent();
        KeyMappingGrid.PreviewKeyDown += OnKeyMappingGridKeyDown;
        KeyMappingGrid.RowEditEnding += OnKeyMappingRowEditEnding;
    }

    private void OnKeyMappingGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return; // let manual editing proceed
        }

        if (KeyMappingGrid.SelectedItem is not KeyMappingRow row)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Delete or Key.Back)
        {
            row.Key = "";
            e.Handled = true;
            RefreshFilter();
            return;
        }

        var token = ToKeyToken(key);
        if (token is null)
        {
            return;
        }

        var modifiers = new List<string>(3);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers.Add("CTRL");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers.Add("SHIFT");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers.Add("ALT");
        row.Key = modifiers.Count == 0 ? token : string.Join("+", modifiers.Append(token));
        e.Handled = true;
        RefreshFilter();
    }

    private void OnKeyMappingRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            RefreshFilter();
        }
    }

    private void RefreshFilter() => (DataContext as ProfilesViewModel)?.RefreshKeyMappingFilter();

    private static string? ToKeyToken(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemSemicolon => ";",
        Key.OemQuestion => "/",
        Key.OemMinus => "-",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemQuotes => "'",
        Key.OemPlus => "=",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Enter => "Enter",
        Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12 => key.ToString(),
        _ => null,
    };
}
