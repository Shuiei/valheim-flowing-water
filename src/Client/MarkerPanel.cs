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
/// ends it. Its widgets are <see cref="MarkerPanelView"/>'s, on the game's own dialog canvas; never on a dedicated
/// server.
/// </summary>
internal sealed class MarkerPanel : MonoBehaviour
{
    // How far a player may walk from the placed one they change before the panel closes, metres.
    private const float ChangeReach = 10f;

    private static readonly Color Refused = new(1f, 0.45f, 0.35f);

    private static readonly Color Held = new(1f, 0.82f, 0.45f);

    // The panel's frames, kept from stopping the water: after a fault the panel is put away (TurnOff) and off until a
    // world opens (WorldOpened).
    private static readonly FaultGate Gate = new(
        "The source and drain panel failed; it is off until a world opens, the water goes on",
        FlowingWaterPlugin.Log.LogError,
        TurnOff);

    private static MarkerPanel _panel;

    // A canvas of the mod's own, when the game's dialog canvas cannot be found: made once, kept for every panel after.
    private static Canvas _ownCanvas;

    private static int _closedFrame = -1;

    private MarkerPanelView _view;

    // The placed one being changed; none while the panel shows the next one's values. Destroyed meanwhile, it is
    // still the panel's until the panel closes (Unity's own null would take it for none).
    private Marker _target;

    private bool _drain;

    private bool _editing;

    private int _openedFrame = -1;

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

        Marker selected = HoePieces.SelectedMarker;
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

        // [E] aimed at something the game uses (a door, a chest, a placed source or drain, which opens the panel for
        // it) is the game's: one press must not both use it and open the panel, whichever of the two is asked first.
        if (ZInput.GetButtonDown("Use") && !Hud.IsPieceSelectionVisible() && !ClosedThisFrame && !AimedAtUsable())
        {
            ZInput.ResetButtonStatus("Use");
            Edit();
        }
    }

    private static bool AimedAtUsable() =>
        Player.m_localPlayer is { } player && player.GetHoverObject() is { } hover && hover.GetComponentInParent<Interactable>() != null;

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
        _view.Title.text = MarkerValues.Title(drain, Placed);
        _view.RateHint.text = "m³/s: " + MarkerValues.RateHint(drain);
        _view.RadiusHint.text = "m: " + MarkerValues.RadiusHint;
        _view.Layout(drain, Placed);
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
        if (force || !_view.Rate.isFocused)
        {
            _view.Rate.SetTextWithoutNotify(MarkerValues.Format(rate));
        }

        if (force || !_view.Radius.isFocused)
        {
            _view.Radius.SetTextWithoutNotify(MarkerValues.Format(radius));
        }
    }

    private void Edit()
    {
        _editing = true;
        _openedFrame = Time.frameCount;
        _view.Rate.interactable = true;
        _view.Radius.interactable = true;
        UpdateKeys();
        _view.Rate.Select();
        _view.Rate.ActivateInputField();
    }

    // The values no longer changed: what was typed is taken (as Enter would), the fields close.
    private void Stop()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        foreach (TMP_InputField field in new[] { _view.Rate, _view.Radius })
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
        _view.Keys.text = MarkerValues.Keys(_editing, Placed);
        _view.Remove.gameObject.SetActive(_editing && Placed);
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
        _view.Status.text = string.IsNullOrEmpty(text) ? string.Empty : char.ToUpperInvariant(text[0]) + text.Substring(1);
        _view.Status.color = taken ? (text != null && text.StartsWith("held", System.StringComparison.Ordinal) ? Held : MarkerPanelView.Plain) : Refused;
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
        Canvas canvas = dialog?.GetComponentInParent<Canvas>()?.rootCanvas ?? OwnCanvas();
        var view = MarkerPanelView.Build(canvas.transform, MarkerPanelView.Style.From(dialog));
        MarkerPanel panel = view.Root.AddComponent<MarkerPanel>();
        panel._view = view;
        view.Remove.onClick.AddListener(panel.RemoveTarget);
        view.Rate.onSubmit.AddListener(text => panel.Typed(false, text));
        view.Rate.onEndEdit.AddListener(text => panel.Typed(false, text));
        view.Radius.onSubmit.AddListener(text => panel.Typed(true, text));
        view.Radius.onEndEdit.AddListener(text => panel.Typed(true, text));
        _panel = panel;
        return _panel;
    }

    // A canvas of the mod's own for the panel, where the game's dialog canvas is not found: one, kept (the game's
    // scene changes destroy it; another is made then).
    private static Canvas OwnCanvas()
    {
        if (_ownCanvas != null)
        {
            return _ownCanvas;
        }

        FlowingWaterPlugin.Log.LogWarning("The game's dialog canvas was not found: the source and drain panel has a canvas of its own");
        var own = new GameObject("FlowingWater canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        _ownCanvas = own.GetComponent<Canvas>();
        _ownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _ownCanvas.sortingOrder = 100;
        CanvasScaler scaler = own.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return _ownCanvas;
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
}
