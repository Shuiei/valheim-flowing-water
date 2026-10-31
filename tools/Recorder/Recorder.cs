using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater.Recorder;

// A test plugin, never shipped. Once the player is in a world it finds a steep slope and a gentle
// one near the spawn, runs every flowingwater command with several values there, and captures
// screenshots of each result from fixed camera places, plus short frame bursts to see the motion.
// Everything it does and every console answer goes to <FW_REC_OUT>/log.txt; the game quits when
// it is done. It does nothing unless FW_REC_OUT is set.
[BepInPlugin("Tie.FlowingWater.Recorder", "FlowingWaterRecorder", "1.0.0")]
[BepInDependency("Tie.FlowingWater")]
public sealed partial class Recorder : BaseUnityPlugin, IDisposable
{
    // The most a "!film" takes: frames a second, frames in all and seconds (five minutes, at up to 60 a second).
    private const int MaxFps = 60;

    private const int MaxFrames = 18000;

    private const float MaxSeconds = (float)MaxFrames / MaxFps;

    // How far from the given point "!remove" looks for the source, metres.
    private const float RemoveReach = 2f;

    // Names Windows keeps for devices.
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    // The recorder running, for the game's camera and console (patched, static); set and cleared by the recorder itself.
    private static Recorder Current { get; set; }

    // Where the camera is held, looking at what (none: the game's own camera).
    private Vector3? _cameraAt;

    private Vector3 _cameraLook;

    private string _out;

    private StreamWriter _log;

    private bool _started;

    private Harmony _harmony;

    private void Awake()
    {
        string folder = Environment.GetEnvironmentVariable("FW_REC_OUT");
        if (string.IsNullOrEmpty(folder))
        {
            Logger.LogInfo("FW_REC_OUT not set: recorder idle");
            return;
        }

        // Under Proton the game sees the Linux file system as drive Z:.
        _out = Application.platform == RuntimePlatform.WindowsPlayer ? "Z:" + folder.Replace('/', '\\') : folder;
        Directory.CreateDirectory(_out);
        _log = new StreamWriter(Path.Combine(_out, "log.txt"), false) { AutoFlush = true };
        Current = this;
        _harmony = new Harmony("Tie.FlowingWater.Recorder");
        _harmony.Patch(AccessTools.Method(typeof(GameCamera), "LateUpdate"), postfix: new HarmonyMethod(typeof(Recorder), nameof(CameraPostfix)));
        _harmony.Patch(AccessTools.Method(typeof(Terminal), nameof(Terminal.AddString), [typeof(string)]), postfix: new HarmonyMethod(typeof(Recorder), nameof(ConsolePostfix)));
        Note($"recorder ready, output {_out}, platform {Application.platform}, graphics {SystemInfo.graphicsDeviceType}");
    }

    private static void CameraPostfix(GameCamera __instance)
    {
        if (Current is { _cameraAt: { } at } recorder)
        {
            __instance.transform.position = at;
            __instance.transform.rotation = Quaternion.LookRotation(recorder._cameraLook - at);
        }
    }

    private static void ConsolePostfix(string text)
    {
        if (Current != null && text != null && text.StartsWith("flowingwater", StringComparison.Ordinal))
        {
            Current.Note("  console: " + text);
        }
    }

    private float _popupCheck;

    private readonly HashSet<int> _closed = new HashSet<int>();

    // Other client mods greet the player with popups (Seneal UI warns that the server lacks it);
    // they would sit in every shot. Modal or popup components are closed through their own Close
    // or Hide method, or switched off.
    private void DismissPopups()
    {
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (behaviour == null || !behaviour.isActiveAndEnabled)
            {
                continue;
            }

            Type type = behaviour.GetType();
            if (type.Assembly == typeof(Player).Assembly || !(type.Name.Contains("Modal") || type.Name.Contains("Popup")))
            {
                continue;
            }

            // A modal that says whether it is open (Seneal UI's does) is closed until it says no;
            // others once. A modal may ignore a close until its own short delay has passed.
            System.Reflection.PropertyInfo open = AccessTools.Property(type, "Open");
            if (open != null && open.PropertyType == typeof(bool) && open.GetGetMethod(true).IsStatic)
            {
                if (!(bool)open.GetValue(null, null))
                {
                    continue;
                }
            }
            else if (!_closed.Add(behaviour.GetInstanceID()))
            {
                continue;
            }

            System.Reflection.MethodInfo close = AccessTools.Method(type, "Close", Type.EmptyTypes) ?? AccessTools.Method(type, "Hide", Type.EmptyTypes);
            Note($"closing {type.FullName} on {behaviour.gameObject.name} ({(close == null ? "switched off" : close.Name)})");
            if (close != null && !close.IsStatic)
            {
                close.Invoke(behaviour, null);
            }
            else
            {
                behaviour.gameObject.SetActive(false);
            }
        }
    }

    private void Update()
    {
        if (_log != null && Player.m_localPlayer != null && Time.time > _popupCheck)
        {
            _popupCheck = Time.time + 1f;
            DismissPopups();
        }

        if (_log == null || _started || Player.m_localPlayer == null || !ZNetScene.instance || Console.instance == null)
        {
            return;
        }

        _started = true;
        StartCoroutine(Run());
    }

    /// <summary>
    /// Stops the run, closes the log and takes the patches off; Unity calls it as the recorder goes (OnDestroy). The run
    /// is stopped first: it writes to the log to its end.
    /// </summary>
    public void Dispose()
    {
        StopAllCoroutines();
        if (Current == this)
        {
            Current = null;
        }

        _harmony?.UnpatchSelf();
        _harmony = null;
        _log?.Dispose();
        _log = null;
    }

    // The recorder goes with the game (it quits) or with its plugin (unloaded).
    private void OnDestroy() => Dispose();

    private void Note(string text)
    {
        string line = $"[{Time.realtimeSinceStartup,7:0.0}] {text}";
        Logger.LogInfo(line);
        _log?.WriteLine(line);
    }

    // Runs the steps, stepping any coroutine they start itself (not handing it to Unity), so that an exception at any
    // depth ends here, logged as FAILED.
    private IEnumerator Run()
    {
        var running = new Stack<IEnumerator>();
        running.Push(Steps());
        while (running.Count > 0)
        {
            object current;
            try
            {
                if (!running.Peek().MoveNext())
                {
                    running.Pop();
                    continue;
                }

                current = running.Peek().Current;
            }
            catch (Exception e)
            {
                Note("FAILED: " + e);
                break;
            }

            if (current is IEnumerator nested)
            {
                running.Push(nested);
                continue;
            }

            yield return current;
        }

        Note("done, quitting");
        _log.Flush();
        Application.Quit();
    }

    private IEnumerator Steps()
    {
        Player player = Player.m_localPlayer;
        Note($"in world as {player.GetPlayerName()} at {player.transform.position}");

        // A character new to the world rides in with the Valkyrie first and cannot teleport until
        // it has landed.
        float landBy = Time.time + 240f;
        while ((player.InIntro() || player.IsTeleporting()) && Time.time < landBy)
        {
            yield return new WaitForSeconds(1f);
        }

        if (player.InIntro() || player.IsTeleporting())
        {
            throw new InvalidOperationException("the player never landed (intro or teleport still running after 240 s)");
        }

        Note($"landed at {player.transform.position}");
        Camera view = GameCamera.instance.GetComponent<Camera>();
        Note($"camera renders {view.actualRenderingPath}, depth texture {view.depthTextureMode}");
        // Saved with its place in this world, the character starts there next time: no Valkyrie.
        Game.instance.SavePlayerProfile(true);
        yield return new WaitForSeconds(5f);
        player.SetGodMode(true);
        player.SetGhostMode(true);
        EnvMan.instance.m_debugTimeOfDay = true;
        EnvMan.instance.m_debugTime = 0.45f;
        EnvMan.instance.m_debugEnv = "Clear";
        Hud.instance.m_userHidden = true;

        // Where the sites are looked for: the landing point (the Valkyrie's varies), or a fixed point so
        // that runs on the same world use the same sites.
        Vector3 spawn = player.transform.position;
        string around = Environment.GetEnvironmentVariable("FW_REC_AROUND");
        if (!string.IsNullOrEmpty(around))
        {
            string[] parts = around.Split(',');
            spawn = new Vector3(float.Parse(parts[0], CultureInfo.InvariantCulture), 0f, float.Parse(parts[1], CultureInfo.InvariantCulture));
            Note($"sites looked for around {spawn}");
        }
        // A scenario of its own (FW_REC_SCENARIO), else every command below. Those on a test world's own places
        // need no slope (a flat world has none, and the search would stop the run).
        string chosen = Environment.GetEnvironmentVariable("FW_REC_SCENARIO");
        Site steep = null;
        Site gentle = null;
        bool ownPlaces = chosen is "prep" or "pools" or "cascade" or "diag" or "flood" or "at"
            || (chosen == "perf" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FW_REC_PERF")));
        if (!ownPlaces)
        {
            steep = FindSite(spawn, 0.35f, 0.75f, true) ?? FindSite(spawn, 0.3f, 0.8f, false);
            gentle = FindSite(spawn, 0.06f, 0.18f, true) ?? FindSite(spawn, 0.05f, 0.2f, false);
            Note($"steep site {steep}");
            Note($"gentle site {gentle}");
        }

        IEnumerator scenario = chosen switch
        {
            "cascade" => Cascade(spawn),
            "diag" => Diag(spawn),
            "flood" => Flood(spawn),
            "look" => Look(steep, gentle),
            "prep" => Prep(),
            "pools" => Pools(),
            "at" => At(),
            "hoe" => Hoe(gentle),
            "turn" => Turn(gentle),
            "perf" => Perf(spawn, steep, gentle),
            "grid" => Grid(steep, gentle, Environment.GetEnvironmentVariable("FW_REC_PLACE") != "0"),
            "erosion" => Erosion(steep, Environment.GetEnvironmentVariable("FW_REC_PLACE") != "0"),
            "gpu" => Gpu(steep, gentle, FindSite(spawn + new Vector3(0f, 0f, -150f), 0.02f, 0.09f, true) ?? FindSite(spawn + new Vector3(150f, 0f, 0f), 0.02f, 0.09f, false)),
            "ledge" => Ledge(FindSite(spawn + new Vector3(0f, 0f, -150f), 0.02f, 0.09f, true) ?? FindSite(spawn + new Vector3(150f, 0f, 0f), 0.02f, 0.09f, false)),
            _ => null,
        };
        if (scenario != null)
        {
            while (scenario.MoveNext())
            {
                yield return scenario.Current;
            }

            Note("all steps run");
            yield break;
        }

        // Steep slope: a source and how the water runs over time.
        yield return Go(steep.At(0f));
        Command("flowingwater info");
        Command("flowingwater remove");
        Command("flowingwater nonsense");
        Command("flowingwater source 3");
        foreach (float wait in new[] { 10f, 20f, 30f, 60f })
        {
            yield return new WaitForSeconds(wait);
            yield return Views(steep, $"steep-t{Mathf.RoundToInt(Time.timeSinceLevelLoad)}");
        }

        yield return Burst(steep.Behind(), steep.At(14f), "steep-motion", 4f, 12);
        Command("flowingwater info");
        Command("flowingwater debug");
        yield return Go(steep.At(12f));
        Command("flowingwater info");
        yield return Go(steep.At(25f));
        Command("flowingwater info");

        // Drawing commands.
        Command("flowingwater flat");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-flat-on");
        Command("flowingwater flat");
        yield return null;
        yield return Shot(steep.Behind(), steep.At(14f), "steep-flat-off");
        Command("flowingwater lift abc");
        foreach (string lift in new[] { "1", "3", "0" })
        {
            Command("flowingwater lift " + lift);
            yield return Shot(steep.Side(), steep.At(12f), "steep-lift-" + lift);
        }

        // Material commands, each with a value that shows, then back.
        Command("flowingwater mat");
        Command("flowingwater mat custom list");
        Command("flowingwater mat custom _Bogus 1");
        Command("flowingwater mat custom _ShallowColor");
        Command("flowingwater mat custom _ShallowColor 0.8 0.1 0.1 0.6");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-mat-red");
        Command("flowingwater mat custom _ShallowColor 0.2 0.32 0.3 0.06");
        Command("flowingwater mat custom _NormalStrength 1.6");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-mat-normal-1.6");
        Command("flowingwater mat custom _NormalStrength 0.45");
        Command("flowingwater mat custom _FoamSpeed 0.8");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-mat-foam");
        Command("flowingwater mat custom _FoamSpeed 4");
        Command("flowingwater mat custom _Reflection 0");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-mat-reflection-0");
        Command("flowingwater mat custom _Reflection 1");
        yield return Shot(steep.Behind(), steep.At(14f), "steep-mat-reflection-1");
        Command("flowingwater mat custom _Reflection 0.6");
        Command("flowingwater mat custom _FlowSpeed 3");
        yield return Burst(steep.Behind(), steep.At(14f), "steep-flowspeed-3", 3f, 12);
        Command("flowingwater mat custom _FlowSpeed 1");
        Command("flowingwater mat still _DepthFade 5");

        // Drains where the water actually runs (not where the generated slope says it should): one
        // that takes everything, one that takes 0.5 m3/s; then removing both.
        Vector3 firstDrain = Wet(steep, 14f, 26f);
        Vector3 secondDrain = Wet(steep, 28f, 45f);
        yield return Go(firstDrain);
        Command("flowingwater info");
        Command("flowingwater drain");
        Command("flowingwater drain -1");
        yield return Go(secondDrain);
        Command("flowingwater drain 0.5");
        yield return new WaitForSeconds(45f);
        yield return Views(steep, "steep-drains");
        Command("flowingwater info");
        Command("flowingwater remove");
        yield return Go(firstDrain);
        Command("flowingwater info");
        Command("flowingwater remove");

        // The source gone: the water runs off and settles.
        yield return Go(steep.At(0f));
        Command("flowingwater remove");
        Command("flowingwater remove");
        yield return new WaitForSeconds(40f);
        yield return Views(steep, "steep-after-remove");
        Command("flowingwater info");

        // Gentle slope: a strong source and a weak one, and bad rates.
        yield return Go(gentle.At(0f));
        Command("flowingwater source 8");
        yield return Go(gentle.At(0f) + (gentle.Across * 12f));
        Command("flowingwater source 0.5");
        yield return Go(gentle.At(0f) + (gentle.Across * 24f));
        Command("flowingwater source abc");
        Command("flowingwater remove");
        Command("flowingwater source -3");
        Command("flowingwater remove");
        yield return Go(gentle.At(6f));
        foreach (float wait in new[] { 20f, 40f, 60f })
        {
            yield return new WaitForSeconds(wait);
            yield return Views(gentle, $"gentle-t{Mathf.RoundToInt(Time.timeSinceLevelLoad)}");
        }

        yield return Burst(gentle.Behind(), gentle.At(14f), "gentle-motion", 4f, 12);
        Command("flowingwater info");

        // The game's own materials (CustomShader off), and their mat commands.
        ConfigEntry<bool> custom = Setting("Client", "CustomShader");
        custom.Value = false;
        yield return new WaitForSeconds(1f);
        yield return Views(gentle, "gentle-vanilla");
        Command("flowingwater mat still _DepthFade 5");
        Command("flowingwater mat stream _SpeedY 2");
        Command("flowingwater mat custom _Reflection 0.5");
        Command("flowingwater debug");
        custom.Value = true;
        yield return new WaitForSeconds(1f);
        yield return Shot(gentle.Behind(), gentle.At(14f), "gentle-custom-again");
        Command("flowingwater debug");
        Note("all steps run");
    }

    // The look of the server's grid (run once per CellSize): a source on each slope, both seen as the
    // water spreads, in motion and close up; then a drain where the steep stream runs. With place off
    // the sources are already there (the world of an earlier run, the server now on another grid).
    private IEnumerator Grid(Site steep, Site gentle, bool place)
    {
        if (place)
        {
            string rate = Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "3";
            yield return Go(steep.At(0f));
            Command("flowingwater source " + rate);
            yield return Go(gentle.At(0f));
            Command("flowingwater source " + rate);
        }

        yield return Go(steep.At(8f));
        Command("flowingwater info");
        yield return Views(steep, "steep-start");
        yield return Views(gentle, "gentle-start");
        foreach (float wait in new[] { 30f, 60f })
        {
            yield return new WaitForSeconds(wait);
            int t = Mathf.RoundToInt(Time.timeSinceLevelLoad);
            yield return Views(steep, $"steep-t{t}");
            yield return Shot(steep.Close(), steep.At(7f), $"steep-close-t{t}");
            yield return Views(gentle, $"gentle-t{t}");
            yield return Shot(gentle.Close(), gentle.At(7f), $"gentle-close-t{t}");
        }

        Vector3? busiest = EffectStats();
        if (busiest is { } spray)
        {
            yield return Shot(spray + new Vector3(5f, 3f, 3f), spray, "spray-close");
            yield return Burst(spray + new Vector3(5f, 3f, 3f), spray, "spray-motion", 3f, 12);
        }

        // The frame rate looking at the water (the players' cost of the grid: the mesh rebuilds).
        _cameraAt = Clear(steep.Behind(), steep.At(14f));
        _cameraLook = steep.At(14f);
        yield return new WaitForSeconds(1f);
        int frames = Time.frameCount;
        float since = Time.realtimeSinceStartup;
        yield return new WaitForSeconds(5f);
        Note($"frame rate over the steep stream: {(Time.frameCount - frames) / (Time.realtimeSinceStartup - since):0.0} fps");
        yield return Burst(steep.Behind(), steep.At(14f), "steep-motion", 4f, 12);
        yield return Burst(gentle.Behind(), gentle.At(14f), "gentle-motion", 4f, 12);
        foreach (float metres in new[] { 6f, 15f, 25f })
        {
            yield return Go(steep.At(metres));
            Command("flowingwater info");
        }

        yield return Go(gentle.At(10f));
        Command("flowingwater info");
        Command("flowingwater debug");

        // Drains: bad radii refused; one of 3 m across the steep stream, one of the default radius
        // on the gentle one.
        Vector3 drain = Wet(steep, 14f, 26f);
        yield return Go(drain);
        Command("flowingwater info");
        Command("flowingwater drain 0 -1");
        Command("flowingwater drain 0 abc");
        Command("flowingwater drain 0 3");
        Vector3 gentleDrain = Wet(gentle, 14f, 26f);
        yield return Go(gentleDrain);
        Command("flowingwater info");
        Command("flowingwater drain");
        yield return new WaitForSeconds(40f);
        Command("flowingwater info");
        yield return Views(gentle, "gentle-drain");
        yield return Go(drain);
        Command("flowingwater info");
        yield return Views(steep, "steep-drain");
        Command("flowingwater remove");
        yield return Go(gentleDrain);
        Command("flowingwater remove");
    }

    // The water moved on the graphics card near the camera (Client.GpuWater) against the water as the server sends it:
    // a stream on each slope seen and filmed with it on, then off, then on again, the frame rate each way; then a flood
    // on nearly flat open ground (a strong source for a few seconds, then taken away) filmed as it runs out, from the side
    // and from above. Each site's source of an earlier run on the same world is taken away first.
    private IEnumerator Gpu(Site steep, Site gentle, Site flat)
    {
        Note($"flat site {flat}");

        // A fall: a platform of stone 3.5 m high uphill of the flat site, a source on it (the world's generated ground has
        // no cliff near the spawn).
        Vector3 towerAt = flat.At(-16f) + (flat.Across * 8f);
        yield return Go(towerAt);
        Vector3 tower = Platform(Player.m_localPlayer.transform.position, 3.5f);
        yield return Go(tower, tower.y);
        Command("flowingwater remove");
        Command("flowingwater source 4");
        string rate = Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "4";
        yield return Go(flat.At(0f));
        Command("flowingwater remove");
        yield return Go(steep.At(0f));
        Command("flowingwater remove");
        Command("flowingwater source " + rate);
        yield return Go(gentle.At(0f));
        Command("flowingwater remove");
        Command("flowingwater source " + rate);
        yield return Go(steep.At(8f));
        yield return new WaitForSeconds(40f);
        foreach (bool on in new[] { true, false, true })
        {
            SetGpuWater(on);
            yield return new WaitForSeconds(3f);
            string mode = on ? "gpu" : "server";
            Command("flowingwater gpu");
            yield return Views(steep, $"steep-{mode}");
            yield return Views(gentle, $"gentle-{mode}");
            yield return Burst(steep.Close(), steep.At(7f), $"steep-close-{mode}", 3f, 15);
            yield return Burst(gentle.Behind(), gentle.At(14f), $"gentle-motion-{mode}", 3f, 15);
            _cameraAt = Clear(steep.Behind(), steep.At(14f));
            _cameraLook = steep.At(14f);
            yield return new WaitForSeconds(1f);
            int frames = Time.frameCount;
            float since = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(5f);
            Note($"frame rate over the steep stream, {mode}: {(Time.frameCount - frames) / (Time.realtimeSinceStartup - since):0.0} fps");
        }

        // Flying 50 m down the steep stream: the card's square moves on with the camera twice or so, its water kept (no
        // pop, no refill); the square before and after.
        Command("flowingwater gpu");
        yield return Fly(t => (steep.At(6f + (50f * t)) + (steep.Across * 4f) + new Vector3(0f, 5f, 0f), steep.At(14f + (50f * t))), "steep-fly", 8f, 15);
        Command("flowingwater gpu");

        // Far water: the card's square cut to 32 m, so most of the stream is the blocks' (red in the colours), whose waves
        // must run on as the card's do and meet them at the square's edge; the square put back after.
        ConfigEntry<float> range = GpuWaterRange();
        yield return ForSteps.Set(() => range.Value, metres => SetGpuWaterRange(range, metres), 32f, FarWater(steep));
        yield return new WaitForSeconds(2f);

        // The fall off the platform (its source placed first): falls, splashes and spray as particles, filmed close and
        // from further off; who draws what.
        Vector3 fall = tower + new Vector3(0f, -2f, 0f);
        Vector3 near = tower + (flat.Across * 8f) + (flat.Down * 5f) + new Vector3(0f, -1f, 0f);
        yield return Burst(near, fall, "cliff-near", 4f, 15);
        yield return Burst(tower + (flat.Across * 14f) + (flat.Down * 12f) + new Vector3(0f, 4f, 0f), fall, "cliff-far", 3f, 15);
        Command("flowingwater gpu colours");
        yield return Shot(near, fall, "cliff-colours");
        Command("flowingwater gpu colours");

        // A flood on the flat site: 40 m3/s for six seconds, then gone, seen from the side; then what is left, from above.
        Vector3 flood = flat.At(0f);
        yield return Go(flood);
        Command("flowingwater source 40");
        Vector3 from = flat.At(8f) + (flat.Across * 16f) + new Vector3(0f, 7f, 0f);
        Vector3 at = flat.At(8f);
        yield return Burst(from, at, "flood-on", 6f, 12);

        // Who draws what while it pours: the card's water blue, the bricks' surface red, their sides and falls yellow.
        Command("flowingwater gpu colours");
        yield return Shot(from, at, "flood-colours-side");
        yield return Shot(flat.At(6f) + new Vector3(0f, 22f, 0f) - (flat.Down * 6f), flat.At(8f), "flood-colours-above");
        yield return Burst(from, at, "flood-colours", 2f, 12);
        Command("flowingwater gpu colours");
        Command("flowingwater remove");
        yield return Burst(from, at, "flood-off", 6f, 12);
        yield return Burst(flat.At(6f) + new Vector3(0f, 22f, 0f) - (flat.Down * 6f), flat.At(8f), "flood-above", 6f, 12);
        Command("flowingwater gpu");
    }

    // The fall off the gpu scenario's platform, at its edges: the water's voxels and the ground cast there (the console's
    // "flowingwater gpu columns"), and who draws what, close.
    private IEnumerator Ledge(Site flat)
    {
        yield return Go(flat.At(-16f) + (flat.Across * 8f));
        Vector3 tower = Platform(Player.m_localPlayer.transform.position, 3.5f);
        yield return Go(tower, tower.y);
        Command("flowingwater remove");
        Command("flowingwater source 4");
        yield return new WaitForSeconds(30f);
        Vector3 fall = tower + new Vector3(0f, -2f, 0f);
        Vector3 near = tower + (flat.Across * 8f) + (flat.Down * 5f) + new Vector3(0f, -1f, 0f);
        Command("flowingwater gpu colours");
        yield return Shot(near, fall, "ledge-colours");
        yield return Shot(tower + (flat.Across * -8f) + (flat.Down * -5f) + new Vector3(0f, 1f, 0f), fall, "ledge-colours-back");
        yield return Burst(near, fall, "ledge-colours", 8f, 5);
        Command("flowingwater gpu colours");
        yield return Shot(near, fall, "ledge");
        foreach (Vector3 side in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            Vector3 edge = tower + (side * 2f);
            Note(FormattableString.Invariant($"edge {edge.ToString("F2")}"));
            yield return Go(edge);
            Command("flowingwater gpu columns");
        }
    }

    // A platform of stone floors 4 m a side, this high over the ground at its middle, built from the ground up (or the
    // one an earlier run built there: a piece found well over the ground); its top's middle.
    private Vector3 Platform(Vector3 middle, float height)
    {
        if (Physics.Raycast(middle + (Vector3.up * 30f), Vector3.down, out RaycastHit hit, 40f, LayerMask.GetMask("piece"), QueryTriggerInteraction.Ignore)
            && hit.point.y > middle.y + (height / 2f))
        {
            Note(FormattableString.Invariant($"platform already at {hit.point.ToString("F1")}"));
            return hit.point;
        }

        GameObject piece = new[] { "stone_floor_2x2", "wood_floor" }.Select(n => ZNetScene.instance.GetPrefab(n)).FirstOrDefault(p => p != null)
            ?? throw new InvalidOperationException("neither stone_floor_2x2 nor wood_floor found");
        float goal = middle.y + height;
        float highest = middle.y;
        int count = 0;
        foreach (float dx in new[] { -1f, 1f })
        {
            foreach (float dz in new[] { -1f, 1f })
            {
                var at = new Vector3(middle.x + dx, middle.y, middle.z + dz);
                float top = ZoneSystem.instance.GetGroundHeight(at, out float ground) ? ground : middle.y;
                while (top < goal && count < 120)
                {
                    GameObject placed = Instantiate(piece, new Vector3(at.x, top, at.z), Quaternion.identity);
                    placed.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, Player.m_localPlayer.GetPlayerID());
                    Physics.SyncTransforms();
                    Bounds bounds = placed.GetComponentsInChildren<Collider>().Where(k => !k.isTrigger).Select(k => k.bounds)
                        .Aggregate((Bounds?)null, (all, b) => all is { } a ? Grown(a, b) : b) ?? new Bounds(placed.transform.position, Vector3.one);
                    top = Mathf.Max(top + 0.25f, bounds.max.y);
                    count++;
                }

                highest = Mathf.Max(highest, top);
            }
        }

        Note(FormattableString.Invariant($"platform of {count} {piece.name} at {middle.ToString("F1")}, top {highest:0.0}"));
        return new Vector3(middle.x, highest, middle.z);
    }

    // The far water's shots, the card's square cut short (Gpu): its waves and the blocks', then who draws what.
    private IEnumerator FarWater(Site steep)
    {
        yield return new WaitForSeconds(4f);
        yield return Burst(steep.Behind() + new Vector3(0f, 6f, 0f), steep.At(20f), "steep-far", 3f, 15);
        Command("flowingwater gpu colours");
        yield return Shot(steep.Behind() + new Vector3(0f, 6f, 0f), steep.At(20f), "steep-far-colours");
        Command("flowingwater gpu colours");
    }

    // The side of the card's square: its setting in the plugin's file.
    private static ConfigEntry<float> GpuWaterRange()
    {
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("Tie.FlowingWater", out BepInEx.PluginInfo plugin)
            || !plugin.Instance.Config.TryGetEntry("Client", "GpuWaterRange", out ConfigEntry<float> entry))
        {
            throw new InvalidOperationException("FlowingWater's Client.GpuWaterRange setting not found");
        }

        return entry;
    }

    // The side of the card's square set, as its setting in the plugin's file does.
    private void SetGpuWaterRange(ConfigEntry<float> range, float metres)
    {
        range.Value = metres;
        Note($"Client.GpuWaterRange = {metres}");
    }

    // Turns the water moved on the graphics card on or off, as its setting in the plugin's file does.
    private void SetGpuWater(bool on)
    {
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("Tie.FlowingWater", out BepInEx.PluginInfo plugin)
            || !plugin.Instance.Config.TryGetEntry("Client", "GpuWater", out BepInEx.Configuration.ConfigEntry<bool> entry))
        {
            throw new InvalidOperationException("FlowingWater's Client.GpuWater setting not found");
        }

        entry.Value = on;
        Note($"Client.GpuWater = {on}");
    }

    // Erosion as players see it (run with the server's erosion on and fast): the ground over the steep site
    // before a source runs, and as the running water wears it, with a floor laid on the stream once it runs,
    // whose ground must stay as it was from then on. The heights go to heights-<when>.csv. With place off,
    // the ground of an earlier run is only read again (after a restart: it must have been kept).
    private IEnumerator Erosion(Site steep, bool place)
    {
        yield return Go(steep.At(8f));
        Heights(steep, place ? "before" : "restart");
        yield return Views(steep, place ? "erosion-before" : "erosion-restart");
        if (!place)
        {
            yield break;
        }

        yield return Go(steep.At(0f));
        Command("flowingwater source " + (Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "4"));
        yield return Go(steep.At(8f));
        yield return new WaitForSeconds(60f);
        Vector3 floor = Wet(steep, 12f, 22f);
        GameObject piece = Instantiate(ZNetScene.instance.GetPrefab("wood_floor"), floor + (Vector3.up * 0.3f), Quaternion.identity);
        // What the server looks for: the creator the game records for a piece a player built.
        piece.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, Player.m_localPlayer.GetPlayerID());
        Note($"floor laid at {floor.ToString("F1")}");
        // The server looks for what players built every 30 s, and writes the ground at most once a minute.
        yield return new WaitForSeconds(100f);
        Heights(steep, "floor");
        foreach (float wait in new[] { 120f, 120f })
        {
            yield return new WaitForSeconds(wait);
            int t = Mathf.RoundToInt(Time.timeSinceLevelLoad);
            Heights(steep, $"t{t}");
            yield return Views(steep, $"erosion-t{t}");
        }

        Command("flowingwater remove");
        Command("flowingwater info");
    }

    // The ground players stand on over the site, a metre apart.
    private void Heights(Site site, string when)
    {
        using var file = new StreamWriter(Path.Combine(_out, $"heights-{when}.csv"));
        file.WriteLine("along,side,x,z,height");
        int missing = 0;
        foreach ((float along, float side, Vector3? ground) in Ground(site, -2f, 45f))
        {
            if (ground is not { } p)
            {
                missing++;
                continue;
            }

            file.WriteLine(FormattableString.Invariant($"{along},{side},{p.x:0.00},{p.z:0.00},{p.y:0.0000}"));
        }

        Note($"heights {when} saved ({missing} points not loaded)");
    }

    // The white water the plugin made (its sounds and spray, by their names), and its running-water
    // sound saved once as a WAV to listen to.
    private Vector3? EffectStats()
    {
        List<AudioSource> sounds = [.. FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(s => s.name.StartsWith("FlowingWater white water", StringComparison.Ordinal))];
        List<ParticleSystem> sprays = [.. FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.name.StartsWith("FlowingWater spray", StringComparison.Ordinal))];
        ParticleSystem busiest = sprays.OrderByDescending(p => p.particleCount).FirstOrDefault();
        Note($"white water: {sounds.Count} sounds ({sounds.Count(s => s.isPlaying)} playing, loudest {sounds.Select(s => s.volume).DefaultIfEmpty().Max():0.00}), {sprays.Count} sprays with {sprays.Sum(p => p.particleCount)} drops in the air");
        foreach (AudioClip clip in sounds.Select(s => s.clip).Where(c => c != null).Distinct())
        {
            string wav = Path.Combine(_out, clip.name.Replace(' ', '-') + ".wav");
            if (File.Exists(wav))
            {
                continue;
            }

            float[] samples = new float[clip.samples * clip.channels];
            // The array overload by reflection: the compiler cannot tell it from the span one on .NET Framework.
            typeof(AudioClip).GetMethod("GetData", [typeof(float[]), typeof(int)]).Invoke(clip, [samples, 0]);
            WriteWav(wav, samples, clip.channels, clip.frequency);
            Note($"sound {clip.name} saved: {clip.length:0.0} s at {clip.frequency} Hz");
        }

        return busiest != null && busiest.particleCount > 0 ? busiest.transform.position : null;
    }

    private static void WriteWav(string path, float[] samples, int channels, int rate) => File.WriteAllBytes(path, Wav.Encode(samples, channels, rate));

    // A strong spring at the top of the steepest open slope near the spawn, and once it has run, its
    // busiest spray filmed with sound: close, then from further off.
    private IEnumerator Cascade(Vector3 spawn)
    {
        Site steep = FindSite(spawn, 0.8f, 1.6f, true) ?? FindSite(spawn, 0.7f, 2f, false);
        Note($"cascade site {steep}");
        yield return Go(steep.At(0f));
        Command("flowingwater source " + (Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "4"));
        yield return Go(steep.At(10f));
        yield return new WaitForSeconds(90f);
        yield return Views(steep, "cascade-site");
        Vector3? busiest = EffectStats();
        Vector3 at = busiest ?? steep.At(10f);
        Vector3 side = Vector3.Cross(Vector3.up, (at - steep.At(0f)).normalized);
        yield return Film(at + (side * 6f) + new Vector3(0f, 2.5f, 0f), at, "cascade-close", 10f, 24);
        yield return Film(at + (side * 14f) + (steep.Down * 8f) + new Vector3(0f, 6f, 0f), at, "cascade-wide", 8f, 24);
    }

    // Like Burst, with the sound the camera hears (AudioTap on the audio listener) and each frame's
    // real time, so that a video can be made of them in step: name-NNN.png, name.wav, name.txt (an ffmpeg
    // concat list with each frame's duration and the sound's offset).
    private IEnumerator Film(Vector3 from, Vector3 at, string name, float seconds, int fps)
    {
        AudioListener listener = FindObjectsByType<AudioListener>(FindObjectsSortMode.None).FirstOrDefault(l => l.isActiveAndEnabled);
        if (listener == null)
        {
            Note($"film {name}: no audio listener, frames only");
        }
        else if (listener.GetComponent<AudioTap>() == null)
        {
            listener.gameObject.AddComponent<AudioTap>();
        }

        from = Clear(from, at);
        _cameraAt = from;
        _cameraLook = at;
        yield return new WaitForSeconds(1f);
        int frames = Mathf.RoundToInt(seconds * fps);
        float step = 1f / fps;
        var times = new List<float>(frames);
        // Without a listener nothing is heard: no sound file.
        bool heard = listener != null;
        AudioTap.On = heard;
        float soundFrom = Time.realtimeSinceStartup;
        for (int i = 0; i < frames; i++)
        {
            float next = Time.time + step;
            yield return new WaitForEndOfFrame();
            times.Add(Time.realtimeSinceStartup);
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"{name}-{i:000}.png"));
            while (Time.time < next)
            {
                yield return null;
            }
        }

        AudioTap.On = false;
        float[] sound = heard ? AudioTap.Samples() : [];
        if (heard)
        {
            WriteWav(Path.Combine(_out, name + ".wav"), sound, AudioTap.Channels, AudioSettings.outputSampleRate);
        }

        var list = new System.Text.StringBuilder();
        list.Append(FormattableString.Invariant($"# sound starts {times[0] - soundFrom:0.000} s before the first frame\n"));
        for (int i = 0; i < times.Count; i++)
        {
            float duration = i + 1 < times.Count ? times[i + 1] - times[i] : step;
            list.Append(FormattableString.Invariant($"file '{name}-{i:000}.png'\nduration {duration:0.0000}\n"));
        }

        File.WriteAllText(Path.Combine(_out, name + ".txt"), list.ToString());
        Note(FormattableString.Invariant($"film {name}: {frames} frames over {times[times.Count - 1] - times[0]:0.00} s, sound {sound.Length / (float)Math.Max(1, AudioTap.Channels) / AudioSettings.outputSampleRate:0.00} s at {AudioSettings.outputSampleRate} Hz x{AudioTap.Channels}, starting {times[0] - soundFrom:0.000} s before the first frame"));
    }

    // The custom material's look, one set of properties after the other, on both streams from the
    // same cameras: near, from the side and from far off (where ripples must still show).
    private IEnumerator Look(Site steep, Site gentle)
    {
        string rate = Environment.GetEnvironmentVariable("FW_REC_RATE") ?? "2";
        yield return Go(steep.At(0f));
        Command("flowingwater source " + rate);
        yield return Go(gentle.At(0f));
        Command("flowingwater source " + rate);
        yield return Go(steep.At(8f));
        yield return new WaitForSeconds(120f);
        foreach (string set in LookSets())
        {
            string look = UseLook(set);
            yield return new WaitForSeconds(1f);
            foreach (Site site in new[] { steep, gentle })
            {
                string where = site == steep ? "steep" : "gentle";

                // The server sends a zone's water to the players near it.
                yield return Go(site.At(8f));
                yield return Shot(site.At(10f) + (site.Across * 6f) + new Vector3(0f, 7f, 0f), site.At(12f), $"{look}-{where}-near");
                yield return Shot(site.Side(), site.At(12f), $"{look}-{where}-side");
                yield return Shot(site.At(15f) + (site.Across * 50f) + new Vector3(0f, 35f, 0f), site.At(15f), $"{look}-{where}-far");
            }

            // A view of the caller's (FW_REC_VIEW: "fromX,fromY,fromZ,atX,atY,atZ"), to look at one place.
            if (View() is { } view)
            {
                yield return Go(view.At);
                yield return Shot(view.From, view.At, $"{look}-view");
            }
        }
    }

    // The looks to compare (FW_REC_LOOKS: "name=property value;property value|name=..."); unset or empty: the
    // material as it is.
    private static string[] LookSets()
    {
        string configured = Environment.GetEnvironmentVariable("FW_REC_LOOKS");
        return (string.IsNullOrWhiteSpace(configured) ? "default=" : configured).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
    }

    // Sets one look's properties on the custom material (a name alone changes nothing), and gives its name.
    private string UseLook(string set)
    {
        string[] named = set.Split(new[] { '=' }, 2);
        string properties = named.Length > 1 ? named[1] : string.Empty;
        foreach (string property in properties.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            Command("flowingwater mat custom " + property.Trim());
        }

        return named[0];
    }

    // The pools of a test world (FW_REC_POOLS: "x,z,radius,depth;..."): each filled by a spring sized to fill it in
    // about a minute, the springs taken away, then each pool seen from its side and from above, in every look.
    private IEnumerator Pools()
    {
        var pools = (Environment.GetEnvironmentVariable("FW_REC_POOLS") ?? "-66,190,3,1;-84,190,5,2;-108,190,8,4;-140,190,12,7.5")
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray())
            .ToList();
        foreach (float[] p in pools)
        {
            float volume = 0.5f * Mathf.PI * p[2] * p[2] * p[3];
            yield return Go(new Vector3(p[0], 0f, p[1]));
            Command(FormattableString.Invariant($"flowingwater source {Mathf.Max(0.2f, volume / 60f):0.##}"));
        }

        yield return new WaitForSeconds(75f);
        foreach (float[] p in pools)
        {
            yield return Go(new Vector3(p[0], 0f, p[1]));
            Command("flowingwater remove");
        }

        yield return new WaitForSeconds(20f);
        foreach (string set in LookSets())
        {
            string look = UseLook(set);
            yield return new WaitForSeconds(1f);
            for (int k = 0; k < pools.Count; k++)
            {
                float[] p = pools[k];
                var centre = new Vector3(p[0], 0f, p[1]);
                yield return Go(centre);
                ZoneSystem.instance.GetGroundHeight(centre, out float bottom);
                centre.y = bottom + p[3];
                string name = FormattableString.Invariant($"{look}-pool{k + 1}-{p[2]:0}m");
                yield return Shot(centre + new Vector3(0f, (p[2] * 0.6f) + 2f, -((p[2] * 1.6f) + 4f)), centre, name + "-side");
                yield return Shot(centre + new Vector3(p[2] * 0.3f, (p[2] * 1.8f) + 6f, -p[2] * 0.5f), centre, name + "-above");
            }
        }
    }

    private static (Vector3 From, Vector3 At)? View()
    {
        string text = Environment.GetEnvironmentVariable("FW_REC_VIEW");
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string[] parts = text.Split(',');
        if (parts.Length != 6)
        {
            Current?.Note($"FW_REC_VIEW \"{text}\" is not six numbers (from x,y,z then at x,y,z): left out");
            return null;
        }

        float[] v = [.. parts.Select(t => float.Parse(t, CultureInfo.InvariantCulture))];
        return (new Vector3(v[0], v[1], v[2]), new Vector3(v[3], v[4], v[5]));
    }

    // The point with the deepest water between these distances downhill, searching 12 m to each
    // side; the middle of the range when there is no water there.
    private Vector3 Wet(Site site, float from, float to)
    {
        Vector3 best = site.At((from + to) * 0.5f);
        float bestDepth = 0f;
        foreach ((_, _, Vector3? ground) in Ground(site, from, to))
        {
            if (ground is not { } p)
            {
                continue;
            }

            float depth = Floating.GetLiquidLevel(p + (Vector3.up * 0.05f), 1f, LiquidType.Water) - p.y;
            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = p;
            }
        }

        Note($"wettest point {from}..{to} m downhill: {best.ToString("F1")}, {bestDepth:0.00} m deep");
        return best;
    }

    // The site's points a metre apart, between these distances downhill and 12 m to each side, each on the
    // ground players stand on (none where its zone is not loaded).
    private static IEnumerable<(float Along, float Side, Vector3? Ground)> Ground(Site site, float from, float to)
    {
        for (float along = from; along <= to; along += 1f)
        {
            for (float side = -12f; side <= 12f; side += 1f)
            {
                Vector3 p = site.At(along) + (site.Across * side);
                bool loaded = ZoneSystem.instance.GetGroundHeight(p, out float ground);
                p.y = ground;
                yield return (along, side, loaded ? p : null);
            }
        }
    }

    private static ConfigEntry<bool> Setting(string section, string key)
    {
        ConfigFile config = Chainloader.PluginInfos["Tie.FlowingWater"].Instance.Config;
        return config.TryGetEntry(section, key, out ConfigEntry<bool> entry)
            ? entry
            : throw new InvalidOperationException($"setting {section}.{key} not found");
    }

    private void Command(string text)
    {
        Note("> " + text);
        Console.instance.TryRunCommand(text, false, true);
    }

    // The player still there: gone (dead, or out of the world) the run cannot go on, and says so.
    private static Player Alive(Player player, Vector3 target) => player != null && player == Player.m_localPlayer
        ? player
        : throw new InvalidOperationException($"the player is gone (dead?) on the way to {target}");

    private IEnumerator Go(Vector3 point, float? standAt = null, bool allowSlide = false)
    {
        Player player = Player.m_localPlayer;
        float ground = WorldGenerator.instance.GetHeight(point.x, point.z);
        if (standAt is { } given)
        {
            ground = given - 0.5f;
        }
        else if (ZoneSystem.instance.GetGroundHeight(point, out float loaded))
        {
            ground = loaded;
        }

        var target = new Vector3(point.x, ground + 0.5f, point.z);
        float until = Time.time + 30f;
        while (!Alive(player, target).TeleportTo(target, player.transform.rotation, true))
        {
            if (Time.time > until)
            {
                throw new InvalidOperationException($"teleport to {target} refused for 30 s");
            }

            yield return new WaitForSeconds(0.5f);
        }

        until = Time.time + 60f;
        while (Alive(player, target).IsTeleporting() && Time.time < until)
        {
            yield return null;
        }

        yield return new WaitForSeconds(4f);
        Alive(player, target);
        float off = Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), new Vector2(target.x, target.z));
        if (off > (allowSlide ? 25f : 5f) || (standAt is { } wanted && Mathf.Abs(player.transform.position.y - wanted) > 0.5f))
        {
            throw new InvalidOperationException($"teleport to {target} ended at {player.transform.position}");
        }

        // A vantage (a shot, a wait) may be reached a little off (a steep face slides, a current sweeps): noted, and
        // the run goes on, the shots naming their own cameras. A stand a command is run from must be where it was asked.
        Note(off > 5f ? $"at {player.transform.position.ToString("F1")} ({off:0} m from {target.x:0}, {target.z:0}: slid)" : $"at {player.transform.position.ToString("F1")}");
    }

    // "!glide x,z,speed": the player carried over the ground to x, z at so many metres a second, while the steps after
    // it go on (a film of the water as one walks: a step's move takes some 14 s and the water has long caught up).
    private IEnumerator Glide(Vector2 to, float speed)
    {
        Player player = Player.m_localPlayer;
        Note(FormattableString.Invariant($"gliding to {to.x:0.#}, {to.y:0.#} at {speed:0.#} m/s"));
        while (player != null && player == Player.m_localPlayer)
        {
            (float x, float z, bool there) = StepWords.GlideStep(player.transform.position.x, player.transform.position.z, to.x, to.y, speed * Time.deltaTime);
            var place = new Vector3(x, player.transform.position.y, z);
            if (ZoneSystem.instance.GetGroundHeight(place, out float ground))
            {
                place.y = ground + StepWords.OverGround;
            }

            player.transform.position = place;
            if (player.GetComponent<Rigidbody>() is { } body)
            {
                body.position = place;
                body.linearVelocity = Vector3.zero;
            }

            if (there)
            {
                Note(FormattableString.Invariant($"glided to {player.transform.position}"));
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator Views(Site site, string name)
    {
        yield return Shot(site.Behind(), site.At(14f), name + "-behind");
        yield return Shot(site.Side(), site.At(12f), name + "-side");
        yield return Shot(site.Close(), site.At(7f), name + "-close");
    }

    // The first of a few places around the asked one from which nothing solid stands between the
    // camera and what it looks at (trees, rocks, buildings), or the asked one. Never one under the ground: the
    // terrain's heightfield stops no line cast from below it, so a place under a hill would pass for a clear one
    // (and the picture would show the hill's underside).
    private Vector3 Clear(Vector3 from, Vector3 at)
    {
        Vector3 away = from - at;
        foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
        {
            foreach (float rise in new[] { 0f, 6f, 12f })
            {
                Vector3 candidate = at + (Quaternion.Euler(0f, turn, 0f) * away) + new Vector3(0f, rise, 0f);
                if (!Underground(candidate) && !Physics.Linecast(candidate, at + (Vector3.up * 1.5f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Physics.OverlapSphere(candidate, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Length == 0)
                {
                    return candidate;
                }
            }
        }

        Note($"no clear view of {at.ToString("F0")} found near {from.ToString("F0")}");
        return from;
    }

    // Whether a place is under the ground there (or within half a metre over it, where the near plane cuts it).
    private static bool Underground(Vector3 place) => ZoneSystem.instance.GetGroundHeight(place, out float ground) && place.y < ground + 0.5f;

    private IEnumerator Shot(Vector3 from, Vector3 at, string name)
    {
        from = Clear(from, at);
        _cameraAt = from;
        _cameraLook = at;
        yield return new WaitForSeconds(0.6f);
        yield return new WaitForEndOfFrame();
        string file = Path.Combine(_out, name + ".png");
        ScreenCapture.CaptureScreenshot(file);
        Note($"shot {name}: from {from.ToString("F0")} at {at.ToString("F0")}");
        yield return null;
        yield return null;
    }

    private IEnumerator Burst(Vector3 from, Vector3 at, string name, float seconds, int fps)
    {
        from = Clear(from, at);
        yield return Capture(_ => (from, at), name, seconds, fps);
        Note($"burst {name}: {Mathf.RoundToInt(seconds * fps)} frames at {fps} fps");
    }

    // A burst with the camera moving: path gives where it is and where it looks at each share of the time (0 to 1).
    private IEnumerator Fly(Func<float, (Vector3 From, Vector3 At)> path, string name, float seconds, int fps)
    {
        yield return Capture(path, name, seconds, fps);
        Note($"fly {name}: {Mathf.RoundToInt(seconds * fps)} frames at {fps} fps");
    }

    // Frames captured at this rate for this long, the camera where path says at each share of the time (0 to 1), after
    // a moment to settle where it starts.
    private IEnumerator Capture(Func<float, (Vector3 From, Vector3 At)> path, string name, float seconds, int fps)
    {
        (_cameraAt, _cameraLook) = path(0f);
        yield return new WaitForSeconds(0.6f);
        int frames = Mathf.RoundToInt(seconds * fps);
        float step = 1f / fps;
        for (int i = 0; i < frames; i++)
        {
            float next = Time.time + step;
            (Vector3 from, Vector3 at) = path(i / (float)(frames - 1));
            _cameraAt = from;
            _cameraLook = at;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"{name}-{i:000}.png"));
            while (Time.time < next)
            {
                yield return null;
            }
        }
    }

    // Console commands at given places (FW_REC_AT: "x,z[,y]|command;x,z[,y]|command"): the player is taken there
    // (standing at y when given, on a floor or in a box) and runs the command; its answer goes to the log. A place of
    // "~" runs the command where the player is, at once (a film right after a pour: a move takes some 14 s). A command
    // starting with '!' is the recorder's own: "!source x,y,z,rate[,heading,width,spread,throw]" (a source at that
    // point, all round or facing a way), "!remove x,y,z" (the source within 2 m taken away), "!panel x,y,z" (the
    // panel opened for the source or drain within 2 m, as [E] on it would), "!slide row,notch" (a slider of that panel
    // moved), "!ghost x,y,z,heading" (a source's placing ghost there, turned so), "!break x,y,z" (the piece nearest the
    // point broken, as a player would), "!glide x,z,speed" (the player carried over the ground there at so many m/s
    // while the steps after it go on), "!piece name x,y,z,yaw" (a piece of that prefab placed there, turned so many
    // degrees round the vertical), "!drop name x,y,z" (any prefab let go there), "!wait s", "!shot
    // from,at,name" and "!film from,at,name,seconds,fps" (points as x,y,z).
    private IEnumerator At()
    {
        string[] steps = (Environment.GetEnvironmentVariable("FW_REC_AT") ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (steps.Length == 0)
        {
            throw new InvalidOperationException("the at scenario needs FW_REC_AT (\"x,z[,y]|command;...\"), and it has no steps");
        }

        foreach (string step in steps)
        {
            string[] parts = step.Split(new[] { '|' }, 2);
            bool here = parts[0].Trim() == "~";
            float[] where = null;
            if (!here && (!StepWords.Numbers(parts[0], out where) || where.Length < 2))
            {
                Note("recorder step with a bad place: " + step);
                continue;
            }

            string command = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            bool vantage = command.StartsWith("!wait ", StringComparison.Ordinal) || command.StartsWith("!shot ", StringComparison.Ordinal) || command.StartsWith("!film ", StringComparison.Ordinal);
            if (!here)
            {
                yield return Go(new Vector3(where[0], 0f, where[1]), where.Length > 2 ? where[2] : null, vantage);
            }

            if (command.StartsWith("!wait ", StringComparison.Ordinal))
            {
                if (StepWords.Numbers(command.Substring(6), out float[] wait) && wait.Length == 1)
                {
                    yield return new WaitForSeconds(wait[0]);
                }
                else
                {
                    Note("recorder command with missing or bad numbers: " + command);
                }
            }
            else if (command.StartsWith("!shot ", StringComparison.Ordinal) || command.StartsWith("!film ", StringComparison.Ordinal))
            {
                // "!shot fx,fy,fz,ax,ay,az,name" or "!film fx,fy,fz,ax,ay,az,name,seconds,fps": from a point, at a point.
                bool film = command[1] == 'f';
                string[] v = command.Substring(6).Split(',');
                if (v.Length != (film ? 9 : 7) || !StepWords.Numbers(string.Join(",", v, 0, 6), out float[] p))
                {
                    Note("recorder command with missing or bad numbers: " + command);
                }
                else if (!PlainName(v[6]))
                {
                    // Its files are written in the output folder, never elsewhere.
                    Note("recorder shot or film name that is not a plain name: " + command);
                }
                else if (film && !FilmName(v[6]))
                {
                    Note("film name ffmpeg's frame list refuses (letters, digits, '.', '_' and '-' only, no '.' first): " + command);
                }
                else if (!film)
                {
                    yield return Shot(new Vector3(p[0], p[1], p[2]), new Vector3(p[3], p[4], p[5]), v[6]);
                }
                else if (StepWords.Numbers(v[7], out float[] seconds) && int.TryParse(v[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps)
                    && fps is >= 1 and <= MaxFps && seconds[0] <= MaxSeconds && seconds[0] * fps is >= 1f and <= MaxFrames)
                {
                    yield return Film(new Vector3(p[0], p[1], p[2]), new Vector3(p[3], p[4], p[5]), v[6], seconds[0], fps);
                }
                else
                {
                    Note(FormattableString.Invariant($"film with bad numbers, no frame or too many (fps up to {MaxFps}, frames up to {MaxFrames}, seconds up to {MaxSeconds}): {command}"));
                }
            }
            else if (command.StartsWith("!", StringComparison.Ordinal))
            {
                Own(command);
            }
            else if (command.Length > 0)
            {
                Command(command);
            }

            yield return new WaitForSeconds(here ? 0f : 2f);
        }

        // What the last step changed (a piece, a source) reaches the server before the game quits.
        yield return new WaitForSeconds(2f);
    }

    // A name that makes a file in the output folder: not empty, no folder, no "..", nothing a film's ffmpeg list would
    // need escaped (quotes, line breaks), not "log" (the recorder's own log.txt is open) and no Windows device.
    private static bool PlainName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.IndexOfAny(['/', '\\', '\'', '"', '\n', '\r']) < 0
        && name != "." && name != ".." && !string.Equals(name, "log", StringComparison.OrdinalIgnoreCase) && !Device(name);

    // A name ffmpeg's concat list takes in its safe mode: ASCII letters, digits, '.', '_' and '-', not starting with '.'.
    private static bool FilmName(string name) =>
        name[0] != '.' && name.All(c => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '.' or '_' or '-');

    // Whether Windows takes a file of this name (with any extension after it) for a device, not a file.
    private static bool Device(string name) => Devices.Contains(name.Split('.')[0].TrimEnd(' '));

    // The recorder's own commands of At.
    private void Own(string command)
    {
        string[] words = command.Split(new[] { ' ' }, 2);
        if (words[0] == "!piece")
        {
            Piece(words.Length > 1 ? words[1] : string.Empty);
            return;
        }

        if (words[0] == "!drop")
        {
            Drop(words.Length > 1 ? words[1] : string.Empty);
            return;
        }

        if (words[0] == "!slide")
        {
            Slide(words.Length > 1 ? words[1] : string.Empty);
            return;
        }

        if (words[0] == "!glide")
        {
            if (words.Length < 2 || !StepWords.Glide(words[1], out (float X, float Z, float Speed) glide))
            {
                Note("recorder !glide needs x,z,speed: " + command);
                return;
            }

            StartCoroutine(Glide(new Vector2(glide.X, glide.Z), glide.Speed));
            return;
        }

        if (words[0] is not ("!source" or "!remove" or "!break" or "!panel" or "!ghost"))
        {
            Note("unknown recorder command " + command);
            return;
        }

        if (words.Length < 2 || !StepWords.Numbers(words[1], out float[] v) || v.Length < (words[0] == "!source" ? 4 : 3))
        {
            Note("recorder command with missing or bad numbers: " + command);
            return;
        }

        // The server pours only from a source of a rate over 0: one that would pour nothing is not made.
        if (words[0] == "!source" && v[3] <= 0f)
        {
            Note("source rate must be over 0 m³/s: " + command);
            return;
        }

        var point = new Vector3(v[0], v[1], v[2]);

        // A height of StepWords.OnTheGround or lower: on the ground there.
        point.y = StepWords.Height(point.y, point.y <= StepWords.OnTheGround && ZoneSystem.instance.GetGroundHeight(point, out float ground) ? ground : null);

        switch (words[0])
        {
            case "!source":
                // "!source x,y,z,rate" pours all round; ",heading,width,spread,throw" after it faces that way (degrees
                // from north), its outlet that wide (m), fanned and thrown up so many degrees.
                bool faces = v.Length >= 8;
                GameObject source = Instantiate(ZNetScene.instance.GetPrefab("FlowingWater_Source"), point, faces ? Quaternion.Euler(0f, v[4], 0f) : Quaternion.identity);
                ZDO zdo = source.GetComponent<ZNetView>().GetZDO();
                zdo.Set("FW_Rate".GetStableHashCode(), v[3]);
                if (faces)
                {
                    zdo.Set("FW_Shape".GetStableHashCode(), 1);
                    zdo.Set("FW_Width".GetStableHashCode(), v[5]);
                    zdo.Set("FW_Spread".GetStableHashCode(), v[6]);
                    zdo.Set("FW_Throw".GetStableHashCode(), v[7]);
                }

                Note(FormattableString.Invariant($"source of {v[3]} m³/s at {point}") + (faces ? FormattableString.Invariant($" facing {v[4]}°, {v[5]} m wide, fanned {v[6]}°, thrown up {v[7]}°") : string.Empty));
                break;
            case "!remove":
                ZNetView marker = FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                    .Where(n => n.IsValid() && n.GetZDO().GetPrefab() == "FlowingWater_Source".GetStableHashCode())
                    .Where(n => Vector3.Distance(n.transform.position, point) <= RemoveReach)
                    .OrderBy(n => Vector3.Distance(n.transform.position, point)).FirstOrDefault();
                Note(marker == null ? "no source to remove" : FormattableString.Invariant($"source at {marker.transform.position} removed"));
                if (marker != null)
                {
                    marker.ClaimOwnership();
                    ZNetScene.instance.Destroy(marker.gameObject);
                }

                break;
            case "!ghost":
                // "!ghost x,y,z,heading": the source as the hoe's ghost shows it while placing, turned that way (made
                // as the game makes its ghost, with networking off for it); left there.
                ZNetView.m_forceDisableInit = true;
                try
                {
                    Instantiate(ZNetScene.instance.GetPrefab("FlowingWater_Source"), point, Quaternion.Euler(0f, v.Length > 3 ? v[3] : 0f, 0f));
                }
                finally
                {
                    ZNetView.m_forceDisableInit = false;
                }

                Note(FormattableString.Invariant($"a source's placing ghost at {point}, turned {(v.Length > 3 ? v[3] : 0f)}°"));
                break;
            case "!panel":
                // The source and drain panel opened for the source or drain within 2 m, as [E] on it would (the
                // plugin's own MarkerPanel.Open, found by name: the recorder builds without the plugin).
                Component nearest = FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                    .Where(n => n.IsValid() && Vector3.Distance(n.transform.position, point) <= RemoveReach)
                    .Select(n => n.GetComponent("Marker"))
                    .Where(m => m != null)
                    .OrderBy(m => Vector3.Distance(m.transform.position, point)).FirstOrDefault();
                System.Reflection.MethodInfo open = Type.GetType("FlowingWater.Client.MarkerPanel, FlowingWater")?.GetMethod("Open");
                if (nearest == null || open == null)
                {
                    Note(nearest == null ? "no source or drain to open the panel for" : "the plugin's MarkerPanel.Open was not found");
                    break;
                }

                open.Invoke(null, [nearest]);
                Note(FormattableString.Invariant($"panel opened for the one at {nearest.transform.position}"));
                break;
            case "!break":
                WearNTear piece = FindObjectsByType<WearNTear>(FindObjectsSortMode.None)
                    .OrderBy(w => Vector3.Distance(w.transform.position, point)).FirstOrDefault();
                Note(piece == null ? "no piece to break" : FormattableString.Invariant($"{piece.name} at {piece.transform.position} broken"));
                piece?.Remove();
                break;
            default:
                break;
        }
    }

    // "!piece name x,y,z,yaw": a piece of that prefab placed at the point, turned round the vertical, as the game's own
    // spawning places one (an object the server keeps).
    private void Piece(string text)
    {
        string[] words = text.Split(new[] { ' ' }, 2);
        GameObject prefab = words[0].Length > 0 ? ZNetScene.instance.GetPrefab(words[0]) : null;
        if (prefab == null || prefab.GetComponent<Piece>() == null)
        {
            Note("recorder !piece with no piece of that name: " + text);
            return;
        }

        if (words.Length < 2 || !StepWords.Numbers(words[1], out float[] v) || v.Length != 4)
        {
            Note("recorder !piece needs a name and x,y,z,yaw: " + text);
            return;
        }

        var point = new Vector3(v[0], v[1], v[2]);
        // The local player's, as a piece the player builds (a private chest opens for them).
        GameObject placed = Instantiate(prefab, point, Quaternion.Euler(0f, v[3], 0f));
        placed.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_creator, Player.m_localPlayer.GetPlayerID());
        Note(FormattableString.Invariant($"{words[0]} placed at {point}, turned {v[3]}°"));
    }

    // "!slide row,notch": a slider of the open source and drain panel (its row: Width, Spread, Throw, Power or Reach)
    // moved to that notch, as a player dragging it would (the plugin's panel found by name: the recorder builds
    // without the plugin).
    private void Slide(string what)
    {
        string[] parts = what.Split(',');
        object panel = Type.GetType("FlowingWater.Client.MarkerPanel, FlowingWater") is { } type ? AccessTools.Field(type, "_panel")?.GetValue(null) : null;
        object view = panel == null ? null : AccessTools.Field(panel.GetType(), "_view")?.GetValue(panel);
        object row = parts.Length == 2 && view != null ? AccessTools.Property(view.GetType(), parts[0])?.GetValue(view) : null;
        if (row == null || AccessTools.Property(row.GetType(), "Slider")?.GetValue(row) is not UnityEngine.UI.Slider slider || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int notch))
        {
            Note("no open panel's slider to move: " + what);
            return;
        }

        slider.value = notch;
        Note(FormattableString.Invariant($"slider {parts[0]} moved to notch {notch}"));
    }

    // "!drop name x,y,z": that prefab (an item, a log, a boat) let go at the point as it is, for the current to carry.
    private void Drop(string text)
    {
        string[] words = text.Split(new[] { ' ' }, 2);
        GameObject prefab = words[0].Length > 0 ? ZNetScene.instance.GetPrefab(words[0]) : null;
        if (prefab == null)
        {
            Note("recorder !drop with no prefab of that name: " + text);
            return;
        }

        if (words.Length < 2 || !StepWords.Numbers(words[1], out float[] v) || v.Length != 3)
        {
            Note("recorder !drop needs a name and x,y,z: " + text);
            return;
        }

        var point = new Vector3(v[0], v[1], v[2]);
        Instantiate(prefab, point, Quaternion.identity);
        Note(FormattableString.Invariant($"{words[0]} dropped at {point}"));
    }

    // Readies a test world for the editor: at each place of FW_REC_DIG ("x,z;x,z"), the game generates the zones
    // around (the player stands there) and the ground is dug once (the editor makes new ground only where the
    // game already has some). The server saves the world when it stops.
    private IEnumerator Prep()
    {
        foreach (string place in (Environment.GetEnvironmentVariable("FW_REC_DIG") ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] xz = place.Split(',');
            var at = new Vector3(float.Parse(xz[0], CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], CultureInfo.InvariantCulture));
            yield return Go(at);
            ZoneSystem.instance.GetGroundHeight(at, out at.y);
            yield return Dig(at, 0.5f, 1);
            yield return new WaitForSeconds(5f);
        }
    }

    // A pit this wide, dug with the game's own terrain operation (the pickaxe's) over and over.
    private IEnumerator Dig(Vector3 centre, float radius, int passes)
    {
        // The tools carry them: what the pickaxe leaves where it hits the ground, the hoe's pieces.
        List<GameObject> ops = [.. ObjectDB.instance.m_items
            .Select(i => i.GetComponent<ItemDrop>()?.m_itemData.m_shared)
            .Where(s => s != null)
            .SelectMany(s => new[] { s.m_spawnOnHitTerrain }.Concat(s.m_buildPieces != null ? s.m_buildPieces.m_pieces : []))
            .Where(p => p != null && p.GetComponent<TerrainOp>() != null)
            .Distinct()];
        Note($"terrain operations: {string.Join(", ", ops.Select(p => p.name))}");
        GameObject dig = ops.FirstOrDefault(p => p.name.IndexOf("dig", StringComparison.OrdinalIgnoreCase) >= 0)
            ?? throw new InvalidOperationException("no dig terrain operation among the game's prefabs");
        for (int pass = 0; pass < passes; pass++)
        {
            // A grid 1.5 m apart round the centre, which is always dug.
            int steps = Mathf.FloorToInt(radius / 1.5f);
            for (int iz = -steps; iz <= steps; iz++)
            {
                float dz = iz * 1.5f;
                for (int ix = -steps; ix <= steps; ix++)
                {
                    float dx = ix * 1.5f;
                    if ((dx * dx) + (dz * dz) > radius * radius)
                    {
                        continue;
                    }

                    Vector3 at = centre + new Vector3(dx, 0f, dz);
                    ZoneSystem.instance.GetGroundHeight(at, out float ground);
                    at.y = ground;
                    Instantiate(dig, at, Quaternion.identity);
                }
            }

            yield return new WaitForSeconds(1f);
        }

        ZoneSystem.instance.GetGroundHeight(centre, out float bottom);
        Note($"pit dug with {dig.name} at {centre.ToString("F1")}: ground {centre.y:0.0} -> {bottom:0.0} m");
    }

    // The generated ground's slope at a place (rise per metre, east and north), over 2 m each way.
    private static Vector2 Gradient(WorldGenerator generator, float x, float z) =>
        new Vector2(generator.GetHeight(x + 2f, z) - generator.GetHeight(x - 2f, z), generator.GetHeight(x, z + 2f) - generator.GetHeight(x, z - 2f)) / 4f;

    // The nearest place within 400 m of the spawn whose slope lies in the range, on dry land
    // well above the sea, that keeps falling for 30 m the same way.
    private Site FindSite(Vector3 spawn, float minSlope, float maxSlope, bool open)
    {
        WorldGenerator generator = WorldGenerator.instance;
        float sea = ZoneSystem.instance.m_waterLevel;
        Site best = null;
        float bestDistance = float.MaxValue;
        var tried = new List<string>();
        for (float dz = -400f; dz <= 400f; dz += 6f)
        {
            for (float dx = -400f; dx <= 400f; dx += 6f)
            {
                float x = spawn.x + dx;
                float z = spawn.z + dz;
                float h = generator.GetHeight(x, z);
                if (h < sea + 15f || (open && (generator.GetBiome(x, z) != Heightmap.Biome.Meadows || WorldGenerator.InForest(new Vector3(x, h, z)) || WorldGenerator.InForest(new Vector3(x - 10f, h, z)) || WorldGenerator.InForest(new Vector3(x + 10f, h, z)) || WorldGenerator.InForest(new Vector3(x, h, z - 10f)) || WorldGenerator.InForest(new Vector3(x, h, z + 10f)))))
                {
                    continue;
                }

                Vector2 gradient = Gradient(generator, x, z);
                float slope = gradient.magnitude;
                if (slope < minSlope || slope > maxSlope)
                {
                    continue;
                }

                Vector2 down = -gradient.normalized;
                // Downhill it keeps falling, and stays well above the sea for 60 m (not a slope that
                // runs straight into it).
                float far = generator.GetHeight(x + (down.x * 30f), z + (down.y * 30f));
                if (far > h - (minSlope * 20f) || far < sea + 8f || generator.GetHeight(x + (down.x * 60f), z + (down.y * 60f)) < sea + 5f)
                {
                    continue;
                }

                float distance = new Vector2(dx, dz).magnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = new Site(new Vector3(x, h, z), new Vector3(down.x, 0f, down.y), slope);
                }
            }
        }

        if (best == null && !open)
        {
            throw new InvalidOperationException($"no site with slope {minSlope}..{maxSlope} near {spawn}");
        }

        Note(best == null ? $"no open meadow with slope {minSlope}..{maxSlope}: trying anywhere" : $"site found, open {open}");
        return best;
    }

    private sealed class Site
    {
        public Site(Vector3 origin, Vector3 down, float slope)
        {
            Origin = origin;
            Down = down;
            Across = new Vector3(-down.z, 0f, down.x);
            Slope = slope;
        }

        public Vector3 Origin { get; }

        public Vector3 Down { get; }

        public Vector3 Across { get; }

        public float Slope { get; }

        // A point this far downhill, on the generated ground.
        public Vector3 At(float metres)
        {
            Vector3 p = Origin + (Down * metres);
            p.y = WorldGenerator.instance.GetHeight(p.x, p.z);
            return p;
        }

        // High and steep, so rocks and trees at the source do not hide the water.
        public Vector3 Behind() => At(-6f) + new Vector3(0f, 16f, 0f);

        public Vector3 Side() => At(12f) + (Across * 14f) + new Vector3(0f, 11f, 0f);

        public Vector3 Close() => At(3f) + (Across * 2.5f) + new Vector3(0f, 2.5f, 0f);

        public override string ToString() => FormattableString.Invariant($"{Origin.ToString("F1")} down {Down.ToString("F2")} slope {Slope:0.00}");
    }
}
