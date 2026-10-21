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
public sealed class Recorder : BaseUnityPlugin
{
    private static Recorder _instance;

    private static Vector3? _cameraAt;

    private static Vector3 _cameraLook;

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
        _instance = this;
        _harmony = new Harmony("Tie.FlowingWater.Recorder");
        _harmony.Patch(AccessTools.Method(typeof(GameCamera), "LateUpdate"), postfix: new HarmonyMethod(typeof(Recorder), nameof(CameraPostfix)));
        _harmony.Patch(AccessTools.Method(typeof(Terminal), nameof(Terminal.AddString), [typeof(string)]), postfix: new HarmonyMethod(typeof(Recorder), nameof(ConsolePostfix)));
        Note($"recorder ready, output {_out}, platform {Application.platform}, graphics {SystemInfo.graphicsDeviceType}");
    }

    private static void CameraPostfix(GameCamera __instance)
    {
        if (_cameraAt.HasValue)
        {
            __instance.transform.position = _cameraAt.Value;
            __instance.transform.rotation = Quaternion.LookRotation(_cameraLook - _cameraAt.Value);
        }
    }

    private static void ConsolePostfix(string text)
    {
        if (_instance != null && text != null && text.StartsWith("flowingwater", StringComparison.Ordinal))
        {
            _instance.Note("  console: " + text);
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

    private void Note(string text)
    {
        string line = $"[{Time.realtimeSinceStartup,7:0.0}] {text}";
        Logger.LogInfo(line);
        _log?.WriteLine(line);
    }

    private IEnumerator Run()
    {
        IEnumerator steps = Steps();
        while (true)
        {
            object current;
            try
            {
                if (!steps.MoveNext())
                {
                    break;
                }

                current = steps.Current;
            }
            catch (Exception e)
            {
                Note("FAILED: " + e);
                break;
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
        Site steep = FindSite(spawn, 0.35f, 0.75f, true) ?? FindSite(spawn, 0.3f, 0.8f, false);
        Site gentle = FindSite(spawn, 0.06f, 0.18f, true) ?? FindSite(spawn, 0.05f, 0.2f, false);
        Note($"steep site {steep}");
        Note($"gentle site {gentle}");
        // A scenario of its own (FW_REC_SCENARIO), else every command below.
        IEnumerator scenario = Environment.GetEnvironmentVariable("FW_REC_SCENARIO") switch
        {
            "cascade" => Cascade(spawn),
            "look" => Look(steep, gentle),
            "grid" => Grid(steep, gentle, Environment.GetEnvironmentVariable("FW_REC_PLACE") != "0"),
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
        AudioTap.On = true;
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
        float[] sound = AudioTap.Samples();
        WriteWav(Path.Combine(_out, name + ".wav"), sound, AudioTap.Channels, AudioSettings.outputSampleRate);
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
        yield return Go(steep.At(0f));
        Command("flowingwater source 2");
        yield return Go(gentle.At(0f));
        Command("flowingwater source 2");
        yield return Go(steep.At(8f));
        yield return new WaitForSeconds(120f);
        // Unset or empty: the material as it is.
        string configured = Environment.GetEnvironmentVariable("FW_REC_LOOKS");
        string sets = string.IsNullOrWhiteSpace(configured) ? "default=" : configured;
        foreach (string set in sets.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // "name=property value;property value"; a name alone changes nothing.
            string[] named = set.Split(new[] { '=' }, 2);
            string properties = named.Length > 1 ? named[1] : string.Empty;
            foreach (string property in properties.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Command("flowingwater mat custom " + property.Trim());
            }

            yield return new WaitForSeconds(1f);
            foreach (Site site in new[] { steep, gentle })
            {
                string where = site == steep ? "steep" : "gentle";

                // The server sends a zone's water to the players near it.
                yield return Go(site.At(8f));
                yield return Shot(site.At(10f) + (site.Across * 6f) + new Vector3(0f, 7f, 0f), site.At(12f), $"{named[0]}-{where}-near");
                yield return Shot(site.Side(), site.At(12f), $"{named[0]}-{where}-side");
                yield return Shot(site.At(15f) + (site.Across * 50f) + new Vector3(0f, 35f, 0f), site.At(15f), $"{named[0]}-{where}-far");
            }
        }
    }

    // The point with the deepest water between these distances downhill, searching 12 m to each
    // side; the middle of the range when there is no water there.
    private Vector3 Wet(Site site, float from, float to)
    {
        Vector3 best = site.At((from + to) * 0.5f);
        float bestDepth = 0f;
        for (float along = from; along <= to; along += 1f)
        {
            for (float side = -12f; side <= 12f; side += 1f)
            {
                Vector3 p = site.At(along) + (site.Across * side);
                if (!ZoneSystem.instance.GetGroundHeight(p, out float ground))
                {
                    continue;
                }

                p.y = ground;
                float depth = Floating.GetLiquidLevel(p + (Vector3.up * 0.05f), 1f, LiquidType.Water) - ground;
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = p;
                }
            }
        }

        Note($"wettest point {from}..{to} m downhill: {best.ToString("F1")}, {bestDepth:0.00} m deep");
        return best;
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

    private IEnumerator Go(Vector3 point)
    {
        Player player = Player.m_localPlayer;
        float ground = WorldGenerator.instance.GetHeight(point.x, point.z);
        if (ZoneSystem.instance.GetGroundHeight(point, out float loaded))
        {
            ground = loaded;
        }

        var target = new Vector3(point.x, ground + 0.5f, point.z);
        float until = Time.time + 30f;
        while (!player.TeleportTo(target, player.transform.rotation, true))
        {
            if (Time.time > until)
            {
                throw new InvalidOperationException($"teleport to {target} refused for 30 s");
            }

            yield return new WaitForSeconds(0.5f);
        }

        until = Time.time + 60f;
        while (player.IsTeleporting() && Time.time < until)
        {
            yield return null;
        }

        yield return new WaitForSeconds(4f);
        float off = Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), new Vector2(target.x, target.z));
        if (off > 5f)
        {
            throw new InvalidOperationException($"teleport to {target} ended at {player.transform.position}");
        }

        Note($"at {player.transform.position.ToString("F1")}");
    }

    private IEnumerator Views(Site site, string name)
    {
        yield return Shot(site.Behind(), site.At(14f), name + "-behind");
        yield return Shot(site.Side(), site.At(12f), name + "-side");
        yield return Shot(site.Close(), site.At(7f), name + "-close");
    }

    // The first of a few places around the asked one from which nothing solid stands between the
    // camera and what it looks at (trees, rocks, buildings), or the asked one.
    private Vector3 Clear(Vector3 from, Vector3 at)
    {
        Vector3 away = from - at;
        foreach (float turn in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
        {
            foreach (float rise in new[] { 0f, 6f, 12f })
            {
                Vector3 candidate = at + (Quaternion.Euler(0f, turn, 0f) * away) + new Vector3(0f, rise, 0f);
                if (!Physics.Linecast(candidate, at + (Vector3.up * 1.5f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Physics.OverlapSphere(candidate, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).Length == 0)
                {
                    return candidate;
                }
            }
        }

        Note($"no clear view of {at.ToString("F0")} found near {from.ToString("F0")}");
        return from;
    }

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
        _cameraAt = from;
        _cameraLook = at;
        yield return new WaitForSeconds(0.6f);
        int frames = Mathf.RoundToInt(seconds * fps);
        float step = 1f / fps;
        for (int i = 0; i < frames; i++)
        {
            float next = Time.time + step;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, $"{name}-{i:000}.png"));
            while (Time.time < next)
            {
                yield return null;
            }
        }

        Note($"burst {name}: {frames} frames at {fps} fps");
    }

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

                var gradient = new Vector2(generator.GetHeight(x + 2f, z) - generator.GetHeight(x - 2f, z), generator.GetHeight(x, z + 2f) - generator.GetHeight(x, z - 2f)) / 4f;
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
