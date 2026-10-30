using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlowingWater.Client;

/// <summary>
/// The widgets of the source and drain panel (<see cref="MarkerPanel"/>), built in code on the game's own canvas in its
/// font, sprites and colours: a title; for a source, two drawings of its water (from above and from the side) with a
/// line naming it, and a slider for each of its width, spread, throw and power; for a drain, sliders for its power
/// and reach; each slider with its label and value beside it, a hint under them, a status line, the keys and a Remove
/// button. What they show and do is the panel's; this only makes and lays them out.
/// </summary>
internal sealed class MarkerPanelView
{
    /// <summary>The panel's object, under the game's canvas.</summary>
    public const string ObjectName = "FlowingWater panel";

    /// <summary>The colour of the hints, and of a status that was taken as set.</summary>
    public static readonly Color Plain = new(0.85f, 0.85f, 0.85f);

    // A slider's label and value while what it sets does not apply.
    private static readonly Color DimColour = new(0.6f, 0.6f, 0.6f);

    private const string RemoveName = "Remove";

    // The panel's size and its rows, canvas units from its top left: the title; a source's drawings, captioned, and the
    // line naming its water; the sliders, each a row with its label left and its value right; a hint; the status, the
    // keys and (a placed one's) the Remove button at the foot.
    private const float PanelWidth = 360f;

    private const float Gutter = 18f;

    private const float LabelWidth = 66f;

    private const float ValueWidth = 88f;

    private const float ButtonWidth = 140f;

    private const float TitleTop = 16f;

    private const float TitleHeight = 34f;

    private const float TitleSize = 26f;

    private const float CaptionTop = 56f;

    private const float CaptionHeight = 18f;

    private const float DrawingTop = 76f;

    private const float DrawingWidth = 156f;

    private const float DrawingHeight = 117f;

    private const float KindTop = 198f;

    private const float KindHeight = 26f;

    // The first slider's row: a source's under its drawings, a drain's under the title.
    private const float RowsTopSource = 230f;

    private const float RowsTopDrain = 62f;

    private const float RowHeight = 34f;

    private const float RowStep = 40f;

    private const float HintHeight = 40f;

    private const float StatusHeight = 40f;

    private const float KeysBelowStatus = 44f;

    private const float KeysHeight = 24f;

    private const float RemoveBelowStatus = 76f;

    private const float FieldHeight = 34f;

    private const float TextSize = 20f;

    private const float HintSize = 16f;

    private const float CaptionSize = 14f;

    // What the panel's height adds under the status: room for the keys, and for the Remove button under them.
    private const float FootPlaced = 126f;

    private const float Foot = 80f;

    private const float FromScreenEdge = 40f;

    private MarkerPanelView()
    {
    }

    /// <summary>The panel's object (inactive until shown).</summary>
    public GameObject Root { get; private set; }

    /// <summary>The panel's frame, sized to what it shows.</summary>
    public RectTransform Frame { get; private set; }

    public TextMeshProUGUI Title { get; private set; }

    /// <summary>A source's water from above (its texture is the panel's to draw).</summary>
    public RawImage Top { get; private set; }

    /// <summary>A source's water from the side.</summary>
    public RawImage Side { get; private set; }

    /// <summary>The line naming a source's water, under its drawings.</summary>
    public TextMeshProUGUI Kind { get; private set; }

    public Row Width { get; private set; }

    public Row Spread { get; private set; }

    public Row Throw { get; private set; }

    public Row Power { get; private set; }

    public Row Reach { get; private set; }

    public TextMeshProUGUI Hint { get; private set; }

    public TextMeshProUGUI Status { get; private set; }

    public TextMeshProUGUI Keys { get; private set; }

    public Button Remove { get; private set; }

    // A source's widgets only, a drain's only.
    private GameObject[] _sourceOnly;

    private GameObject[] _drainOnly;

    /// <summary>The panel's widgets under the canvas, in this style; built inactive, so that the sliders wake up whole.</summary>
    public static MarkerPanelView Build(Transform canvas, Style style)
    {
        var view = new MarkerPanelView();
        var root = new GameObject(ObjectName, typeof(RectTransform));
        root.SetActive(false);
        root.transform.SetParent(canvas, false);
        var frame = (RectTransform)root.transform;
        frame.anchorMin = frame.anchorMax = new Vector2(1f, 0.5f);
        frame.pivot = new Vector2(1f, 0.5f);
        frame.anchoredPosition = new Vector2(-FromScreenEdge, 0f);
        Image back = root.AddComponent<Image>();
        Sprite(back, style.Background, new Color(0.1f, 0.08f, 0.06f, 0.92f));

        view.Root = root;
        view.Frame = frame;
        view.Title = Text(root.transform, "Title", style, TitleSize, style.Title, TitleTop, TitleHeight);
        TextMeshProUGUI above = Text(root.transform, "From above", style, CaptionSize, Plain, CaptionTop, CaptionHeight);
        above.text = "From above";
        TextMeshProUGUI beside = Text(root.transform, "From the side", style, CaptionSize, Plain, CaptionTop, CaptionHeight);
        beside.text = "From the side";
        Place(beside.rectTransform, Gutter + DrawingWidth + 12f, CaptionTop, DrawingWidth, CaptionHeight);
        view.Top = Drawing(root.transform, "Top view", Gutter);
        view.Side = Drawing(root.transform, "Side view", Gutter + DrawingWidth + 12f);
        view.Kind = Text(root.transform, "Kind", style, 18f, style.Title, KindTop, KindHeight);
        view.Width = Slider(root.transform, "Width", style, MarkerSliders.WidthNotches, 0);
        view.Spread = Slider(root.transform, "Spread", style, MarkerSliders.SpreadNotches, 0);
        view.Throw = Slider(root.transform, "Throw", style, MarkerSliders.ThrowNotches, 0);
        view.Power = Slider(root.transform, "Power", style, MarkerSliders.RateNotches, 0);
        view.Reach = Slider(root.transform, "Reach", style, MarkerSliders.ReachNotches, 1);
        view.Hint = Text(root.transform, "Hint", style, HintSize, Plain, 0f, HintHeight);
        view.Hint.textWrappingMode = TextWrappingModes.Normal;
        view.Status = Text(root.transform, "Status", style, HintSize, Plain, 0f, StatusHeight);
        view.Status.textWrappingMode = TextWrappingModes.Normal;
        view.Keys = Text(root.transform, "Keys", style, HintSize, style.Title, 0f, KeysHeight);
        view.Remove = RemoveButton(root.transform, style);
        view._sourceOnly = [above.gameObject, beside.gameObject, view.Top.gameObject, view.Side.gameObject, view.Kind.gameObject, view.Width.Root, view.Spread.Root, view.Throw.Root];
        view._drainOnly = [view.Reach.Root];
        return view;
    }

    /// <summary>The widgets a drain's or a source's, a placed one's (with Remove) or the next one's.</summary>
    public void Layout(bool drain, bool placed)
    {
        foreach (GameObject only in _sourceOnly)
        {
            only.SetActive(!drain);
        }

        foreach (GameObject only in _drainOnly)
        {
            only.SetActive(drain);
        }

        Row[] rows = drain ? [Power, Reach] : [Width, Spread, Throw, Power];
        float top = drain ? RowsTopDrain : RowsTopSource;
        foreach (Row row in rows)
        {
            row.Place(top);
            top += RowStep;
        }

        Place(Hint.rectTransform, Gutter, top, PanelWidth - (2f * Gutter), HintHeight);
        top += HintHeight + 4f;
        // A placed one's Remove button goes under the keys, away from the sliders.
        Place(Status.rectTransform, Gutter, top, PanelWidth - (2f * Gutter), StatusHeight);
        Place(Keys.rectTransform, Gutter, top + KeysBelowStatus, PanelWidth - (2f * Gutter), KeysHeight);
        Place((RectTransform)Remove.transform, Gutter, top + RemoveBelowStatus, ButtonWidth, FieldHeight);
        Frame.sizeDelta = new Vector2(PanelWidth, top + (placed ? FootPlaced : Foot));
    }

    /// <summary>Whether the sliders take the mouse and keys (while the panel's values are changed), else only show.</summary>
    public void Interactable(bool on)
    {
        foreach (Row row in new[] { Width, Spread, Throw, Power, Reach })
        {
            row.Slider.interactable = on;
        }
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
        Place(text.rectTransform, Gutter, top, PanelWidth - (2f * Gutter), height);
        return text;
    }

    // A drawing's frame, its texture drawn by the panel (SourcePreview's size, scaled to the frame).
    private static RawImage Drawing(Transform parent, string name, float left)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RawImage image = go.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.texture = new Texture2D(SourcePreview.Width, SourcePreview.Height, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Place(image.rectTransform, left, DrawingTop, DrawingWidth, DrawingHeight);
        return image;
    }

    // A slider's row: its label, the slider by whole notches from the first to the last, and its value.
    private static Row Slider(Transform parent, string label, Style style, int last, int first)
    {
        var go = new GameObject(label + " row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var row = new Row
        {
            Root = go,
            Label = Text(go.transform, "Label", style, TextSize, Color.white, 0f, RowHeight),
            Value = Text(go.transform, "Value", style, 18f, Color.white, 0f, RowHeight),
        };
        row.Label.text = label;
        Place(row.Label.rectTransform, 0f, 0f, LabelWidth, RowHeight);
        row.Value.alignment = TextAlignmentOptions.MidlineRight;
        Place(row.Value.rectTransform, PanelWidth - (2f * Gutter) - ValueWidth, 0f, ValueWidth, RowHeight);

        var bar = new GameObject("Slider", typeof(RectTransform));
        bar.transform.SetParent(go.transform, false);
        Place((RectTransform)bar.transform, LabelWidth + 8f, 0f, PanelWidth - (2f * Gutter) - LabelWidth - ValueWidth - 16f, RowHeight);
        Image track = Child(bar.transform, "Track", style.Field, new Color(0f, 0f, 0f, 0.6f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0.5f);
        trackRect.anchorMax = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(0f, 10f);

        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(bar.transform, false);
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(-12f, 10f);
        Image fill = Child(fillArea.transform, "Fill", null, new Color(style.Title.r, style.Title.g, style.Title.b, 0.55f));
        fill.rectTransform.sizeDelta = Vector2.zero;

        var handleArea = new GameObject("Handle Area", typeof(RectTransform));
        handleArea.transform.SetParent(bar.transform, false);
        var handleAreaRect = (RectTransform)handleArea.transform;
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.sizeDelta = new Vector2(-12f, 0f);
        Image handle = Child(handleArea.transform, "Handle", style.Button ?? style.Field, style.Title);
        handle.rectTransform.sizeDelta = new Vector2(14f, 0f);

        row.Slider = bar.AddComponent<Slider>();
        row.Slider.fillRect = fill.rectTransform;
        row.Slider.handleRect = handle.rectTransform;
        row.Slider.targetGraphic = handle;
        row.Slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
        row.Slider.wholeNumbers = true;
        row.Slider.minValue = first;
        row.Slider.maxValue = last;
        row.Slider.interactable = false;
        return row;
    }

    // An image stretched over its parent (anchors set by the caller after), in a sprite or a plain colour.
    private static Image Child(Transform parent, string name, Sprite sprite, Color plain)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image image = go.AddComponent<Image>();
        Sprite(image, sprite, plain);
        if (sprite != null)
        {
            image.color = plain.a < 1f ? Color.white : plain;
        }

        Fill(image.rectTransform);
        return image;
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
        TextMeshProUGUI label = Text(go.transform, "Label", style, TextSize, style.ButtonText, 0f, FieldHeight);
        label.text = MarkerValues.RemoveLabel;
        label.alignment = TextAlignmentOptions.Center;
        Fill(label.rectTransform);
        go.SetActive(false);
        return button;
    }

    // Placed from its parent's top left.
    private static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    // Stretched over its parent.
    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
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

    /// <summary>A slider's row: its label, its slider by whole notches and its value, placed together.</summary>
    public sealed class Row
    {
        public GameObject Root { get; set; }

        public TextMeshProUGUI Label { get; set; }

        public Slider Slider { get; set; }

        public TextMeshProUGUI Value { get; set; }

        /// <summary>The row placed this far from the panel's top.</summary>
        public void Place(float top) => MarkerPanelView.Place((RectTransform)Root.transform, Gutter, top, PanelWidth - (2f * Gutter), RowHeight);

        /// <summary>Its label and value dimmed (what it sets does not apply now: a source's width or throw all round), or not.</summary>
        public void Dim(bool dim)
        {
            Color colour = dim ? DimColour : Color.white;
            Label.color = colour;
            Value.color = colour;
        }
    }

    /// <summary>The look of the game's own text box (the sign's): its font, title colour and sprites; plain when not found.</summary>
    public sealed class Style
    {
        public TMP_FontAsset Font { get; private set; }

        public Color Title { get; private set; } = new(1f, 0.72f, 0.36f);

        public Sprite Background { get; private set; }

        public Sprite Field { get; private set; }

        public Sprite Button { get; private set; }

        public Color ButtonText { get; private set; } = Color.white;

        /// <summary>The style of the game's text box, or the plain one without it.</summary>
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
