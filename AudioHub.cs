using Godot;
using System.Collections.Generic;

// Процедурные звуковые эффекты: синтез PCM в коде, без внешних файлов.
// Autoload singleton: AudioHub.Instance.PlayHit() и т.д.
public partial class AudioHub : Node
{
    public static AudioHub Instance { get; private set; }

    private const int MixRate = 22050;
    private readonly Dictionary<string, AudioStreamWav> _sounds = new();
    private readonly List<AudioStreamPlayer> _players = new();
    private const int PoolSize = 8;

    public bool Muted => AudioServer.IsBusMute(0);

    public void ToggleMute()
    {
        AudioServer.SetBusMute(0, !Muted);
    }

    // Мастер-громкость 0..1 (через децибелы шины).
    public float Volume01
    {
        get => Mathf.DbToLinear(AudioServer.GetBusVolumeDb(0));
        set => AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Mathf.Clamp(value, 0.001f, 1f)));
    }

    public override void _Ready()
    {
        Instance = this;
        // Always: клики в меню паузы и ESC должны работать на паузе.
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < PoolSize; i++)
        {
            var p = new AudioStreamPlayer();
            AddChild(p);
            _players.Add(p);
        }
        BuildSounds();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo
            && keyEvent.Keycode == Key.Escape)
        {
            GetNodeOrNull<MapGenerator>("/root/Main")?.TogglePauseMenu();
        }
    }

    public void PlayHit() => Play("hit", -4f);
    public void PlayCapture() => Play("capture", -4f);
    public void PlayBuild() => Play("build", -6f);
    public void PlaySpawn() => Play("spawn", -6f);
    public void PlayClick() => Play("click", -10f);
    public void PlayVictory() => Play("victory", -4f);
    public void PlayDefeat() => Play("defeat", -4f);

    private void Play(string name, float volumeDb)
    {
        if (!_sounds.TryGetValue(name, out var stream)) return;
        AudioStreamPlayer slot = null;
        foreach (var p in _players)
        {
            if (!p.Playing) { slot = p; break; }
        }
        slot ??= _players[0];
        slot.Stream = stream;
        slot.VolumeDb = volumeDb;
        slot.Play();
    }

    private void BuildSounds()
    {
        _sounds["hit"] = Mix(
            Tone(90f, 0.15f, 1f),
            Noise(0.12f, 0.7f));
        _sounds["capture"] = Concat(
            Tone(523f, 0.12f, 0.8f),
            Tone(659f, 0.12f, 0.8f),
            Tone(784f, 0.2f, 0.9f));
        _sounds["build"] = Concat(
            Tone(220f, 0.1f, 0.7f),
            Tone(440f, 0.15f, 0.7f));
        _sounds["spawn"] = ToWav(Tone(300f, 0.18f, 0.8f, 0.5f));
        _sounds["click"] = ToWav(Tone(800f, 0.06f, 0.6f));
        _sounds["victory"] = Concat(
            Tone(523f, 0.15f, 0.8f),
            Tone(659f, 0.15f, 0.8f),
            Tone(784f, 0.15f, 0.8f),
            Tone(1047f, 0.3f, 0.9f));
        _sounds["defeat"] = Concat(
            Tone(392f, 0.2f, 0.8f),
            Tone(311f, 0.2f, 0.8f),
            Tone(233f, 0.4f, 0.9f));
    }

    // Синус с экспоненциальным затуханием; slide = доля сдвига частоты к концу.
    private float[] Tone(float freq, float dur, float vol, float slide = 0f)
    {
        int n = (int)(MixRate * dur);
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / MixRate;
            float f = freq * (1f + slide * t / dur);
            float env = Mathf.Exp(-4f * t / dur);
            s[i] = Mathf.Sin(Mathf.Tau * f * t) * vol * env;
        }
        return s;
    }

    private float[] Noise(float dur, float vol)
    {
        int n = (int)(MixRate * dur);
        var s = new float[n];
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / MixRate;
            float env = Mathf.Exp(-6f * t / dur);
            s[i] = rng.RandfRange(-1f, 1f) * vol * env;
        }
        return s;
    }

    private AudioStreamWav Mix(params float[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n = Mathf.Max(n, p.Length);
        var s = new float[n];
        foreach (var p in parts)
            for (int i = 0; i < p.Length; i++) s[i] += p[i];
        return ToWav(s);
    }

    private AudioStreamWav Concat(params float[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var s = new float[n];
        int k = 0;
        foreach (var p in parts)
            foreach (var v in p) s[k++] = v;
        return ToWav(s);
    }

    private AudioStreamWav ToWav(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(v & 0xFF);
            bytes[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        var wav = new AudioStreamWav();
        wav.Format = AudioStreamWav.FormatEnum.Format16Bits;
        wav.MixRate = MixRate;
        wav.Stereo = false;
        wav.Data = bytes;
        return wav;
    }
}
