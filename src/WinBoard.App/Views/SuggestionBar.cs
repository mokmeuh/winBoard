using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinBoard.App.Services;

namespace WinBoard.App.Views;

/// <summary>Barre de suggestions à trois emplacements (celui du centre est mis en avant), avec zone de messages.</summary>
public sealed class SuggestionBar : Border
{
    private readonly Border[] _slots = new Border[3];
    private readonly TextBlock[] _texts = new TextBlock[3];
    private readonly string?[] _values = new string?[3];
    private readonly Border _messageBox = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock _message = new();
    private readonly DispatcherTimer _flashTimer = new();
    private string? _status;
    private string? _flash;
    private bool _flashIsError;

    public SuggestionBar()
    {
        SetResourceReference(BackgroundProperty, "SuggestionBarBrush");
        SetResourceReference(CornerRadiusProperty, "KeyCornerRadius");
        MinHeight = 32;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var tb = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 8, 0),
                FontWeight = i == 1 ? FontWeights.SemiBold : FontWeights.Normal
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, i == 1 ? "AccentBrush" : "TextBrush");
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "ThemeFontFamily");
            tb.FontSize = 17;
            var slot = new Border { Child = tb, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            if (i > 0)
            {
                slot.BorderThickness = new Thickness(1, 0, 0, 0);
                slot.SetResourceReference(Border.BorderBrushProperty, "KeyBrush");
            }
            slot.MouseLeftButtonUp += (_, e) =>
            {
                if (_values[index] != null) SuggestionPicked?.Invoke(_map[index]);
                e.Handled = true;
            };
            Grid.SetColumn(slot, i);
            grid.Children.Add(slot);
            _slots[i] = slot;
            _texts[i] = tb;
        }

        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.TextTrimming = TextTrimming.CharacterEllipsis;
        _message.Margin = new Thickness(10, 0, 10, 0);
        _message.FontSize = 14;
        _message.SetResourceReference(TextBlock.FontFamilyProperty, "ThemeFontFamily");
        _messageBox.Child = _message;
        _messageBox.SetResourceReference(Border.BackgroundProperty, "SuggestionBarBrush");
        _messageBox.SetResourceReference(Border.CornerRadiusProperty, "KeyCornerRadius");
        Grid.SetColumnSpan(_messageBox, 3);
        grid.Children.Add(_messageBox);

        Child = grid;
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); _flash = null; RefreshMessage(); };
    }

    /// <summary>Indice de la suggestion choisie dans la liste du contrôleur.</summary>
    public event Action<int>? SuggestionPicked;

    public void SetSuggestions(IReadOnlyList<string> items)
    {
        // Le meilleur candidat va au centre : ordre d'affichage gauche, centre, droite = 2e, 1er, 3e.
        // PickSuggestion reçoit toujours l'indice dans la liste d'origine.
        int[] order = { 1, 0, 2 };
        for (int slot = 0; slot < 3; slot++)
        {
            int src = order[slot];
            _values[slot] = src < items.Count ? items[src] : null;
            _texts[slot].Text = _values[slot] ?? "";
        }
        _map = order;
    }

    private int[] _map = { 1, 0, 2 };

    /// <summary>Message transitoire (erreur ou information).</summary>
    public void Flash(string message, bool error = true, double seconds = 6)
    {
        _flash = message;
        _flashIsError = error;
        _flashTimer.Stop();
        _flashTimer.Interval = TimeSpan.FromSeconds(seconds);
        _flashTimer.Start();
        RefreshMessage();
    }

    /// <summary>Message persistant (par exemple « Écoute… ») jusqu'à ce qu'on le retire avec null.</summary>
    public void SetStatus(string? status)
    {
        _status = status;
        RefreshMessage();
    }

    private void RefreshMessage()
    {
        string? text = _flash ?? _status;
        if (text == null)
        {
            _messageBox.Visibility = Visibility.Collapsed;
            return;
        }
        _message.Text = text;
        if (_flash != null && _flashIsError)
            _message.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x4D));
        else
            _message.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _messageBox.Visibility = Visibility.Visible;
    }
}
