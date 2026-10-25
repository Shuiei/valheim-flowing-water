using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlowingWater.Client;

/// <summary>
/// The widgets of the source and drain panel (<see cref="MarkerPanel"/>), built in code on the game's own canvas in its
/// font, sprites and colours: a title, a rate field with its label and hint, a radius field with its own, a status line,
/// the keys and a Remove button. What they show and do is the panel's; this only makes and lays them out.
/// </summary>
internal sealed class MarkerPanelView
{
    /// <summary>The panel's object, under the game's canvas.</summary>
    public const string ObjectName = "FlowingWater panel";

    /// <summary>The colour of the hints, and of a status that was taken as typed.</summary>
    public static readonly Color Plain = new(0.85f, 0.85f, 0.85f);

    private const string RateName = "Rate";

    private const string RadiusName = "Radius";

    private const string RemoveName = "Remove";

    // The panel's size and its rows, canvas units from its top left: the title, then each field with its label beside
    // and its hint under it, then the status, the keys and (a placed one's) the Remove button at the foot.
    private const float Width = 360f;

    private const float Gutter = 18f;

    private const float FieldWidth = 110f;

    private const float ButtonWidth = 140f;

    private const float TitleTop = 16f;

    private const float TitleHeight = 34f;

    private const float TitleSize = 26f;

    private const float RateLabelTop = 64f;

    private const float RateFieldTop = 60f;

    private const float RateHintTop = 96f;

    private const float RadiusLabelTop = 128f;

    private const float RadiusFieldTop = 124f;

    private const float RadiusHintTop = 160f;

    // Where the status goes: under the radius for a drain, under the rate for a source.
    private const float StatusTopDrain = 196f;

    private const float StatusTopSource = 132f;

    private const float StatusHeight = 40f;

    private const float KeysBelowStatus = 44f;

    private const float KeysHeight = 24f;

    private const float RemoveBelowStatus = 76f;

    private const float LabelHeight = 30f;

    private const float HintHeight = 22f;

    private const float FieldHeight = 34f;

    private const float TextSize = 20f;

    private const float HintSize = 16f;

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

    public TextMeshProUGUI RateHint { get; private set; }

    public TextMeshProUGUI RadiusLabel { get; private set; }

    public TextMeshProUGUI RadiusHint { get; private set; }

    public TextMeshProUGUI Status { get; private set; }

    public TextMeshProUGUI Keys { get; private set; }

    public TMP_InputField Rate { get; private set; }

    public TMP_InputField Radius { get; private set; }

    public Button Remove { get; private set; }

    /// <summary>The panel's widgets under the canvas, in this style; built inactive, so that the fields wake up whole.</summary>
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
        _ = Label(root.transform, "Rate label", style, "Rate", RateLabelTop);
        view.Rate = Field(root.transform, RateName, style, RateFieldTop);
        view.RateHint = Text(root.transform, "Rate hint", style, HintSize, Plain, RateHintTop, HintHeight);
        view.RadiusLabel = Label(root.transform, "Radius label", style, "Radius", RadiusLabelTop);
        view.Radius = Field(root.transform, RadiusName, style, RadiusFieldTop);
        view.RadiusHint = Text(root.transform, "Radius hint", style, HintSize, Plain, RadiusHintTop, HintHeight);
        view.Status = Text(root.transform, "Status", style, HintSize, Plain, StatusTopDrain, StatusHeight);
        view.Status.textWrappingMode = TextWrappingModes.Normal;
        view.Keys = Text(root.transform, "Keys", style, HintSize, style.Title, StatusTopDrain + KeysBelowStatus, KeysHeight);
        view.Remove = RemoveButton(root.transform, style);
        return view;
    }

    /// <summary>The widgets a drain's (with its radius) or a source's, a placed one's (with Remove) or the next one's.</summary>
    public void Layout(bool drain, bool placed)
    {
        RadiusLabel.gameObject.SetActive(drain);
        Radius.gameObject.SetActive(drain);
        RadiusHint.gameObject.SetActive(drain);
        // A placed one's Remove button goes under the keys, away from the fields.
        float top = drain ? StatusTopDrain : StatusTopSource;
        Place(Status.rectTransform, top, Width - (2f * Gutter), StatusHeight);
        Place(Keys.rectTransform, top + KeysBelowStatus, Width - (2f * Gutter), KeysHeight);
        Place((RectTransform)Remove.transform, top + RemoveBelowStatus, ButtonWidth, FieldHeight);
        Frame.sizeDelta = new Vector2(Width, top + (placed ? FootPlaced : Foot));
    }

    private static TextMeshProUGUI Label(Transform parent, string name, Style style, string text, float top)
    {
        TextMeshProUGUI label = Text(parent, name, style, TextSize, Color.white, top, LabelHeight);
        label.text = text;
        label.rectTransform.sizeDelta = new Vector2(Width - (2f * Gutter) - FieldWidth - 8f, LabelHeight);
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
        Place(rect, top, FieldWidth, FieldHeight);
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

        text.fontSize = TextSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        Fill(text.rectTransform);

        TMP_InputField field = go.AddComponent<TMP_InputField>();
        field.textViewport = areaRect;
        field.textComponent = text;
        field.targetGraphic = back;
        if (style.Font != null)
        {
            field.fontAsset = style.Font;
        }

        field.pointSize = TextSize;
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
        TextMeshProUGUI label = Text(go.transform, "Label", style, TextSize, style.ButtonText, 0f, FieldHeight);
        label.text = MarkerValues.RemoveLabel;
        label.alignment = TextAlignmentOptions.Center;
        Fill(label.rectTransform);
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
