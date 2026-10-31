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
// keyboard added when the game has none); the panel's sliders are moved to a notch as a player drags them.
public sealed partial class Recorder
{
    private const string SourceName = "FlowingWater_Source";

    private const string DrainName = "FlowingWater_Drain";

    private const string RemoverName = "FlowingWater_Remove";

    private const string AimedMaterial = "FlowingWater marker to remove";

    private const string PanelName = "FlowingWater panel";

    private static readonly int RateKey = "FW_Rate".GetStableHashCode();

    private static readonly int RadiusKey = "FW_Radius".GetStableHashCode();

    // The power slider's notches run even on a logarithm from 0.1 m³/s (a drain's first: everything) to the most,
    // 100, at notch 120: notch 75 gives 7.5 and 52 gives 2. The reach slider's are quarter metres: notch 12 is 3 m.
    private const int MostRateNotch = 120;

    private const int SevenAndAHalfNotch = 75;

    private const int TwoNotch = 52;

    private const int ThreeMetresNotch = 12;

    private int _passed;

    private int _failed;

    // The turn scenario (FW_REC_SCENARIO=turn): a source turned with the mouse wheel as it is placed with the hoe, as a
    // player turns any piece the hammer builds, faces the way its ghost was turned.
    private IEnumerator Turn(Site site)
    {
        Player player = Player.m_localPlayer;
        player.SetGodMode(true);
        Hud.instance.m_userHidden = false;
        yield return Go(site.At(0f));
        player.EquipItem(Tool(player, "Hoe"));
        yield return new WaitForSeconds(1f);
        Piece sourcePiece = ZNetScene.instance.GetPrefab(SourceName).GetComponent<Piece>();
        Check(player.InPlaceMode() && player.SetSelectedPiece(sourcePiece), "the source is chosen in the hoe in hand");
        Vector3 target = OnGround(site.At(3f));
        Aim(target);
        yield return new WaitForSeconds(1f);

        float notch = (float)AccessTools.Field(typeof(Player), "m_placeRotationDegrees").GetValue(player);
        float unturned = GhostYaw(player);
        yield return Scroll(1f);
        float turned = Mathf.DeltaAngle(unturned, GhostYaw(player));
        Check(Mathf.Abs(Mathf.Abs(turned) - notch) < 0.5f, $"the mouse wheel turns the source's ghost a notch (turned {turned:0.#} degrees, a notch {notch:0.#})");
        yield return Scroll(1f);
        turned = Mathf.DeltaAngle(unturned, GhostYaw(player));
        Check(Mathf.Abs(Mathf.Abs(turned) - (2f * notch)) < 0.5f, $"a second notch turns it as far again (turned {turned:0.#} degrees)");

        float ghostYaw = GhostYaw(player);
        bool placed = player.TryPlacePiece(sourcePiece);
        Vector3 ghostAt = Ghost(player);
        Check(placed, $"the source is placed (placement {player.GetPlacementStatus()})");
        yield return new WaitForSeconds(0.5f);
        ZNetView source = NearestMarker(SourceName, ghostAt, 1f);
        Check(source != null, $"a source stands where the ghost was ({ghostAt.ToString("F2")})");
        if (source != null)
        {
            float placedYaw = source.GetZDO().GetRotation().eulerAngles.y;
            Check(Mathf.Abs(Mathf.DeltaAngle(placedYaw, ghostYaw)) < 0.5f && Mathf.Abs(Mathf.DeltaAngle(placedYaw, unturned)) > 1f, $"the placed source faces the way its ghost was turned ({placedYaw:0.#} degrees, the ghost {ghostYaw:0.#}, unturned {unturned:0.#})");
        }

        yield return Snap("turn-source-placed");
        Note($"turn: {_passed} passed, {_failed} failed");
    }

    private IEnumerator Hoe(Site site)
    {
        Player player = Player.m_localPlayer;

        // Standing on the source, the player swims in its water and drowns once out of stamina, as the game wants:
        // not what is tested here.
        player.SetGodMode(true);

        // The panel stays hidden with the game's interface.
        Hud.instance.m_userHidden = false;
        yield return Go(site.At(0f));

        // 1. The hoe in hand, in build mode, the source chosen in it.
        GameObject hoePrefab = ObjectDB.instance.GetItemPrefab("Hoe");
        PieceTable hoeTable = hoePrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces;
        Check(hoeTable.m_pieces.Any(p => p.name == SourceName) && hoeTable.m_pieces.Any(p => p.name == DrainName) && hoeTable.m_pieces.Any(p => p.name == RemoverName), "the hoe's pieces hold the source, the drain and the remover");
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
        Note($"panel: power {Shown(panel, "Power")}, \"{Label(panel, "Hint")}\", keys \"{Label(panel, "Keys")}\"");
        Check(Row(panel, "Width").activeInHierarchy && !Row(panel, "Reach").activeInHierarchy, "the source's panel shows its shape's sliders, not a drain's reach");
        Check(!Bar(panel, "Power").interactable, "before [E] the sliders only show the next source's values");

        // 2 and 8. [E] gives the panel the keys and the mouse; W held then moves nothing; the power slider moved is
        // taken, from a source's least to the most.
        Vector3 standing = player.transform.position;
        yield return Press(Key.E);
        Check(TextInput.IsVisible() && Bar(panel, "Power").interactable, "[E] gives the sliders the mouse: the game takes the keys for them as for its own text box");
        yield return Hold(Key.W, 1f);
        Check(Vector3.Distance(standing, player.transform.position) < 0.05f, $"W held while the panel has the keys does not move the player (moved {Vector3.Distance(standing, player.transform.position):0.00} m)");
        Slide(panel, "Power", MostRateNotch);
        Check(Shown(panel, "Power") == "100 m³/s", $"the power slider's last notch is the most, 100 m³/s (shows \"{Shown(panel, "Power")}\")");
        Slide(panel, "Power", 0);
        Check(Shown(panel, "Power") == "0.1 m³/s", $"its first notch is a source's least, 0.1 m³/s: a source always gives water (shows \"{Shown(panel, "Power")}\")");
        Slide(panel, "Power", SevenAndAHalfNotch);
        Check(Shown(panel, "Power") == "7.5 m³/s", $"the power slid to 7.5 m³/s shows it (shows \"{Shown(panel, "Power")}\")");
        yield return Snap("hoe-1-panel-source");
        yield return Press(Key.E);
        Check(!TextInput.IsVisible() && !Bar(panel, "Power").interactable, "[E] again gives the keys back to the game, the sliders only showing");
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
        Check(Label(panel, "Title") == "Next drain" && Row(panel, "Reach").activeInHierarchy && !Row(panel, "Width").activeInHierarchy, "the panel shows the next drain's values, its reach and no shape");
        yield return Press(Key.E);
        Slide(panel, "Power", 0);
        Slide(panel, "Reach", ThreeMetresNotch);
        Check(Shown(panel, "Power") == "all" && Shown(panel, "Reach") == "3 m", $"the drain's power at its left end takes all, its reach 3 m (\"{Shown(panel, "Power")}\", \"{Shown(panel, "Reach")}\")");
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

        // 5. The hoe put away, the placed source is hidden; back in hand with the source chosen, it shows, and [E] aimed
        // at it opens the panel for it (not the next one's); its rate changed to 2.
        player.UnequipItem(hoe);
        yield return Go(sourceAt - (site.Down * 2.5f));
        Aim(sourceAt + (Vector3.up * 0.3f));
        yield return new WaitForSeconds(1f);
        Check(!Drawn(source) && player.GetHoverObject()?.GetComponentInParent<ZNetView>() != source, "the hoe put away, the placed source is hidden and not aimed at");
        player.EquipItem(hoe);
        yield return new WaitForSeconds(1f);
        player.SetSelectedPiece(sourcePiece);
        Aim(sourceAt + (Vector3.up * 0.3f));
        yield return new WaitForSeconds(1f);
        Check(Drawn(source), "the hoe in hand with the source chosen, the placed source shows");
        // In build mode the game hovers nothing: the hoe aims at the source, and its text shows under the crosshair.
        Piece aimedPiece = player.GetHoveringPiece();
        GameObject hover = aimedPiece == null ? null : aimedPiece.gameObject;
        Check(hover != null && hover.GetComponent<ZNetView>() == source, $"aiming at the source aims the hoe at it ({(hover == null ? "nothing" : hover.name)})");
        string hoverText = Hud.instance.m_hoverName.text;
        Check(hoverText.Contains("7.5 m³/s") && hoverText.Contains("Settings"), $"the text under the crosshair says its rate and the key for its settings (\"{hoverText.Replace("\n", " | ")}\")");
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
            Check(panel != null && Label(panel, "Title") == "This source", $"[E] on the source opens its panel, not the next one's (\"{(panel == null ? "none" : Label(panel, "Title"))}\")");
        }

        if (panel != null)
        {
            Check(Label(panel, "Title") == "This source" && Shown(panel, "Power") == "7.5 m³/s", $"the panel is this source's, 7.5 m³/s (\"{Label(panel, "Title")}\", \"{Shown(panel, "Power")}\")");
            Slide(panel, "Power", TwoNotch);
            yield return new WaitForSeconds(0.5f);
            Check(Mathf.Approximately(source.GetZDO().GetFloat(RateKey, -1f), 2f), $"the source's object now says 2 m³/s ({source.GetZDO().GetFloat(RateKey, -1f)})");
            yield return Snap("hoe-5-panel-placed");
            yield return Press(Key.Escape);
            yield return new WaitForSeconds(0.5f);
            Check(BackToNext(), $"[Esc] closes this source's panel: the next one's shows again, only showing ({PanelState()})");
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

        // 7. The drain removed with the hoe's remover: chosen, it shows the drain, red when aimed at; the floor it stood
        // on is not the hoe's to remove.
        Piece removerPiece = hoeTable.m_pieces.FirstOrDefault(p => p.name == RemoverName)?.GetComponent<Piece>();
        if (drain != null && removerPiece != null)
        {
            Vector3 drainAt = drain.transform.position;

            // Uphill of the drain, on the ground: the source's water has had a minute to run down by now, and the floor,
            // a metre up with nothing under it, may have broken (the drain stays where it was placed).
            yield return Go(floorSpot - (site.Down * 3f));
            Check(player.SetSelectedPiece(removerPiece), "the remover is chosen in the hoe");
            Aim(drainAt + (Vector3.up * 0.3f));
            yield return new WaitForSeconds(1f);
            Check(Drawn(drain) && drain.GetComponentInChildren<Renderer>().sharedMaterial.name == AimedMaterial, "the remover chosen, the drain aimed at shows red");
            Check(GameObject.Find(PanelName) == null, "the remover chosen, no panel shows");
            yield return Snap("hoe-7-remover");
            bool removed = player.TryPlacePiece(removerPiece);
            yield return new WaitForSeconds(1f);
            Check(removed && (drain == null || !drain.IsValid()) && NearestMarker(DrainName, drainAt, 0.5f) == null, $"the remover takes the drain away (removed {removed})");
            if (floor != null)
            {
                Aim(floor.transform.position);
                yield return new WaitForSeconds(1f);
                bool floorRemoved = player.TryPlacePiece(removerPiece);
                yield return new WaitForSeconds(0.5f);
                Check(!floorRemoved && floor != null, $"the remover leaves the game's pieces alone (the floor removed {floorRemoved})");
            }
        }
        else
        {
            Check(false, $"the drain is removed with the hoe's remover (drain {(drain == null ? "gone" : "placed")}, remover {(removerPiece == null ? "missing" : "found")})");
        }

        // 8. Two more sources placed with the hoe on open ground: the first removed with the hammer as any piece, the
        // second with its panel's Remove button.
        foreach (bool byHammer in new[] { true, false })
        {
            string which = byHammer ? "the hammer's source" : "the panel's source";
            float along = byHammer ? 12f : 18f;
            Vector3 spot = OnGround(site.At(8f) - (site.Across * along));
            for (float across = along + 8f; Location.IsInsideNoBuildLocation(spot) && across <= 80f; across += 8f)
            {
                spot = OnGround(site.At(8f) - (site.Across * across));
            }

            Check(!Location.IsInsideNoBuildLocation(spot), $"{which}'s place may be built on ({spot.ToString("F2")})");
            yield return Go(spot - (site.Down * 3f));
            player.EquipItem(hoe);
            yield return new WaitForSeconds(1f);
            Check(player.SetSelectedPiece(sourcePiece), $"the source is chosen in the hoe again for {which}");
            Aim(spot);
            yield return new WaitForSeconds(1f);
            placed = player.TryPlacePiece(sourcePiece);
            ghostAt = Ghost(player);
            yield return new WaitForSeconds(0.5f);
            ZNetView more = NearestMarker(SourceName, ghostAt, 1f);
            Check(placed && more != null, $"{which} is placed ({player.GetPlacementStatus()}, at {ghostAt.ToString("F2")})");
            if (more == null)
            {
                continue;
            }

            Vector3 moreAt = more.transform.position;
            if (byHammer)
            {
                // The hammer in hand shows it too, red where it aims, and removes it as any piece.
                ItemDrop.ItemData hammer = Tool(player, "Hammer");
                player.EquipItem(hammer);
                yield return new WaitForSeconds(1f);
                Aim(moreAt + (Vector3.up * 0.3f));
                yield return new WaitForSeconds(1f);
                Check(Drawn(more) && more.GetComponentInChildren<Renderer>().sharedMaterial.name == AimedMaterial, "the hammer in hand shows the source, red where it aims");
                yield return Snap("hoe-8-hammer");
                bool removed = (bool)AccessTools.Method(typeof(Player), "RemovePiece").Invoke(player, []);
                yield return new WaitForSeconds(1f);
                Check(removed && (more == null || !more.IsValid()) && NearestMarker(SourceName, moreAt, 0.5f) == null, $"the hammer takes the source away (removed {removed})");
                player.UnequipItem(hammer);
                continue;
            }

            player.SetSelectedPiece(sourcePiece);
            Aim(moreAt + (Vector3.up * 0.3f));
            yield return new WaitForSeconds(1f);
            yield return Press(Key.E);
            yield return new WaitForSeconds(0.3f);
            panel = GameObject.Find(PanelName);
            Transform removeButton = panel == null ? null : panel.transform.Find("Remove");
            Check(panel != null && removeButton != null && removeButton.gameObject.activeInHierarchy, "[E] on the source opens its panel, with its Remove button");
            if (removeButton != null)
            {
                yield return Snap("hoe-8-panel-remove");
                removeButton.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                yield return new WaitForSeconds(1f);
                Check((more == null || !more.IsValid()) && NearestMarker(SourceName, moreAt, 0.5f) == null, "the Remove button takes the source away");
                Check(BackToNext() && !TextInput.IsVisible(), $"its panel closes with it and gives the keys back to the game, the next one's showing ({PanelState()})");
            }
        }

        player.UnequipItem(hoe);

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

    // Whether a placed source's or drain's sphere is drawn.
    private static bool Drawn(ZNetView marker) => marker != null && marker.GetComponentInChildren<Renderer>() is { } sphere && sphere.enabled;

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

    // The way the placing ghost is turned round the vertical, degrees (0 with none).
    private static float GhostYaw(Player player) => ((GameObject)AccessTools.Field(typeof(Player), "m_placementGhost").GetValue(player))?.transform.eulerAngles.y ?? 0f;

    // The mouse wheel turned so many notches up (a mouse added when the game has none), as a player turns a piece.
    private static IEnumerator Scroll(float notches)
    {
        Mouse mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
        InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0f, 120f * notches) });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState());
        yield return new WaitForSeconds(0.5f);
    }

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

    private static string Label(GameObject panel, string name) => panel.transform.Find(name).GetComponent<TMP_Text>().text;

    // Whether the panel of a placed one closed, the source still the hoe's piece: the next one's panel shows in its
    // place, its sliders only showing.
    private static bool BackToNext() => GameObject.Find(PanelName) is { } panel && Label(panel, "Title") == "Next source" && !Bar(panel, "Power").interactable;

    private static string PanelState() => GameObject.Find(PanelName) is { } panel ? $"\"{Label(panel, "Title")}\", sliders {(Bar(panel, "Power").interactable ? "taking the mouse" : "only showing")}" : "no panel";

    // A slider's row in the panel (its label, the slider and the value beside it).
    private static GameObject Row(GameObject panel, string name) => panel.transform.Find(name + " row").gameObject;

    private static UnityEngine.UI.Slider Bar(GameObject panel, string name) => Row(panel, name).transform.Find("Slider").GetComponent<UnityEngine.UI.Slider>();

    // The value shown beside a slider.
    private static string Shown(GameObject panel, string name) => Row(panel, name).transform.Find("Value").GetComponent<TMP_Text>().text;

    // A slider moved to a notch, as a player drags it: the panel takes what it stands at as it moves.
    private void Slide(GameObject panel, string name, int notch)
    {
        Bar(panel, name).value = notch;
        Note($"slid {name} to notch {notch}: shows \"{Shown(panel, name)}\", \"{Label(panel, "Status")}\"");
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
