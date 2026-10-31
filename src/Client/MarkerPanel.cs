using System.Diagnostics.CodeAnalysis;
using FlowingWater.Volume;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FlowingWater.Client;

/// <summary>
/// The panel on the right of the screen with a source's shape and power, or a drain's power and reach, on sliders:
/// those the next one placed takes while one is the hoe's piece, or a placed one's after [E] on it (each change written
/// into it at once). [E] changes the next one's values; while they are changed the mouse is free and the game takes no
/// keys (as for its own text boxes); [Esc] or [E] ends it. Its widgets are <see cref="MarkerPanelView"/>'s, on the game's own dialog canvas; never on a dedicated
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

    // A slider moved: the values the sliders stand at taken for the next one, or written into the placed one; one
    // refused (the player may not change it), it is said why and the sliders go back to what is kept.
    private void Slid()
    {
        if (Placed && !_target.IsValid)
        {
            Close();
            return;
        }

        SourceShape shape = MarkerSliders.Slid(KeptShape, (int)_view.Width.Slider.value, (int)_view.Spread.Slider.value, (int)_view.Throw.Slider.value);
        float rate = MarkerSliders.SlidRate((int)_view.Power.Slider.value, KeptRate, _drain);
        float radius = MarkerSliders.SlidReach((int)_view.Reach.Slider.value, KeptRadius);
        if (Placed)
        {
            ChangeTarget(rate, radius, shape);
        }
        else
        {
            NextPlacement next = HoePieces.Next;
            bool taken = next.TrySetRate(_drain, rate, out string note) & (!_drain || next.TrySetRadius(radius, out note));
            if (!_drain)
            {
                next.SetShape(shape.AllRound, shape.Width, shape.Spread, shape.Throw);
            }

            Say(note, taken);
        }

        ShowValues();
    }

    private void ChangeTarget(float rate, float radius, SourceShape shape)
    {
        if (Commands.MarkerRefusal(CommandRights.MarkerUse.Change) is { } refusal)
        {
            Say(refusal, false);
            return;
        }

        if (!PrivateArea.CheckAccess(_target.transform.position, 0f, false))
        {
            Say(Localization.instance.Localize("$msg_privatezone"), false);
            return;
        }

        bool taken = MarkerValues.TryRate(rate, _drain, out float used, out string note);
        if (taken && !(MarkerValues.Same(used, _target.Rate) && (!_drain || MarkerValues.Same(radius, _target.Radius))))
        {
            _target.Change(used, radius);
        }

        if (!_drain && shape != _target.KeptShape)
        {
            _target.ChangeShape(shape);
        }

        Say(note, taken);
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

        if (!ZInput.GetButtonDown("Use") || Hud.IsPieceSelectionVisible() || ClosedThisFrame)
        {
            return;
        }

        // [E] aimed at a placed source or drain opens the panel for it: in build mode, where alone they show, the game
        // hovers nothing, and its own [E] never reaches them.
        Player player = Player.m_localPlayer;
        if (HoePieces.AimedMarker(player) is { } aimed)
        {
            ZInput.ResetButtonStatus("Use");
            aimed.Interact(player, false, false);
            return;
        }

        // [E] aimed at something the game uses (a door, a chest) is the game's: one press must not both use it and open
        // the panel, whichever of the two is asked first.
        if (!AimedAtUsable())
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
        _view.Hint.text = MarkerValues.SliderHint(drain);
        _view.Layout(drain, Placed);
        Say(null, true);
        ShowValues();
        UpdateKeys();
    }

    // The values kept: the placed one's (an all-round source's with its outlet's, kept for a turn back), or the next
    // one's.
    private float KeptRate => Placed ? _target.Rate : HoePieces.Next.Rate(_drain);

    private float KeptRadius => Placed ? _target.Radius : HoePieces.Next.Radius;

    private SourceShape KeptShape => Placed ? _target.KeptShape : HoePieces.Next.Shape;

    // The values kept (the placed one's, or the next one's) on the sliders and beside them, and a source's drawings.
    private void ShowValues()
    {
        if (Placed && !_target.IsValid)
        {
            return;
        }

        float rate = KeptRate;
        float radius = KeptRadius;
        _view.Power.Slider.SetValueWithoutNotify(MarkerSliders.RateNotch(rate, _drain));
        _view.Power.Value.text = MarkerValues.RateText(rate, _drain);
        if (_drain)
        {
            _view.Reach.Slider.SetValueWithoutNotify(MarkerSliders.ReachNotch(radius));
            _view.Reach.Value.text = MarkerValues.Format(MarkerSliders.Reach(MarkerSliders.ReachNotch(radius))) + " m";
            return;
        }

        SourceShape shape = KeptShape;
        _view.Width.Slider.SetValueWithoutNotify(MarkerSliders.WidthNotch(shape.Width));
        _view.Spread.Slider.SetValueWithoutNotify(MarkerSliders.SpreadNotch(shape));
        _view.Throw.Slider.SetValueWithoutNotify(MarkerSliders.ThrowNotch(shape.Throw));
        _view.Width.Value.text = shape.Width > 0f ? MarkerValues.Format(shape.Width) + " m" : "a point";
        _view.Spread.Value.text = shape.AllRound ? "all round" : shape.Spread > 0f ? MarkerValues.Format(shape.Spread) + "°" : "straight";
        _view.Throw.Value.text = shape.Throw > 0f ? MarkerValues.Format(shape.Throw) + "°" : "ground";
        _view.Width.Dim(shape.AllRound);
        _view.Throw.Dim(shape.AllRound);
        _view.Kind.text = MarkerValues.KindLine(shape, rate);
        Draw(_view.Top, SourcePreview.Top(shape));
        Draw(_view.Side, SourcePreview.Side(shape, rate));
    }

    private static void Draw(RawImage image, byte[] pixels)
    {
        if (image.texture is Texture2D texture)
        {
            texture.LoadRawTextureData(pixels);
            texture.Apply(false);
        }
    }

    private void Edit()
    {
        _editing = true;
        _openedFrame = Time.frameCount;
        _view.Interactable(true);
        UpdateKeys();
    }

    // The values no longer changed: the sliders only show again.
    private void Stop()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        _view.Interactable(false);
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

        // The game's own, else one of the mod's. Unity's own null checks (its == on objects), not C#'s ?. and ??,
        // which the style rule would have: a destroyed object is null to Unity's and not to C#'s.
        TextInput dialog = TextInput.instance;
#pragma warning disable IDE0031 // Use null propagation: not on Unity objects.
        Canvas gameCanvas = dialog != null ? dialog.GetComponentInParent<Canvas>() : null;
#pragma warning restore IDE0031
        Canvas canvas = gameCanvas != null && gameCanvas.rootCanvas != null ? gameCanvas.rootCanvas : OwnCanvas();
        var view = MarkerPanelView.Build(canvas.transform, MarkerPanelView.Style.From(dialog));
        MarkerPanel panel = view.Root.AddComponent<MarkerPanel>();
        panel._view = view;
        view.Remove.onClick.AddListener(panel.RemoveTarget);
        foreach (MarkerPanelView.Row row in new[] { view.Width, view.Spread, view.Throw, view.Power, view.Reach })
        {
            row.Slider.onValueChanged.AddListener(_ => panel.Slid());
        }
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
