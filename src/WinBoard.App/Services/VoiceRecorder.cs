using System.IO;
using NAudio.Wave;

namespace WinBoard.App.Services;

/// <summary>Aucun périphérique d'enregistrement disponible.</summary>
public sealed class MicrophoneNotFoundException : Exception
{
    public MicrophoneNotFoundException() : base("Aucun micro détecté.") { }
}

/// <summary>Capture micro 16 kHz, mono, 16 bits (NAudio), limitée à 60 secondes.</summary>
public sealed class VoiceRecorder : IDisposable
{
    public const int SampleRate = 16000;
    public const int MaxSeconds = 60;

    private readonly object _gate = new();
    private WaveInEvent? _wave;
    private MemoryStream? _buffer;
    private System.Threading.Timer? _limit;

    /// <summary>Durée maximale atteinte (levé sur un fil du pool : l'appelant doit revenir sur l'interface).</summary>
    public event Action? LimitReached;

    public bool IsRecording => _wave != null;

    public void Start()
    {
        if (_wave != null) return;
        if (WaveInEvent.DeviceCount == 0) throw new MicrophoneNotFoundException();

        var buffer = new MemoryStream();
        var wave = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 100 };
        wave.DataAvailable += (_, e) =>
        {
            lock (_gate)
            {
                // Garde-fou : jamais plus de MaxSeconds + 1 s en mémoire.
                if (buffer.Length < (MaxSeconds + 1) * SampleRate * 2)
                    buffer.Write(e.Buffer, 0, e.BytesRecorded);
            }
        };
        wave.StartRecording();
        _buffer = buffer;
        _wave = wave;
        _limit = new System.Threading.Timer(_ => LimitReached?.Invoke(), null, MaxSeconds * 1000, Timeout.Infinite);
    }

    /// <summary>Arrête la capture et renvoie les échantillons enregistrés.</summary>
    public short[] Stop()
    {
        var wave = _wave;
        var buffer = _buffer;
        _wave = null;
        _buffer = null;
        _limit?.Dispose();
        _limit = null;
        if (wave == null || buffer == null) return Array.Empty<short>();

        try { wave.StopRecording(); } catch { }
        byte[] bytes;
        lock (_gate) bytes = buffer.ToArray();
        // L'événement d'arrêt est posté sur le contexte de l'interface : on ne l'attend pas.
        _ = Task.Run(() => { try { wave.Dispose(); } catch { } });

        var samples = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);
        return samples;
    }

    public void Dispose()
    {
        if (_wave != null) Stop();
    }
}
