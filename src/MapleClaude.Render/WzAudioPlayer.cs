using MapleClaude.Wz;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Media;
using NAudio.Wave;

namespace MapleClaude.Render;

/// <summary>
/// Plays WZ-sourced audio (typically MP3 BGM) via MonoGame's
/// <see cref="MediaPlayer"/>. We extract the audio bytes from a
/// <see cref="WzSound"/> node to a per-asset temp file (since <see cref="Song"/>
/// requires a URI), cache the file path by node identity, and stream from
/// disk. Looping is on by default for BGM use.
/// </summary>
public sealed class WzAudioPlayer : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _tempDir;
    private readonly Dictionary<WzSound, (Song Song, string Path)> _cache = new();
    private readonly Dictionary<WzSound, SoundEffect?> _effects = new();
    private Song? _current;
    private bool _disposed;

    public WzAudioPlayer(ILogger logger)
    {
        _logger = logger;
        _tempDir = Path.Combine(Path.GetTempPath(), "MapleClaude", "audio");
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Starts playing the given sound on loop (BGM mode). Stops any currently
    /// playing track. Returns <c>false</c> if the sound couldn't be prepared.
    /// </summary>
    public bool PlayLoop(WzSound? sound)
    {
        if (_disposed || sound is null)
        {
            return false;
        }

        try
        {
            if (!_cache.TryGetValue(sound, out var entry))
            {
                var path = Path.Combine(_tempDir, $"bgm_{Guid.NewGuid():N}.mp3");
                File.WriteAllBytes(path, sound.AudioBytes.ToArray());
                var song = Song.FromUri(Path.GetFileNameWithoutExtension(path), new Uri(path));
                entry = (song, path);
                _cache[sound] = entry;
            }
            _current = entry.Song;
            MediaPlayer.IsRepeating = true;
            MediaPlayer.Volume = 0.6f;
            MediaPlayer.Play(_current);
            _logger.LogInformation("BGM playing: {Duration}ms", sound.DurationMs);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start BGM playback");
            return false;
        }
    }

    /// <summary>
    /// Plays a short SFX one-shot via <see cref="SoundEffect"/>. Decodes the
    /// WzSound's MP3 payload to PCM on first use, caches the
    /// <see cref="SoundEffect"/>, then plays. Does NOT interrupt BGM (BGM
    /// runs through MediaPlayer; SFX through XAudio2 voices). Returns
    /// <c>false</c> if decoding failed.
    /// </summary>
    public bool PlayEffect(WzSound? sound)
    {
        if (_disposed || sound is null)
        {
            return false;
        }
        if (!_effects.TryGetValue(sound, out var effect))
        {
            effect = TryDecodeEffect(sound);
            _effects[sound] = effect; // cache failures too so we don't retry every click
        }
        if (effect is null)
        {
            return false;
        }
        try
        {
            effect.Play();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SFX play failed");
            return false;
        }
    }

    private SoundEffect? TryDecodeEffect(WzSound sound)
    {
        try
        {
            var mp3 = sound.AudioBytes.ToArray();
            using var mp3Stream = new MemoryStream(mp3, writable: false);
            using var mp3Reader = new Mp3FileReader(mp3Stream);
            using var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader);
            using var wav = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(wav, pcmStream);
            wav.Position = 0;
            var effect = SoundEffect.FromStream(wav);
            _logger.LogInformation("Decoded SFX: {Bytes} mp3 bytes → SoundEffect", mp3.Length);
            return effect;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode WZ sound to SoundEffect ({Bytes} bytes)",
                sound.AudioBytes.Length);
            return null;
        }
    }

    public void Stop()
    {
        if (_current != null)
        {
            try
            {
                MediaPlayer.Stop();
            }
            catch
            {
                // Ignore: MediaPlayer can throw when running headless or with no audio device.
            }
            _current = null;
        }
    }

    /// <summary>Pauses playback (window lost focus). Idempotent.</summary>
    public void Pause()
    {
        if (_current is null)
        {
            return;
        }
        try
        {
            MediaPlayer.Pause();
        }
        catch
        {
            // Ignore.
        }
    }

    /// <summary>Resumes playback (window regained focus). Idempotent.</summary>
    public void Resume()
    {
        if (_current is null)
        {
            return;
        }
        try
        {
            MediaPlayer.Resume();
        }
        catch
        {
            // Ignore.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        foreach (var (_, entry) in _cache)
        {
            try
            {
                entry.Song.Dispose();
            }
            catch
            {
                // Ignore.
            }
            try
            {
                if (File.Exists(entry.Path))
                {
                    File.Delete(entry.Path);
                }
            }
            catch
            {
                // Best-effort cleanup; OS will eventually clean the temp dir.
            }
        }
        _cache.Clear();
        foreach (var fx in _effects.Values)
        {
            try
            {
                fx?.Dispose();
            }
            catch
            {
                // Ignore.
            }
        }
        _effects.Clear();
    }
}
