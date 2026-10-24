using System.Diagnostics.CodeAnalysis;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlowingWater.Client;

/// <summary>
/// The panel on the right of the screen with a source's or drain's rate (and a drain's radius): those the next one
/// placed takes while one is the hoe's piece, or a placed one's after [E] on it. [E] changes the next one's values;
/// while they are changed the mouse is free and the game takes no keys (as for its own text boxes); [Esc] or [E]
/// ends it. Built in code on the game's own dialog canvas, in its font, sprites and colours; never on a dedicated
/// server.
/// </summary>
internal sealed class MarkerPanel : MonoBehaviour
{
    /// <summary>The panel's object, under the game's canvas.</summary>
    public const string ObjectName = "FlowingWater panel";

    /// <summary>The rate's field (a child of the panel).</summary>
    public const string RateName = "Rate";

    /// <summary>The radius's field (a child of the panel).</summary>
    public const string RadiusName = "Radius";

    /// <summary>The button that removes a placed one (a child of the panel).</summary>
    public const string RemoveName = "Remove";

    private const float Width = 360f;

    private const float Gutter = 18f;

    private const float FieldWidth = 110f;

    private const float ButtonWidth = 140f;

    // How far a player may walk from the placed one they change before the panel closes, metres.
    private const float ChangeReach = 10f;

    private static readonly Color Refused = new(1f, 0.45f, 0.35f);

    private static readonly Color Held = new(1f, 0.82f, 0.45f);

    private static readonly Color Plain = new(0.85f, 0.85f, 0.85f);

    // The panel's frames, kept from stopping the water: after a fault the panel is put away (TurnOff) and off until a
    // world opens (WorldOpened).
    private static readonly FaultGate Gate = new(
        "The source and drain panel failed; it is off until a world opens, the water goes on",
        FlowingWaterPlugin.Log.LogError,
        TurnOff);

    private static MarkerPanel _panel;

    private static int _closedFrame = -1;

    // The placed one being changed; none while the panel shows the next one's values. Destroyed meanwhile, it is
    // still the panel's until the panel closes (Unity's own null would take it for none).
    private Marker _target;

    private bool _drain;

    private bool _editing;

    private int _openedFrame = -1;

    private TextMeshProUGUI _title;

    private TextMeshProUGUI _rateHint;

    private TextMeshProUGUI _radiusLabel;

    private TextMeshProUGUI _radiusHint;

    private TextMeshProUGUI _status;

    private TextMeshProUGUI _keys;

    private TMP_InputField _rate;

    private TMP_InputField _radius;

    private Button _remove;

    private RectTransform _frame;

    /// <summary>Whether the panel was closed this frame (the key that closed it opens nothing else).</summary>
    public static bool ClosedThisFrame => _closedFrame == Time.frameCount;

    /// <summary>
    /// Whether the panel takes the keyboard and the mouse, as the game's text box does: while its values are changed,
    /// and the frame it closes.
    /// </summary>
    public static bool TakesInput => (_panel != null && _panel._editing) || ClosedThisFrame;

    /// <summary>Whether the panel is off, after a fault in it (<see cref="TurnOff"/>): it neither shows nor opens.</summary>
    public static bool Off => !Gate.On;

    private bool Placed => _target is not null;

    /// <summary>[E] on a placed one: the panel opens to change it.</summary>
    public static void Open(Marker marker)
    {
        MarkerPanel panel = Off ? null : Made();
        if (panel == null)
        {
            return;
        }

        panel.Stop();
        panel._target = marker;
        panel.Show(marker.IsDrain);
        panel.Edit();
    }

    /// <summary>
    /// Each frame: the panel shows the next one's values while the player holds the hoe with a source or drain as its
    /// piece, a placed one's while it is changed, else nothing; and takes [E] and [Esc]. A fault in it is logged once
    /// and turns it off, until a world opens; the water's frame goes on.
    /// </summary>
    public static void Tick() => Gate.Run(TickOn);

    /// <summary>A world opens: the panel, off after a fault in the last one, has a fresh try.</summary>
    public static void WorldOpened() => Gate.Reset();

    private static void TickOn()
    {
        Player player = Player.m_localPlayer;
        if (player == null || ZNet.instance == null || ZNet.instance.IsDedicated())
        {
            _panel?.Close();
            return;
        }

        if (_panel != null && _panel.Placed)
        {
            _panel.TickPlaced(player);
            return;
        }

        Marker selected = player.InPlaceMode() && player.GetSelectedPiece() is { } piece ? piece.GetComponent<Marker>() : null;
        if (selected == null || (!(_panel != null && _panel._editing) && (Hud.IsUserHidden() || OtherWindowOpen())))
        {
            _panel?.Close();
            return;
        }

        MarkerPanel panel = Made();
        if (panel == null)
        {
            return;
        }

        if (!panel.gameObject.activeSelf || panel._drain != selected.IsDrain)
        {
            panel.Stop();
            panel.Show(selected.IsDrain);
        }

        panel.TickPlacing();
    }

    // The panel put away after a fault in it, until a world opens: it no longer takes the keyboard or the mouse,
    // whatever it was doing.
    private static void TurnOff()
    {
        if (_panel != null)
        {
            Destroy(_panel.gameObject);
        }

        _panel = null;
    }

    // A value typed in a field: taken for the next one or written into the placed one; refused, it is said why and
    // the field shows the value kept. Enter, leaving the field and closing the panel all come here.
    private void Typed(bool radius, string text)
    {
        if (Placed)
        {
            ChangeTarget(radius, text);
        }
        else
        {
            float before = radius ? HoePieces.Next.Radius : HoePieces.Next.Rate(_drain);
            bool taken = radius ? HoePieces.Next.TrySetRadius(text, out string note) : HoePieces.Next.TrySetRate(_drain, text, out note);
            if (!taken || note != null || !MarkerValues.Same(before, radius ? HoePieces.Next.Radius : HoePieces.Next.Rate(_drain)))
            {
                Say(note, taken);
            }
        }

        // The field typed in shows what was kept (held, or the value before a refused one), though it may still have
        // the focus while Enter is handled.
        ShowValues(true);
    }

    private void ChangeTarget(bool radius, string text)
    {
        if (!_target.IsValid)
        {
            Close();
            return;
        }

        float rate = _target.Rate;
        float reach = _target.Radius;
        bool taken = radius ? MarkerValues.TryRadius(text, out float value, out string note) : MarkerValues.TryRate(text, _drain, out value, out note);
        if (!taken || MarkerValues.Same(value, radius ? reach : rate))
        {
            if (!taken || note != null)
            {
                Say(note, taken);
            }

            return;
        }

        if (Commands.MarkerRefusal(CommandRights.MarkerUse.Change) is { } refusal)
        {
            Say(refusal, false);
            return;
        }

        if (!PrivateArea.CheckAccess(_target.transform.position))
        {
            Say(Localization.instance.Localize("$msg_privatezone"), false);
            return;
        }

        _target.Change(radius ? rate : value, radius ? value : reach);
        Say(note ?? "Changed: " + MarkerValues.Describe(_drain, _target.Rate, _target.Radius), true);
    }

    private void TickPlaced(Player player)
    {
        if (!_target.IsValid || Vector3.Distance(player.transform.position, _target.transform.position) > ChangeReach)
        {
            Close();
            return;
        }

        if (Leaving())
        {
            Close();
        }
    }

    private void TickPlacing()
    {
        if (_editing)
        {
            if (Leaving())
            {
                Stop();
                NoteClosed();
            }

            return;
        }

        // [E] aimed at a placed one is the game's, which opens the panel for it.
        if (ZInput.GetButtonDown("Use") && !Hud.IsPieceSelectionVisible() && !ClosedThisFrame && !AimedAtPlaced())
        {
            ZInput.ResetButtonStatus("Use");
            Edit();
        }
    }

    private static bool AimedAtPlaced() =>
        Player.m_localPlayer is { } player && player.GetHoverObject() is { } hover && hover.GetComponentInParent<Marker>() != null;

    // [Esc] or [E], not on the frame the panel opened (the same press).
    private bool Leaving()
    {
        if (_openedFrame == Time.frameCount || !(ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("Use")))
        {
            return false;
        }

        ZInput.ResetButtonStatus("Use");
        return true;
    }

    // The game's windows the panel stays out of the way of (its text box by its own panel: whether the game takes the
    // keys for a text box also says whether this panel does).
    private static bool OtherWindowOpen() =>
        InventoryGui.IsVisible() || Menu.IsVisible() || Minimap.IsOpen() || Console.IsVisible()
        || (TextInput.instance != null && TextInput.instance.m_panel.activeSelf)
        || (Chat.instance != null && Chat.instance.HasFocus()) || StoreGui.IsVisible();

    private void Show(bool drain)
    {
        _drain = drain;
        gameObject.SetActive(true);
        _title.text = MarkerValues.Title(drain, Placed);
        _rateHint.text = "m³/s: " + MarkerValues.RateHint(drain);
        _radiusHint.text = "m: " + MarkerValues.RadiusHint;
        _radiusLabel.gameObject.SetActive(drain);
        _radius.gameObject.SetActive(drain);
        _radiusHint.gameObject.SetActive(drain);
        // A placed one's Remove button goes under the keys, away from the fields.
        float bottom = drain ? 196f : 132f;
        Place(_status.rectTransform, bottom, Width - (2f * Gutter), 40f);
        Place(_keys.rectTransform, bottom + 44f, Width - (2f * Gutter), 24f);
        Place((RectTransform)_remove.transform, bottom + 76f, ButtonWidth, 34f);
        _frame.sizeDelta = new Vector2(Width, bottom + (Placed ? 126f : 80f));
        Say(null, true);
        ShowValues();
        UpdateKeys();
    }

    // The values in the fields; a field being typed in is left alone unless the typing is done (forced).
    private void ShowValues(bool force = false)
    {
        if (Placed && !_target.IsValid)
        {
            return;
        }

        float rate = Placed ? _target.Rate : HoePieces.Next.Rate(_drain);
        float radius = Placed ? _target.Radius : HoePieces.Next.Radius;
        if (force || !_rate.isFocused)
        {
            _rate.SetTextWithoutNotify(MarkerValues.Format(rate));
        }

        if (force || !_radius.isFocused)
        {
            _radius.SetTextWithoutNotify(MarkerValues.Format(radius));
        }
    }

    private void Edit()
    {
        _editing = true;
        _openedFrame = Time.frameCount;
        _rate.interactable = true;
        _radius.interactable = true;
        UpdateKeys();
        _rate.Select();
        _rate.ActivateInputField();
    }

    // The values no longer changed: what was typed is taken (as Enter would), the fields close.
    private void Stop()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        foreach (TMP_InputField field in new[] { _rate, _radius })
        {
            if (field.isFocused)
            {
                field.DeactivateInputField(true);
            }

            field.interactable = false;
        }

        EventSystem.current?.SetSelectedGameObject(null);

        ShowValues();

        UpdateKeys();
    }

    private void Close()
    {
        if (!gameObject.activeSelf)
        {
            return;
        }

        if (_editing)
        {
            NoteClosed();
        }

        Stop();
        _target = null;
        gameObject.SetActive(false);
    }

    // The keys, and the Remove button while a placed one is changed (the mouse free to click it).
    private void UpdateKeys()
    {
        _keys.text = MarkerValues.Keys(_editing, Placed);
        _remove.gameObject.SetActive(_editing && Placed);
    }

    // The Remove button clicked: the placed one removed as the console's remove does, for a player who may (others,
    // or a ward not theirs, told why in the centre of the screen as the game's own removal does), and the panel closed.
    // The game takes no click while the panel has the mouse: nothing is hit or built with it.
    private void RemoveTarget()
    {
        if (!Placed || !_target.IsValid)
        {
            Close();
            return;
        }

        Player player = Player.m_localPlayer;
        if (Commands.MarkerRefusal(CommandRights.MarkerUse.Remove) is { } refusal)
        {
            player?.Message(MessageHud.MessageType.Center, refusal);
            return;
        }

        if (!PrivateArea.CheckAccess(_target.transform.position))
        {
            player?.Message(MessageHud.MessageType.Center, "$msg_privatezone");
            return;
        }

        // The click is the panel's: the game's attack does not see it on the frame the panel closes.
        ZInput.ResetButtonStatus("Attack");
        Marker target = _target;
        Close();
        target.Remove();
    }

    private void Say(string text, bool taken)
    {
        _status.text = string.IsNullOrEmpty(text) ? string.Empty : char.ToUpperInvariant(text[0]) + text.Substring(1);
        _status.color = taken ? (text != null && text.StartsWith("held", System.StringComparison.Ordinal) ? Held : Plain) : Refused;
    }

    // The panel, made the first time it is needed, on the game's dialog canvas (made again when the game's interface
    // was, at a new world).
    private static MarkerPanel Made()
    {
        if (_panel != null)
        {
            return _panel;
        }

        TextInput dialog = TextInput.instance;
        Canvas canvas = dialog?.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            FlowingWaterPlugin.Log.LogWarning("The game's dialog canvas was not found: the source and drain panel has a canvas of its own");
            var own = new GameObject("FlowingWater canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = own.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            CanvasScaler scaler = own.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }
        else
        {
            canvas = canvas.rootCanvas;
        }

        _panel = Build(canvas.transform, Style.From(dialog));
        return _panel;
    }

    private static MarkerPanel Build(Transform canvas, Style style)
    {
        // Built inactive: the fields wake up whole.
        var root = new GameObject(ObjectName, typeof(RectTransform));
        root.SetActive(false);
        root.transform.SetParent(canvas, false);
        var frame = (RectTransform)root.transform;
        frame.anchorMin = frame.anchorMax = new Vector2(1f, 0.5f);
        frame.pivot = new Vector2(1f, 0.5f);
        frame.anchoredPosition = new Vector2(-40f, 0f);
        Image back = root.AddComponent<Image>();
        Sprite(back, style.Background, new Color(0.1f, 0.08f, 0.06f, 0.92f));

        MarkerPanel panel = root.AddComponent<MarkerPanel>();
        panel._frame = frame;
        panel._title = Text(root.transform, "Title", style, 26f, style.Title, 16f, 34f);
        _ = Label(root.transform, "Rate label", style, "Rate", 64f);
        panel._rate = Field(root.transform, RateName, style, 60f);
        panel._rateHint = Text(root.transform, "Rate hint", style, 16f, Plain, 96f, 22f);
        panel._radiusLabel = Label(root.transform, "Radius label", style, "Radius", 128f);
        panel._radius = Field(root.transform, RadiusName, style, 124f);
        panel._radiusHint = Text(root.transform, "Radius hint", style, 16f, Plain, 160f, 22f);
        panel._status = Text(root.transform, "Status", style, 16f, Plain, 196f, 40f);
        panel._status.textWrappingMode = TextWrappingModes.Normal;
        panel._keys = Text(root.transform, "Keys", style, 16f, style.Title, 240f, 24f);
        panel._remove = RemoveButton(root.transform, style);
        panel._remove.onClick.AddListener(panel.RemoveTarget);
        panel._rate.onSubmit.AddListener(text => panel.Typed(false, text));
        panel._rate.onEndEdit.AddListener(text => panel.Typed(false, text));
        panel._radius.onSubmit.AddListener(text => panel.Typed(true, text));
        panel._radius.onEndEdit.AddListener(text => panel.Typed(true, text));
        return panel;
    }

    private static TextMeshProUGUI Label(Transform parent, string name, Style style, string text, float top)
    {
        TextMeshProUGUI label = Text(parent, name, style, 20f, Color.white, top, 30f);
        label.text = text;
        label.rectTransform.sizeDelta = new Vector2(Width - (2f * Gutter) - FieldWidth - 8f, 30f);
        return label;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, Style style, float size, Color colour, float top, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        if (style.Font != null)
        {
            text.font = style.Font;
        }

        text.fontSize = size;
        text.color = colour;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        Place(text.rectTransform, top, Width - (2f * Gutter), height);
        return text;
    }

    // A one-line field for a number: digits, a point or comma and a sign only (a key like [E] types nothing).
    private static TMP_InputField Field(Transform parent, string name, Style style, float top)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        Place(rect, top, FieldWidth, 34f);
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-Gutter, -top);
        Image back = go.AddComponent<Image>();
        Sprite(back, style.Field, new Color(0f, 0f, 0f, 0.6f));

        var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        area.transform.SetParent(go.transform, false);
        var areaRect = (RectTransform)area.transform;
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(8f, 2f);
        areaRect.offsetMax = new Vector2(-8f, -2f);

        var textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(area.transform, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (style.Font != null)
        {
            text.font = style.Font;
        }

        text.fontSize = 20f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRect;
        field.textComponent = text;
        field.targetGraphic = back;
        if (style.Font != null)
        {
            field.fontAsset = style.Font;
        }

        field.pointSize = 20f;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 10;
        field.restoreOriginalTextOnEscape = true;
        field.onValidateInput = (_, _, added) => char.IsDigit(added) || added is '.' or ',' or '-' ? added : '\0';
        field.interactable = false;
        return field;
    }

    // The button that removes a placed one: the game's button sprite and font, hidden until a placed one is changed.
    private static Button RemoveButton(Transform parent, Style style)
    {
        var go = new GameObject(RemoveName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image back = go.AddComponent<Image>();
        Sprite(back, style.Button ?? style.Field, new Color(0.35f, 0.12f, 0.08f, 0.9f));
        Button button = go.AddComponent<Button>();
        button.targetGraphic = back;
        TextMeshProUGUI label = Text(go.transform, "Label", style, 20f, style.ButtonText, 0f, 34f);
        label.text = MarkerValues.RemoveLabel;
        label.alignment = TextAlignmentOptions.Center;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        go.SetActive(false);
        return button;
    }

    // Placed from the panel's top left.
    private static void Place(RectTransform rect, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(Gutter, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Sprite(Image image, Sprite sprite, Color plain)
    {
        if (sprite == null)
        {
            image.color = plain;
            return;
        }

        image.sprite = sprite;
        image.type = sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.color = Color.white;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy() => Forget(this);

    // The panel was closed this frame (ClosedThisFrame).
    private static void NoteClosed() => _closedFrame = Time.frameCount;

    // A panel destroyed (with the game's interface, at a new world): made again when next needed.
    private static void Forget(MarkerPanel panel)
    {
        if (_panel == panel)
        {
            _panel = null;
        }
    }

    // The look of the game's own text box (the sign's): its font, title colour and sprites; plain when not found.
    private sealed class Style
    {
        public TMP_FontAsset Font { get; private set; }

        public Color Title { get; private set; } = new(1f, 0.72f, 0.36f);

        public Sprite Background { get; private set; }

        public Sprite Field { get; private set; }

        public Sprite Button { get; private set; }

        public Color ButtonText { get; private set; } = Color.white;

        public static Style From(TextInput dialog)
        {
            var style = new Style();
            if (dialog == null)
            {
                return style;
            }

            if (dialog.m_topic != null)
            {
                style.Font = dialog.m_topic.font;
                style.Title = dialog.m_topic.color;
            }

            if (dialog.m_panel != null && dialog.m_panel.GetComponent<Image>() is { } back)
            {
                style.Background = back.sprite;
            }

            if (dialog.m_inputField != null && dialog.m_inputField.GetComponent<Image>() is { } field)
            {
                style.Field = field.sprite;
            }

            // The dialog's own button (its OK), with its label's colour.
            if (dialog.m_panel != null && dialog.m_panel.GetComponentInChildren<Button>(true) is { } button)
            {
                style.Button = (button.targetGraphic as Image)?.sprite;
                if (button.GetComponentInChildren<TMP_Text>(true) is { } text)
                {
                    style.ButtonText = text.color;
                }
            }

            return style;
        }
    }
}
