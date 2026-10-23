using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace FlowingWater.Recorder;

// The hoe scenario (FW_REC_SCENARIO=hoe): sources and drains placed with the game's hoe and set in the mod's panel, as
// a player would, each check a PASS or FAIL line in the log. Keys are pressed through the game's input system (a
// keyboard added when the game has none); values are typed into the panel's fields as the panel takes a typed value.
public sealed partial class Recorder
{
    private const string SourceName = "FlowingWater_Source";

    private const string DrainName = "FlowingWater_Drain";

    private const string PanelName = "FlowingWater panel";

    private static readonly int RateKey = "FW_Rate".GetStableHashCode();

    private static readonly int RadiusKey = "FW_Radius".GetStableHashCode();

    private int _passed;

    private int _failed;

    private IEnumerator Hoe(Site site)
    {
        Player player = Player.m_localPlayer;
        // The panel stays hidden with the game's interface.
        Hud.instance.m_userHidden = false;
        yield return Go(site.At(0f));

        // 1. The hoe in hand, in build mode, the source chosen in it.
        GameObject hoePrefab = ObjectDB.instance.GetItemPrefab("Hoe");
        PieceTable hoeTable = hoePrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
        Check(hoeTable.m_pieces.Any(p => p.name == SourceName) && hoeTable.m_pieces.Any(p => p.name == DrainName), "the hoe's pieces hold the source and the drain");
        ItemDrop.ItemData hoe = Tool(player, "Hoe");
        player.EquipItem(hoe);
        yield return new WaitForSeconds(1f);
        Check(player.InPlaceMode(), "the hoe in hand is in build mode");
        Piece sourcePiece = ZNetScene.instance.GetPrefab(SourceName).GetComponent<Piece>();
        Piece drainPiece = ZNetScene.instance.GetPrefab(DrainName).GetComponent<Piece>();
        Check(player.SetSelectedPiece(sourcePiece), "the source is chosen in the hoe");
        Vector3 target = OnGround(site.At(3f));
        Aim(target);
        yield return new WaitForSeconds(1f);
        GameObject panel = GameObject.Find(PanelName);
        Check(panel != null, "the panel shows with the source chosen");
        if (panel == null)
        {
            yield break;
        }

        Check(Label(panel, "Title") == "Next source", $"the panel is the next source's (title \"{Label(panel, "Title")}\")");
        Note($"panel: rate {Field(panel, "Rate").text}, \"{Label(panel, "Rate hint")}\", keys \"{Label(panel, "Keys")}\"");

        // 2 and 8. [E] gives the panel the keys; W held then moves nothing; a rate typed is taken.
        Vector3 standing = player.transform.position;
        yield return Press(Key.E);
        Check(TextInput.IsVisible(), "[E] opens the panel's fields: the game takes the keys for them as for its own text box");
        yield return Hold(Key.W, 1f);
        Check(Vector3.Distance(standing, player.transform.position) < 0.05f, $"W held while the panel has the keys does not move the player (moved {Vector3.Distance(standing, player.transform.position):0.00} m)");
        TypeInto(panel, "Rate", "7.5");
        Check(Field(panel, "Rate").text == "7.5", $"the rate typed shows 7.5 (shows \"{Field(panel, "Rate").text}\")");
        TypeInto(panel, "Rate", "250");
        Check(Field(panel, "Rate").text == "100" && Label(panel, "Status").Contains("Held to 100"), $"a rate over the most is held to 100 and says so (\"{Field(panel, "Rate").text}\", \"{Label(panel, "Status")}\")");
        TypeInto(panel, "Rate", "0");
        Check(Field(panel, "Rate").text == "100" && Label(panel, "Status").Contains("above 0"), $"a source's rate of 0 is refused, the rate kept (\"{Field(panel, "Rate").text}\", \"{Label(panel, "Status")}\")");
        TypeInto(panel, "Rate", "7.5");
        yield return Snap("hoe-1-panel-source");
        yield return Press(Key.E);
        Check(!TextInput.IsVisible(), "[E] again gives the keys back to the game");
        Check(panel.activeInHierarchy && Label(panel, "Title") == "Next source", "the panel still shows the next source's values");

        // 3. Placed where the camera aims, with the panel's rate.
        bool placed = player.TryPlacePiece(sourcePiece);
        Vector3 ghostAt = Ghost(player);
        Check(placed, $"the source is placed (placement {player.GetPlacementStatus()})");
        yield return new WaitForSeconds(0.5f);
        ZNetView source = NearestMarker(SourceName, ghostAt, 1f);
        Check(source != null, $"a source stands where the ghost was ({ghostAt.ToString("F2")})");
        if (source == null)
        {
            yield break;
        }

        Vector3 sourceAt = source.transform.position;
        Check(Vector3.Distance(sourceAt, ghostAt) < 0.01f && Vector3.Distance(sourceAt, target) < 0.3f, $"the source is where the ghost was and the camera aimed ({sourceAt.ToString("F2")}, aimed at {target.ToString("F2")})");
        Check(Mathf.Approximately(source.GetZDO().GetFloat(RateKey, -1f), 7.5f), $"the placed source's object says 7.5 m³/s ({source.GetZDO().GetFloat(RateKey, -1f)})");
        yield return Snap("hoe-2-source-placed");

        // 4. A floor a metre over the ground, a drain placed on it with the panel's rate 0 and radius 3: across the slope,
        // out of any place the game forbids building in (the spawn's temple reaches far).
        Vector3 floorSpot = OnGround(site.At(0f) + (site.Across * 12f));
        for (float across = 20f; Location.IsInsideNoBuildLocation(floorSpot) && across <= 80f; across += 8f)
        {
            floorSpot = OnGround(site.At(0f) + (site.Across * across));
        }

        Check(!Location.IsInsideNoBuildLocation(floorSpot), $"the floor's place may be built on ({floorSpot.ToString("F2")})");
        yield return Go(floorSpot - (site.Across * 3f));
        GameObject floor = Instantiate(ZNetScene.instance.GetPrefab("wood_floor"), floorSpot + Vector3.up, Quaternion.identity);
        floor.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, player.GetPlayerID());
        yield return new WaitForSeconds(1f);
        bool onFloor = Physics.Raycast(floorSpot + (Vector3.up * 5f), Vector3.down, out RaycastHit hit, 10f, LayerMask.GetMask("piece"));
        Check(onFloor && hit.collider.GetComponentInParent<Piece>()?.gameObject == floor, $"the floor stands over the ground ({(onFloor ? hit.point.ToString("F2") : "nothing hit")})");
        float top = onFloor ? hit.point.y : floorSpot.y + 1f;
        Check(player.SetSelectedPiece(drainPiece), "the drain is chosen in the hoe");
        yield return new WaitForSeconds(0.5f);
        Check(Label(panel, "Title") == "Next drain" && Field(panel, "Radius").gameObject.activeInHierarchy, "the panel shows the next drain's values, its radius too");
        yield return Press(Key.E);
        TypeInto(panel, "Rate", "0");
        TypeInto(panel, "Radius", "3");
        Check(Field(panel, "Rate").text == "0" && Field(panel, "Radius").text == "3", $"the drain's rate 0 and radius 3 are taken (\"{Field(panel, "Rate").text}\", \"{Field(panel, "Radius").text}\")");
        yield return Press(Key.E);
        Vector3 floorTarget = new(floorSpot.x, top, floorSpot.z);
        Aim(floorTarget);
        yield return new WaitForSeconds(1f);
        yield return Snap("hoe-3-panel-drain");
        placed = player.TryPlacePiece(drainPiece);
        ghostAt = Ghost(player);
        Check(placed, $"the drain is placed (placement {player.GetPlacementStatus()})");
        yield return new WaitForSeconds(0.5f);
        ZNetView drain = NearestMarker(DrainName, ghostAt, 1f);
        Check(drain != null, $"a drain stands where the ghost was ({ghostAt.ToString("F2")})");
        if (drain != null)
        {
            float y = drain.transform.position.y;
            Check(Mathf.Abs(y - top) < 0.05f && y > floorSpot.y + 0.5f, $"the drain is on the floor's top ({y:0.00}; floor top {top:0.00}, ground {floorSpot.y:0.00})");
            Check(drain.GetZDO().GetFloat(RateKey, -1f) == 0f && Mathf.Approximately(drain.GetZDO().GetFloat(RadiusKey, -1f), 3f), $"the drain's object says rate 0, radius 3 ({drain.GetZDO().GetFloat(RateKey, -1f)}, {drain.GetZDO().GetFloat(RadiusKey, -1f)})");
            yield return Snap("hoe-4-drain-placed");
        }

        // 5. The hoe put away, [E] on the placed source opens the panel for it; its rate changed to 2.
        player.UnequipItem(hoe);
        yield return Go(sourceAt - (site.Down * 2.5f));
        Aim(sourceAt + (Vector3.up * 0.3f));
        yield return new WaitForSeconds(1f);
        GameObject hover = player.GetHoverObject();
        Check(hover != null && hover.GetComponentInParent<ZNetView>() == source, $"aiming at the source hovers it ({(hover == null ? "nothing" : hover.name)})");
        string hoverText = hover?.GetComponentInParent<Hoverable>()?.GetHoverText() ?? string.Empty;
        Check(hoverText.Contains("7.5 m³/s") && hoverText.Contains("Settings"), $"its hover text says its rate and the key for its settings (\"{hoverText.Replace("\n", " | ")}\")");
        StartCoroutine(Frames("hoe-film", 6f, 10));
        yield return Press(Key.E);
        yield return new WaitForSeconds(0.3f);
        panel = GameObject.Find(PanelName);
        if (panel == null && hover != null)
        {
            Check(false, "[E] on the source opens the panel; opened through the game's interaction instead");
            AccessTools.Method(typeof(Player), "Interact", [typeof(GameObject), typeof(bool), typeof(bool)]).Invoke(player, [hover, false, false]);
            yield return new WaitForSeconds(0.3f);
            panel = GameObject.Find(PanelName);
        }
        else
        {
            Check(panel != null, "[E] on the source opens the panel");
        }

        if (panel != null)
        {
            Check(Label(panel, "Title") == "This source" && Field(panel, "Rate").text == "7.5", $"the panel is this source's, rate 7.5 (\"{Label(panel, "Title")}\", \"{Field(panel, "Rate").text}\")");
            TypeInto(panel, "Rate", "2");
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Approximately(source.GetZDO().GetFloat(RateKey, -1f), 2f), $"the source's object now says 2 m³/s ({source.GetZDO().GetFloat(RateKey, -1f)})");
            yield return Snap("hoe-5-panel-placed");
            yield return Press(Key.Escape);
            yield return new WaitForSeconds(0.5f);
            Check(GameObject.Find(PanelName) == null, "[Esc] closes the panel");
            Check(!Menu.IsVisible(), "the [Esc] that closed the panel did not open the game's menu");
        }

        yield return new WaitForSeconds(3f);

        // 6. The source's water.
        yield return new WaitForSeconds(15f);
        yield return Shot(sourceAt + (site.Across * 8f) + new Vector3(0f, 6f, 0f) - (site.Down * 4f), sourceAt + (site.Down * 4f), "hoe-6-water");
        float deepest = 0f;
        for (float dz = -4f; dz <= 4f; dz += 1f)
        {
            for (float dx = -4f; dx <= 4f; dx += 1f)
            {
                Vector3 p = OnGround(sourceAt + new Vector3(dx, 0f, dz));
                deepest = Mathf.Max(deepest, Floating.GetLiquidLevel(p + (Vector3.up * 0.05f), 1f, LiquidType.Water) - p.y);
            }
        }

        Check(deepest > 0.02f, $"water runs from the source after 15 s (deepest within 4 m: {deepest:0.00} m)");

        // 7. The drain removed with the game's remove action, by the hoe if it removes pieces, else the hammer.
        string remover = hoeTable.m_canRemovePieces ? "Hoe" : "Hammer";
        Note($"the hoe {(hoeTable.m_canRemovePieces ? "removes" : "does not remove")} pieces: the drain is removed with the {remover}");
        if (drain != null)
        {
            Vector3 drainAt = drain.transform.position;

            // On the floor beside the drain: the source's water has had a minute to run down round it by now. A place
            // the floor is under, as a ray down finds it (the floor's own origin is not its middle).
            Vector3 stand = floorSpot;
            foreach (Vector3 offset in new[] { site.Across * 0.6f, -site.Across * 0.6f, site.Down * 0.6f, -site.Down * 0.6f })
            {
                if (Physics.Raycast(floorSpot + offset + (Vector3.up * 5f), Vector3.down, out RaycastHit under, 10f, LayerMask.GetMask("piece")) && under.collider.GetComponentInParent<Piece>()?.gameObject == floor)
                {
                    stand = floorSpot + offset;
                    break;
                }
            }

            yield return Go(stand, top);
            ItemDrop.ItemData tool = Tool(player, remover);
            player.EquipItem(tool);
            yield return new WaitForSeconds(1f);
            Aim(drainAt + (Vector3.up * 0.3f));
            yield return new WaitForSeconds(1f);
            bool removed = (bool)AccessTools.Method(typeof(Player), "RemovePiece").Invoke(player, []);
            yield return new WaitForSeconds(1f);
            Check(removed && (drain == null || !drain.IsValid()) && NearestMarker(DrainName, drainAt, 0.5f) == null, $"the {remover}'s remove takes the drain away (removed {removed})");
            player.UnequipItem(tool);
        }

        // 8. A second source placed with the hoe on open ground, then removed with its panel's Remove button.
        Vector3 spot = OnGround(site.At(8f) - (site.Across * 12f));
        for (float across = 20f; Location.IsInsideNoBuildLocation(spot) && across <= 80f; across += 8f)
        {
            spot = OnGround(site.At(8f) - (site.Across * across));
        }

        Check(!Location.IsInsideNoBuildLocation(spot), $"the second source's place may be built on ({spot.ToString("F2")})");
        yield return Go(spot - (site.Down * 3f));
        player.EquipItem(hoe);
        yield return new WaitForSeconds(1f);
        Check(player.SetSelectedPiece(sourcePiece), "the source is chosen in the hoe again");
        Aim(spot);
        yield return new WaitForSeconds(1f);
        placed = player.TryPlacePiece(sourcePiece);
        ghostAt = Ghost(player);
        yield return new WaitForSeconds(0.5f);
        ZNetView second = NearestMarker(SourceName, ghostAt, 1f);
        Check(placed && second != null, $"a second source is placed ({player.GetPlacementStatus()}, at {ghostAt.ToString("F2")})");
        player.UnequipItem(hoe);
        if (second != null)
        {
            Vector3 secondAt = second.transform.position;
            Aim(secondAt + (Vector3.up * 0.3f));
            yield return new WaitForSeconds(1f);
            yield return Press(Key.E);
            yield return new WaitForSeconds(0.3f);
            panel = GameObject.Find(PanelName);
            Transform removeButton = panel == null ? null : panel.transform.Find("Remove");
            Check(panel != null && removeButton != null && removeButton.gameObject.activeInHierarchy, "[E] on the second source opens its panel, with its Remove button");
            if (removeButton != null)
            {
                yield return Snap("hoe-7-panel-remove");
                removeButton.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                yield return new WaitForSeconds(1f);
                Check((second == null || !second.IsValid()) && NearestMarker(SourceName, secondAt, 0.5f) == null, "the Remove button takes the second source away");
                Check(GameObject.Find(PanelName) == null && !TextInput.IsVisible(), "the panel closes with it and gives the keys back to the game");
            }
        }

        _cameraAt = null;
        Note($"hoe: {_passed} checks passed, {_failed} failed");
    }

    private void Check(bool passed, string what)
    {
        if (passed)
        {
            _passed++;
        }
        else
        {
            _failed++;
        }

        Note((passed ? "PASS " : "FAIL ") + what);
    }

    // The player's tool of this name, given when missing.
    private ItemDrop.ItemData Tool(Player player, string name)
    {
        GameObject prefab = ObjectDB.instance.GetItemPrefab(name) ?? throw new InvalidOperationException($"no item {name} in the game");
        string shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
        ItemDrop.ItemData item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == shared);
        if (item == null)
        {
            Check(player.GetInventory().AddItem(prefab, 1), $"a {name} is given to the player");
            item = player.GetInventory().GetAllItems().First(i => i.m_shared.m_name == shared);
        }

        return item;
    }

    // The camera held just over the player's eyes, looking at the point: the game aims its tools and hover from it.
    private void Aim(Vector3 point)
    {
        _cameraAt = Player.m_localPlayer.m_eye.position + (Vector3.up * 0.6f);
        _cameraLook = point;
    }

    // The placement ghost where the game last put it.
    private static Vector3 Ghost(Player player) => ((GameObject)AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player))?.transform.position ?? Vector3.zero;

    private static Vector3 OnGround(Vector3 point)
    {
        if (ZoneSystem.instance.GetGroundHeight(point, out float ground))
        {
            point.y = ground;
        }

        return point;
    }

    private static ZNetView NearestMarker(string prefab, Vector3 point, float within) => FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
        .Where(n => n.IsValid() && n.GetZDO().GetPrefab() == prefab.GetStableHashCode() && Vector3.Distance(n.transform.position, point) <= within)
        .OrderBy(n => Vector3.Distance(n.transform.position, point)).FirstOrDefault();

    private static TMP_InputField Field(GameObject panel, string name) => panel.transform.Find(name).GetComponent<TMP_InputField>();

    private static string Label(GameObject panel, string name) => panel.transform.Find(name).GetComponent<TMP_Text>().text;

    // A value typed into a panel field and entered, as the panel takes what a player types.
    private void TypeInto(GameObject panel, string name, string value)
    {
        TMP_InputField field = Field(panel, name);
        field.text = value;
        field.onSubmit.Invoke(value);
        Note($"typed {value} into {name}: shows \"{field.text}\", \"{Label(panel, "Status")}\"");
    }

    // The view as it is (the camera held where it is).
    private IEnumerator Snap(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(_out, name + ".png"));
        Note($"shot {name}");
        yield return null;
    }

    // Frames of the view as it is, run beside the steps (name-NNN.png).
    private IEnumerator Frames(string name, float seconds, int fps)
    {
        int frames = Mathf.RoundToInt(seconds * fps);
        for (int i = 0; i < frames; i++)
        {
            float next = Time.time + (1f / fps);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"{name}-{i:000}.png"));
            while (Time.time < next)
            {
                yield return null;
            }
        }

        Note($"frames {name}: {frames} at {fps} a second");
    }

    private static Keyboard Keys()
    {
        if (Keyboard.current == null)
        {
            Current?.Note("no keyboard: one added for the keys pressed");
            InputSystem.AddDevice<Keyboard>();
        }

        return Keyboard.current;
    }

    // A key pressed and let go, a few frames each, through the game's input system.
    private static IEnumerator Press(Key key)
    {
        Keyboard keyboard = Keys();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        yield return null;
    }

    private static IEnumerator Hold(Key key, float seconds)
    {
        Keyboard keyboard = Keys();
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
        }

        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }
}
