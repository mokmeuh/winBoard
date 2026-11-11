using System.Buffers.Binary;
using System.Text;

namespace WinBoard.Services.Gemini;

/// <summary>Encodage PCM 16 bits en fichier WAV (en-tête RIFF).</summary>
public static class WavEncoder
{
    public static byte[] Encode(ReadOnlySpan<short> pcm16, int sampleRate, int channels)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

        int dataLen = pcm16.Length * 2;
        var buf = new byte[44 + dataLen];
        var s = buf.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 36 + dataLen);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(s[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(s[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(s[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(s[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(s[34..], 16);
        Encoding.ASCII.GetBytes("data").CopyTo(s[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], dataLen);
        for (int i = 0; i < pcm16.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(s[(44 + i * 2)..], pcm16[i]);
        return buf;
    }
}
