using System;
using System.IO;
using System.Reflection;

namespace UH60FastRopeInsertion
{
    internal static class RopeAudioData
    {
        internal static float[] Read(out int rate)
        {
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("UH60.RopeSlide"))
            {
                if (input == null) throw new InvalidDataException("Embedded rope audio is missing.");
                using (var reader = new BinaryReader(input))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Rope audio must be RIFF PCM.");
                    reader.ReadUInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Rope audio must be WAVE.");
                    rate = 0; int channels = 0, bits = 0, format = 0; byte[] data = null;
                    while (input.Position + 8 <= input.Length)
                    {
                        string id = new string(reader.ReadChars(4));
                        uint length = reader.ReadUInt32();
                        long next = input.Position + length + (length & 1);
                        if (length > 4000000 || next > input.Length) throw new InvalidDataException("Invalid rope audio chunk.");
                        if (id == "fmt ")
                        {
                            format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                            reader.ReadUInt32(); reader.ReadUInt16(); bits = reader.ReadUInt16();
                        }
                        if (id == "data") data = reader.ReadBytes((int)length);
                        input.Position = next;
                    }
                    if (format != 1 || channels != 1 || bits != 16 || rate != 48000 || data == null
                        || data.Length < rate || data.Length % 2 != 0) throw new InvalidDataException("Expected mono48k16 rope audio.");
                    var samples = new float[data.Length / 2];
                    for (int i = 0; i < samples.Length; i++) samples[i] = (short)(data[i * 2] | data[i * 2 + 1] << 8) / 32768f;
                    return LoopSeam(samples, rate / 12);
                }
            }
        }

        internal static float[] LoopSeam(float[] input, int blend)
        {
            if (input == null || blend < 2 || input.Length < blend * 4) throw new InvalidDataException("Rope loop too short.");
            var result = new float[input.Length - blend];
            Array.Copy(input, blend, result, 0, result.Length);
            for (int i = 0; i < blend; i++)
            {
                float p = i / (float)(blend - 1);
                int index = result.Length - blend + i;
                result[index] = result[index] * (1f - p) + input[i] * p;
            }
            return result;
        }
    }
}
