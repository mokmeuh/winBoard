using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinBoard.App.Services;
using WinBoard.Services.Emoji;

namespace WinBoard.App.Views;

/// <summary>
/// Panneau d'émojis qui remplace les touches : onglets (retour, récents, recherche, groupes)
/// et grille virtualisée (seules les rangées visibles existent à l'écran).
/// </summary>
public sealed class EmojiPanel : Border
{
    private const double Cell = 42;
    private const int RecentsTab = -1;
    private const int SearchTab = -2;

    private readonly EmojiCatalog _catalog = EmojiCatalog.Load();
    private readonly RecentEmojis _recents;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<int, Border> _tabBoxes = new();
    private readonly TextBlock _hint = new();
    private readonly ScrollViewer _scroll = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private readonly Dictionary<int, Canvas> _rows = new();

    private IReadOnlyList<string> _items = Array.Empty<string>();
    private int _cols = 1;
    private int _currentTab = RecentsTab;
    private string _query = "";
    private string _language = "fr";
    private Point _downPos;
    private bool _down;

    public EmojiPanel(RecentEmojis recents)
    {
        _recents = recents;
        SetResourceReference(BackgroundProperty, "BackgroundBrush");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Barre d'onglets : retour aux lettres, récents, recherche, puis un onglet par groupe.
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        var back = MakeTab("ABC", 0.8);
        back.MouseLeftButtonUp += (_, e) => { CloseRequested?.Invoke(); e.Handled = true; };
        DockPanel.SetDock(back, Dock.Left);
        bar.Children.Add(back);
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.Margin = new Thickness(8, 0, 8, 0);
        _hint.FontSize = 12;
        _hint.Opacity = 0.7;
        _hint.TextTrimming = TextTrimming.CharacterEllipsis;
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        DockPanel.SetDock(_hint, Dock.Right);
        bar.Children.Add(_hint);
        var tabScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.HorizontalOnly,
            Content = _tabs
        };
        bar.Children.Add(tabScroll);
        Grid.SetRow(bar, 0);
        root.Children.Add(bar);

        AddTab(RecentsTab, "🕘");
        AddTab(SearchTab, "🔍");
        for (int i = 0; i < _catalog.Groups.Count; i++)
        {
            var first = _catalog.Groups[i].Items.FirstOrDefault()?.Character ?? "•";
            AddTab(i, first);
        }

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.PanningMode = PanningMode.VerticalOnly;
        _scroll.Content = _canvas;
        _scroll.ScrollChanged += (_, _) => RenderVisible();
        _scroll.SizeChanged += (_, _) => { Layout(); RenderVisible(); };
        _canvas.MouseLeftButtonDown += (_, e) => { _downPos = e.GetPosition(_canvas); _down = true; };
        _canvas.MouseLeftButtonUp += OnCanvasUp;
        Grid.SetRow(_scroll, 1);
        root.Children.Add(_scroll);

        Child = root;
        SelectTab(RecentsTab);
    }

    /// <summary>Un émoji a été choisi.</summary>
    public event Action<string>? EmojiChosen;

    /// <summary>Retour au clavier de lettres.</summary>
    public event Action? CloseRequested;

    public void SetLanguage(string language) => _language = language;

    /// <summary>Appelé à l'ouverture et à chaque changement du mot en cours : alimente l'onglet de recherche.</summary>
    public void SetQuery(string word)
    {
        word = word?.Trim() ?? "";
        if (word == _query) return;
        _query = word;
        _hint.Text = word.Length > 0 ? $"Recherche : {word}" : "Tapez un mot avant d'ouvrir les émojis pour les chercher";
        if (word.Length > 0) SelectTab(SearchTab);
        else if (_currentTab == SearchTab) SelectTab(_recents.Items.Count > 0 ? RecentsTab : 0);
    }

    /// <summary>À appeler quand le panneau devient visible.</summary>
    public void Reset(string currentWord)
    {
        _query = "\u0001"; // force la réévaluation
        SetQuery(currentWord);
        if (_query.Length == 0) SelectTab(_recents.Items.Count > 0 ? RecentsTab : 0);
    }

    private Border MakeTab(string text, double scale)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 18 * scale,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var b = new Border { Width = 44, Height = 34, Margin = new Thickness(1), Child = tb, Background = Brushes.Transparent };
        b.SetResourceReference(Border.CornerRadiusProperty, "KeyCornerRadius");
        return b;
    }

    private void AddTab(int id, string glyph)
    {
        var tab = MakeTab(glyph, 1);
        tab.MouseLeftButtonUp += (_, e) => { SelectTab(id); e.Handled = true; };
        _tabBoxes[id] = tab;
        _tabs.Children.Add(tab);
    }

    private void SelectTab(int id)
    {
        _currentTab = id;
        foreach (var (key, box) in _tabBoxes)
        {
            if (key == id) box.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else box.Background = Brushes.Transparent;
        }
        _items = id switch
        {
            RecentsTab => _recents.Items,
            SearchTab => _query.Length == 0 || _query == "\u0001"
                ? Array.Empty<string>()
                : _catalog.Search(_query, _language, 120).Select(e => e.Character).ToList(),
            _ => _catalog.Groups[id].Items.Select(e => e.Character).ToList()
        };
        _scroll.ScrollToTop();
        Layout();
        RenderVisible();
    }

    private void Layout()
    {
        double w = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth;
        int cols = Math.Max(1, (int)Math.Floor(Math.Max(w, Cell) / Cell));
        if (cols != _cols)
        {
            _cols = cols;
            ClearRows();
        }
        int rows = (_items.Count + _cols - 1) / _cols;
        _canvas.Width = _cols * Cell;
        _canvas.Height = Math.Max(rows * Cell, 1);
        ClearRows();
    }

    private void ClearRows()
    {
        foreach (var r in _rows.Values) _canvas.Children.Remove(r);
        _rows.Clear();
    }

    /// <summary>Crée uniquement les rangées visibles et retire les autres.</summary>
    private void RenderVisible()
    {
        if (_scroll.ViewportHeight <= 0) return;
        int totalRows = (_items.Count + _cols - 1) / _cols;
        int first = Math.Max(0, (int)Math.Floor(_scroll.VerticalOffset / Cell) - 1);
        int last = Math.Min(totalRows - 1, (int)Math.Ceiling((_scroll.VerticalOffset + _scroll.ViewportHeight) / Cell) + 1);

        foreach (var key in _rows.Keys.Where(r => r < first || r > last).ToList())
        {
            _canvas.Children.Remove(_rows[key]);
            _rows.Remove(key);
        }
        for (int r = first; r <= last; r++)
        {
            if (_rows.ContainsKey(r)) continue;
            var row = new Canvas { Width = _cols * Cell, Height = Cell, IsHitTestVisible = false };
            for (int c = 0; c < _cols; c++)
            {
                int idx = r * _cols + c;
                if (idx >= _items.Count) break;
                var tb = new TextBlock
                {
                    Text = _items[idx],
                    FontSize = 24,
                    FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"),
                    Width = Cell,
                    Height = Cell,
                    TextAlignment = TextAlignment.Center,
                    Padding = new Thickness(0, 5, 0, 0)
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                Canvas.SetLeft(tb, c * Cell);
                row.Children.Add(tb);
            }
            Canvas.SetTop(row, r * Cell);
            _canvas.Children.Add(row);
            _rows[r] = row;
        }
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (!_down) return;
        _down = false;
        var p = e.GetPosition(_canvas);
        // Un déplacement notable est un défilement, pas un choix.
        if (Math.Abs(p.X - _downPos.X) > 8 || Math.Abs(p.Y - _downPos.Y) > 8) return;
        int col = (int)(p.X / Cell), row = (int)(p.Y / Cell);
        int idx = row * _cols + col;
        if (col < 0 || col >= _cols || idx < 0 || idx >= _items.Count) return;
        var emoji = _items[idx];
        _recents.Add(emoji);
        EmojiChosen?.Invoke(emoji);
        e.Handled = true;
    }
}
