using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinBoard.App.Services;
using WinBoard.Core;
using WinBoard.Input;
using WinBoard.Services.Gemini;
using WinBoard.Services.Settings;
using WinBoard.Swipe;

namespace WinBoard.App.Views;

public enum ShiftState { Off, Once, Lock }

public enum KeyboardPage { Letters, Symbols1, Symbols2 }

/// <summary>
/// Rendu et gestion des contacts du clavier : les touches sont des éléments positionnés sur un Canvas
/// (unités de touche -> pixels). Souris, tactile et stylet convergent vers les mêmes méthodes Down/Move/Up.
/// </summary>
public sealed class KeyboardView : Border
{
    private const double Gap = 4;
    private const double SwipeStartUnits = 0.6;
    private static readonly TimeSpan LongPressDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(55);
    private const int DoubleTapMs = 400;
    private const int MouseId = -1;

    private sealed class KeyVisual
    {
        public KeyDef Def = null!;
        public Border Box = null!;
        public TextBlock Label = null!;
    }

    private sealed class Contact
    {
        public int Id;
        public KeyVisual? Down;
        public readonly List<PointD> Path = new();
        public bool Swiping;
        public bool LongShown;
        public int Variant = -1;
        public DispatcherTimer? LongTimer;
        public DispatcherTimer? RepeatTimer;
        public System.Windows.Shapes.Polyline? Trail;
        public Border? Popup;
        public double PopupLeft, PopupCell;
        public List<(Border Cell, string Text)> Variants = new();
    }

    private readonly Canvas _keys = new() { Background = Brushes.Transparent, ClipToBounds = false };
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly List<KeyVisual> _visuals = new();
    private readonly Dictionary<int, Contact> _contacts = new();

    private KeyboardLayout? _layout;
    private AppSettings _settings = new();
    private TypingController? _controller;
    private SwipeDecoder? _decoder;
    private KeyboardPage _page = KeyboardPage.Letters;
    private IReadOnlyList<KeyDef> _pageKeys = Array.Empty<KeyDef>();
    private double _pageWidth = 10, _pageRows = 5, _yOffset;
    private double _keyH = 48;
    private double _fontSize = 16;
    private ShiftState _shift = ShiftState.Off;
    private bool _shiftAuto;
    private long _lastShiftTap;
    private DictationState _voice = DictationState.Idle;
    private string _spaceLabel = "";
    private SoundPlayer? _click;

    public KeyboardView()
    {
        Child = new Grid { Children = { _keys, _overlay } };
        _overlay.ClipToBounds = false;
        SnapsToDevicePixels = true;
        Focusable = false;

        // Un seul chemin par contact : la souris et le stylet passent par les événements Mouse
        // (ceux promus depuis le tactile sont ignorés), le tactile par les événements Touch.
        _keys.MouseLeftButtonDown += OnMouseDown;
        _keys.MouseMove += OnMouseMove;
        _keys.MouseLeftButtonUp += OnMouseUp;
        _keys.LostMouseCapture += (_, _) => CancelContact(MouseId);
        _keys.TouchDown += OnTouchDown;
        _keys.TouchMove += OnTouchMove;
        _keys.TouchUp += OnTouchUp;
        _keys.LostTouchCapture += (_, e) => CancelContact(e.TouchDevice.Id);
        _keys.SizeChanged += (_, _) => Relayout();

        // Pas de pression longue système ni de gestes qui détourneraient le contact.
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Stylus.SetIsTouchFeedbackEnabled(this, false);

        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelAllContacts(); };
    }

    // ---------------------------------------------------------------- API publique

    public event Action? HideRequested;
    public event Action? EmojiRequested;
    public event Action? VoiceRequested;
    public event Action? SettingsRequested;
    public event Action? LanguageRequested;

    /// <summary>Hauteur nécessaire au clavier affiché (nombre de rangées x hauteur de touche).</summary>
    public double ContentHeight => _pageRows * _keyH;

    public TypingController? Controller
    {
        get => _controller;
        set
        {
            if (_controller != null) _controller.StateChanged -= OnControllerState;
            _controller = value;
            if (_controller != null) _controller.StateChanged += OnControllerState;
            SyncAutoShift();
        }
    }

    public SwipeDecoder? Decoder
    {
        get => _decoder;
        set => _decoder = value;
    }

    /// <summary>Applique la disposition et les réglages (appelé à chaque changement de réglage).</summary>
    public void Configure(KeyboardLayout layout, AppSettings settings, string spaceLabel)
    {
        bool rebuild = _layout != layout || _settings.ShowNumberRow != settings.ShowNumberRow
                       || _keyH != settings.KeyHeight || _spaceLabel != spaceLabel;
        _layout = layout;
        _settings = settings;
        _spaceLabel = spaceLabel;
        _keyH = Math.Clamp(settings.KeyHeight, 28, 120);
        double themeSize = Application.Current.Resources["ThemeFontSize"] as double? ?? 16;
        _fontSize = themeSize * Math.Clamp(_keyH / 48.0, 0.75, 1.5);
        EnsureClick();
        if (rebuild || _visuals.Count == 0) BuildPage();
        else RefreshAll();
        UpdateHeight();
    }

    public void SetDictationState(DictationState state)
    {
        _voice = state;
        foreach (var v in _visuals.Where(v => v.Def.Kind == KeyKind.Voice)) ApplyVisual(v, false);
    }

    /// <summary>Retourne aux lettres (par exemple après l'émoji ou un changement de langue).</summary>
    public void ShowLetters()
    {
        if (_page == KeyboardPage.Letters) return;
        _page = KeyboardPage.Letters;
        BuildPage();
    }

    public void CancelAllContacts()
    {
        foreach (var id in _contacts.Keys.ToList()) CancelContact(id);
    }

    // ---------------------------------------------------------------- construction des pages

    private void UpdateHeight()
    {
        Height = ContentHeight;
        _keys.Height = ContentHeight;
    }

    private void BuildPage()
    {
        CancelAllContacts();
        if (_layout == null) return;
        _pageKeys = BuildPageKeys(_page, out _pageWidth, out _pageRows, out _yOffset);
        _keys.Children.Clear();
        _visuals.Clear();
        foreach (var def in _pageKeys)
        {
            var label = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.None
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            label.SetResourceReference(TextBlock.FontFamilyProperty, "ThemeFontFamily");
            var box = new Border { Child = label, IsHitTestVisible = false };
            box.SetResourceReference(Border.CornerRadiusProperty, "KeyCornerRadius");
            var kv = new KeyVisual { Def = def, Box = box, Label = label };
            _visuals.Add(kv);
            _keys.Children.Add(box);
        }
        UpdateHeight();
        RefreshAll();
        Relayout();
    }

    private IReadOnlyList<KeyDef> BuildPageKeys(KeyboardPage page, out double width, out double rows, out double yOffset)
    {
        var layout = _layout!;
        width = layout.Width;
        bool numRow = _settings.ShowNumberRow;
        yOffset = 0;

        if (page == KeyboardPage.Letters)
        {
            if (numRow)
            {
                rows = layout.Height;
                return layout.Keys;
            }
            // Sans rangée de chiffres : on retire la rangée 0 et on remonte le reste.
            yOffset = 1;
            rows = layout.Height - 1;
            return layout.Keys.Where(k => k.Y >= 1).Select(k => k with { Y = k.Y - 1 }).ToList();
        }

        var list = new List<KeyDef>();
        int y = 0;
        string a, b, c;
        string toggleId, toggleText;
        if (page == KeyboardPage.Symbols1)
        {
            a = "@#€_&-+()/"; b = "§%^=~|<>{}"; c = "*\"':;!?";
            toggleId = "page2"; toggleText = "=\\<";
        }
        else
        {
            a = "£¥$¢°•√π÷×"; b = "©®™✓¶∆`[]\\"; c = "«»„“”‘’";
            toggleId = "page1"; toggleText = "?123";
        }

        AddChars(list, "1234567890", y++, 0, "n");
        AddChars(list, a, y++, 0, "a");
        if (numRow) AddChars(list, b, y++, 0, "b");
        list.Add(new KeyDef(toggleId, KeyKind.Symbols, 0, y, 1.5, 1, toggleText));
        AddChars(list, c, y, 1.5, "c");
        list.Add(new KeyDef("backspace", KeyKind.Backspace, 8.5, y, 1.5, 1, "⌫"));
        y++;
        list.Add(new KeyDef("abc", KeyKind.Symbols, 0, y, 1.5, 1, "ABC"));
        list.Add(new KeyDef("emoji", KeyKind.Emoji, 1.5, y, 1, 1, "☺"));
        list.Add(new KeyDef("comma", KeyKind.Character, 2.5, y, 1, 1, ","));
        list.Add(new KeyDef("space", KeyKind.Space, 3.5, y, 3, 1, " "));
        list.Add(new KeyDef("period", KeyKind.Character, 6.5, y, 1, 1, "."));
        list.Add(new KeyDef("voice", KeyKind.Voice, 7.5, y, 1, 1, "🎤"));
        list.Add(new KeyDef("enter", KeyKind.Enter, 8.5, y, 1.5, 1, "⏎"));
        rows = y + 1;
        width = 10;
        return list;
    }

    private static void AddChars(List<KeyDef> list, string chars, int y, double x0, string prefix)
    {
        for (int i = 0; i < chars.Length; i++)
        {
            var s = chars[i].ToString();
            list.Add(new KeyDef($"{prefix}{y}_{i}", KeyKind.Character, x0 + i, y, 1, 1, s, s));
        }
    }

    // ---------------------------------------------------------------- rendu

    private void Relayout()
    {
        double w = _keys.ActualWidth;
        if (w <= 0 || _pageWidth <= 0) return;
        double unitW = w / _pageWidth;
        foreach (var kv in _visuals)
        {
            var d = kv.Def;
            Canvas.SetLeft(kv.Box, d.X * unitW + Gap / 2);
            Canvas.SetTop(kv.Box, d.Y * _keyH + Gap / 2);
            kv.Box.Width = Math.Max(1, d.Width * unitW - Gap);
            kv.Box.Height = Math.Max(1, d.Height * _keyH - Gap);
        }
    }

    private bool LettersPage => _page == KeyboardPage.Letters;

    private static bool IsLetterKey(KeyDef k) => k.Kind == KeyKind.Character && k.Text is { Length: 1 } t && char.IsLetter(t[0]);

    private string LabelFor(KeyDef k)
    {
        switch (k.Kind)
        {
            case KeyKind.Character:
                if (IsLetterKey(k) && _shift != ShiftState.Off) return k.ShiftText ?? k.Text!.ToUpperInvariant();
                return k.Text ?? "";
            case KeyKind.Space: return _spaceLabel;
            case KeyKind.Shift: return _shift == ShiftState.Lock ? "⇪" : "⇧";
            case KeyKind.Hide: return "⌄";
            default: return k.Text ?? "";
        }
    }

    private void RefreshAll()
    {
        foreach (var kv in _visuals)
        {
            kv.Label.Text = LabelFor(kv.Def);
            kv.Label.FontSize = kv.Def.Kind == KeyKind.Space ? _fontSize * 0.6
                : kv.Def.Kind == KeyKind.Symbols ? _fontSize * 0.85 : _fontSize;
            ApplyVisual(kv, false);
        }
    }

    private void RefreshShiftVisuals()
    {
        foreach (var kv in _visuals)
        {
            if (kv.Def.Kind is KeyKind.Character or KeyKind.Shift)
            {
                kv.Label.Text = LabelFor(kv.Def);
                ApplyVisual(kv, false);
            }
        }
    }

    /// <summary>Couleur de fond d'une touche selon son rôle et son état.</summary>
    private void ApplyVisual(KeyVisual kv, bool pressed)
    {
        var box = kv.Box;
        if (pressed) { box.SetResourceReference(Border.BackgroundProperty, "KeyPressedBrush"); return; }
        switch (kv.Def.Kind)
        {
            case KeyKind.Character:
            case KeyKind.Space:
                box.SetResourceReference(Border.BackgroundProperty, "KeyBrush");
                break;
            case KeyKind.Shift:
                box.SetResourceReference(Border.BackgroundProperty, _shift == ShiftState.Off ? "SpecialKeyBrush" : "AccentBrush");
                break;
            case KeyKind.Enter:
                box.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                break;
            case KeyKind.Voice when _voice == DictationState.Listening:
                box.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
                break;
            case KeyKind.Voice when _voice == DictationState.Processing:
                box.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                break;
            default:
                box.SetResourceReference(Border.BackgroundProperty, "SpecialKeyBrush");
                break;
        }
        if (kv.Def.Kind == KeyKind.Voice)
            kv.Label.Text = _voice == DictationState.Processing ? "…" : "🎤";
    }

    private KeyVisual? VisualOf(KeyDef def) => _visuals.FirstOrDefault(v => ReferenceEquals(v.Def, def));

    // ---------------------------------------------------------------- Maj

    private void OnControllerState() => SyncAutoShift();

    /// <summary>Arme Maj automatiquement quand le contrôleur demande une majuscule (début de phrase).</summary>
    private void SyncAutoShift()
    {
        if (_controller == null) return;
        bool want = _controller.ShouldCapitalizeNext;
        if (want && _shift == ShiftState.Off)
        {
            _shift = ShiftState.Once;
            _shiftAuto = true;
            RefreshShiftVisuals();
        }
        else if (!want && _shift == ShiftState.Once && _shiftAuto)
        {
            _shift = ShiftState.Off;
            _shiftAuto = false;
            RefreshShiftVisuals();
        }
    }

    private void ToggleShift()
    {
        long now = Environment.TickCount64;
        switch (_shift)
        {
            case ShiftState.Off: _shift = ShiftState.Once; break;
            case ShiftState.Once: _shift = now - _lastShiftTap <= DoubleTapMs && !_shiftAuto ? ShiftState.Lock : ShiftState.Off; break;
            default: _shift = ShiftState.Off; break;
        }
        _shiftAuto = false;
        _lastShiftTap = now;
        RefreshShiftVisuals();
    }

    /// <summary>Après une saisie : la majuscule ponctuelle est consommée.</summary>
    private void ConsumeShift()
    {
        if (_shift != ShiftState.Once) return;
        _shift = ShiftState.Off;
        _shiftAuto = false;
        RefreshShiftVisuals();
    }

    private string ApplyShift(string s) => _shift == ShiftState.Off ? s : s.ToUpperInvariant();

    // ---------------------------------------------------------------- entrées : souris, stylet, tactile

    private static bool IsTouchPromoted(MouseEventArgs e) =>
        e.StylusDevice?.TabletDevice?.Type == TabletDeviceType.Touch;

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsTouchPromoted(e)) return;
        if (_keys.CaptureMouse()) Down(MouseId, e.GetPosition(_keys));
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (IsTouchPromoted(e) || e.LeftButton != MouseButtonState.Pressed) return;
        Move(MouseId, e.GetPosition(_keys));
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (IsTouchPromoted(e)) return;
        var pos = e.GetPosition(_keys);
        Up(MouseId, pos); // avant la libération : LostMouseCapture annulerait le contact
        _keys.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        if (_keys.CaptureTouch(e.TouchDevice)) Down(e.TouchDevice.Id, e.GetTouchPoint(_keys).Position);
        e.Handled = true; // empêche la promotion en événements souris
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        Move(e.TouchDevice.Id, e.GetTouchPoint(_keys).Position);
        e.Handled = true;
    }

    private void OnTouchUp(object? sender, TouchEventArgs e)
    {
        var pos = e.GetTouchPoint(_keys).Position;
        Up(e.TouchDevice.Id, pos);
        _keys.ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- contacts

    private PointD ToUnits(Point px)
    {
        double unitW = Math.Max(1, _keys.ActualWidth) / _pageWidth;
        return new PointD(px.X / unitW, px.Y / _keyH);
    }

    private KeyDef? HitTest(Point px)
    {
        var p = ToUnits(px);
        return _pageKeys.FirstOrDefault(k => k.Contains(p));
    }

    private void Down(int id, Point px)
    {
        if (_contacts.ContainsKey(id)) CancelContact(id);
        var def = HitTest(px);
        if (def == null) return;
        var kv = VisualOf(def);
        if (kv == null) return;

        var c = new Contact { Id = id, Down = kv };
        _contacts[id] = c;
        PlayClick();

        if (IsLetterKey(def) && LettersPage)
            c.Path.Add(ToLayoutUnits(px));
        ApplyVisual(kv, true);

        switch (def.Kind)
        {
            case KeyKind.Backspace:
                DoBackspace();
                c.RepeatTimer = StartTimer(RepeatDelay, () =>
                {
                    DoBackspace();
                    c.RepeatTimer!.Interval = RepeatInterval;
                });
                break;
            case KeyKind.Character when def.LongPress is { Count: > 0 }:
                c.LongTimer = StartTimer(LongPressDelay, () => ShowVariants(c));
                break;
        }
    }

    private PointD ToLayoutUnits(Point px)
    {
        var p = ToUnits(px);
        return new PointD(p.X, p.Y + _yOffset);
    }

    private DispatcherTimer StartTimer(TimeSpan interval, Action tick)
    {
        var t = new DispatcherTimer(DispatcherPriority.Input) { Interval = interval };
        t.Tick += (_, _) =>
        {
            try { tick(); }
            catch (Exception e) { Log.Error("Minuteur du clavier.", e); t.Stop(); }
        };
        t.Start();
        return t;
    }

    private void Move(int id, Point px)
    {
        if (!_contacts.TryGetValue(id, out var c) || c.Down == null) return;

        if (c.LongShown)
        {
            UpdateVariantSelection(c, px);
            return;
        }

        var def = c.Down.Def;
        if (!_settings.SwipeEnabled || _decoder == null || !IsLetterKey(def) || !LettersPage) return;

        var p = ToLayoutUnits(px);
        if (c.Path.Count == 0 || c.Path[^1].DistanceTo(p) > 0.05) c.Path.Add(p);

        if (!c.Swiping)
        {
            var disp = ToUnits(px);
            if (!def.Contains(disp) && SwipeDecoder.IsSwipe(c.Path))
            {
                c.Swiping = true;
                c.LongTimer?.Stop();
                c.LongTimer = null;
                ApplyVisual(c.Down, false);
                if (_settings.SwipeTrail) BeginTrail(c);
            }
        }
        if (c.Swiping && c.Trail != null) c.Trail.Points.Add(px);
    }

    private void Up(int id, Point px)
    {
        if (!_contacts.TryGetValue(id, out var c)) return;
        _contacts.Remove(id);
        StopContact(c);
        if (c.Down == null) return;
        ApplyVisual(c.Down, false);
        var def = c.Down.Def;

        try
        {
            if (c.LongShown)
            {
                if (c.Variant >= 0 && c.Variant < c.Variants.Count) TypeCharacter(c.Variants[c.Variant].Text);
                return;
            }
            if (c.Swiping)
            {
                c.Path.Add(ToLayoutUnits(px));
                CommitSwipe(c);
                return;
            }
            if (def.Kind != KeyKind.Backspace) Activate(def);
        }
        catch (Exception e)
        {
            Log.Error("Traitement d'un contact clavier.", e);
        }
    }

    private void CancelContact(int id)
    {
        if (!_contacts.TryGetValue(id, out var c)) return;
        _contacts.Remove(id);
        StopContact(c);
        if (c.Down != null) ApplyVisual(c.Down, false);
    }

    private void StopContact(Contact c)
    {
        c.LongTimer?.Stop();
        c.RepeatTimer?.Stop();
        if (c.Trail != null) _overlay.Children.Remove(c.Trail);
        if (c.Popup != null) _overlay.Children.Remove(c.Popup);
        c.Trail = null;
        c.Popup = null;
    }

    // ---------------------------------------------------------------- actions

    private void DoBackspace() => _controller?.Backspace();

    private void TypeCharacter(string text)
    {
        _controller?.TypeText(text);
        ConsumeShift();
    }

    private void Activate(KeyDef def)
    {
        switch (def.Kind)
        {
            case KeyKind.Character:
                {
                    string t = def.Text ?? "";
                    if (IsLetterKey(def) && _shift != ShiftState.Off) t = def.ShiftText ?? t.ToUpperInvariant();
                    TypeCharacter(t);
                    break;
                }
            case KeyKind.Space: _controller?.TypeText(" "); ConsumeShift(); break;
            case KeyKind.Enter: _controller?.Enter(); break;
            case KeyKind.Tab: _controller?.Key(SpecialKey.Tab); break;
            case KeyKind.Left: _controller?.Key(SpecialKey.Left); break;
            case KeyKind.Right: _controller?.Key(SpecialKey.Right); break;
            case KeyKind.Shift: ToggleShift(); break;
            case KeyKind.Symbols:
                _page = def.Id switch
                {
                    "abc" => KeyboardPage.Letters,
                    "page2" => KeyboardPage.Symbols2,
                    _ => KeyboardPage.Symbols1
                };
                BuildPage();
                break;
            case KeyKind.Emoji: EmojiRequested?.Invoke(); break;
            case KeyKind.Voice: VoiceRequested?.Invoke(); break;
            case KeyKind.Language: LanguageRequested?.Invoke(); break;
            case KeyKind.Settings: SettingsRequested?.Invoke(); break;
            case KeyKind.Hide: HideRequested?.Invoke(); break;
        }
    }

    private void CommitSwipe(Contact c)
    {
        if (_controller == null || _decoder == null) return;
        if (!SwipeDecoder.IsSwipe(c.Path)) return;
        var words = _decoder.Decode(c.Path, 5, null).Select(x => x.Word).Where(w => w.Length > 0).ToList();
        if (words.Count == 0) return;
        if (_shift == ShiftState.Lock) words = words.Select(w => w.ToUpperInvariant()).ToList();
        else if (_shift == ShiftState.Once) words = words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]).ToList();
        _controller.CommitSwipe(words);
        ConsumeShift();
    }

    // ---------------------------------------------------------------- tracé de swipe

    private void BeginTrail(Contact c)
    {
        var line = new System.Windows.Shapes.Polyline
        {
            StrokeThickness = 6,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Opacity = 0.9,
            IsHitTestVisible = false
        };
        line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "TrailBrush");
        var trail = (Application.Current.Resources["TrailBrush"] as SolidColorBrush)?.Color ?? Colors.DeepSkyBlue;
        line.Effect = new DropShadowEffect { Color = trail, ShadowDepth = 0, BlurRadius = 14, Opacity = 0.9 };
        foreach (var kvp in c.Path) line.Points.Add(PxFromLayout(kvp));
        _overlay.Children.Add(line);
        c.Trail = line;
    }

    private Point PxFromLayout(PointD p)
    {
        double unitW = Math.Max(1, _keys.ActualWidth) / _pageWidth;
        return new Point(p.X * unitW, (p.Y - _yOffset) * _keyH);
    }

    // ---------------------------------------------------------------- bulle de variantes

    private void ShowVariants(Contact c)
    {
        c.LongTimer?.Stop();
        c.LongTimer = null;
        if (c.Down?.Def.LongPress is not { Count: > 0 } alts || c.Swiping) return;

        double unitW = Math.Max(1, _keys.ActualWidth) / _pageWidth;
        double cell = Math.Max(unitW * 0.85, 38);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        c.Variants = new List<(Border, string)>();
        foreach (var alt in alts)
        {
            var text = ApplyShift(alt);
            var tb = new TextBlock
            {
                Text = text,
                FontSize = _fontSize * 1.1,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            tb.SetResourceReference(TextBlock.FontFamilyProperty, "ThemeFontFamily");
            var cellBox = new Border { Width = cell, Height = _keyH - Gap, Margin = new Thickness(1), Child = tb };
            cellBox.SetResourceReference(Border.CornerRadiusProperty, "KeyCornerRadius");
            cellBox.SetResourceReference(Border.BackgroundProperty, "KeyBrush");
            panel.Children.Add(cellBox);
            c.Variants.Add((cellBox, text));
        }

        var popup = new Border { Child = panel, Padding = new Thickness(3), IsHitTestVisible = false };
        popup.SetResourceReference(Border.BackgroundProperty, "SpecialKeyBrush");
        popup.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        popup.SetResourceReference(Border.CornerRadiusProperty, "KeyCornerRadius");
        popup.BorderThickness = new Thickness(1.5);
        popup.Effect = new DropShadowEffect { ShadowDepth = 2, BlurRadius = 10, Opacity = 0.5 };

        double totalW = alts.Count * (cell + 2) + 6 + 3;
        var d = c.Down.Def;
        double left = d.X * unitW + d.Width * unitW / 2 - totalW / 2;
        left = Math.Clamp(left, 0, Math.Max(0, _keys.ActualWidth - totalW));
        double top = Math.Max(0, d.Y * _keyH - _keyH - 6);
        Canvas.SetLeft(popup, left);
        Canvas.SetTop(popup, top);
        _overlay.Children.Add(popup);
        c.Popup = popup;
        c.PopupLeft = left + 3 + 3; // marge interne + bordure
        c.PopupCell = cell + 2;
        c.LongShown = true;
        if (c.Down != null) ApplyVisual(c.Down, false);
    }

    private void UpdateVariantSelection(Contact c, Point px)
    {
        int idx = (int)Math.Floor((px.X - c.PopupLeft) / c.PopupCell);
        if (idx < 0 || idx >= c.Variants.Count) idx = -1;
        if (idx == c.Variant) return;
        c.Variant = idx;
        for (int i = 0; i < c.Variants.Count; i++)
        {
            if (i == idx) c.Variants[i].Cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            else c.Variants[i].Cell.SetResourceReference(Border.BackgroundProperty, "KeyBrush");
        }
    }

    // ---------------------------------------------------------------- son

    private void EnsureClick()
    {
        if (!_settings.KeySound) return;
        if (_click != null) return;
        try
        {
            // Clic synthétique très court (sinusoïde amortie), sans fichier externe.
            const int rate = 16000;
            var pcm = new short[rate / 40];
            for (int i = 0; i < pcm.Length; i++)
            {
                double env = Math.Exp(-i / 60.0);
                pcm[i] = (short)(Math.Sin(2 * Math.PI * 1500 * i / rate) * env * 9000);
            }
            var wav = WavEncoder.Encode(pcm, rate, 1);
            var player = new SoundPlayer(new MemoryStream(wav));
            player.Load();
            _click = player;
        }
        catch (Exception e)
        {
            Log.Error("Initialisation du son de touche.", e);
        }
    }

    private void PlayClick()
    {
        if (!_settings.KeySound) return;
        try { _click?.Play(); }
        catch (Exception) { }
    }
}
