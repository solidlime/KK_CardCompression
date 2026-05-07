using SevenZip.Compression.LZMA;
using System;
using System.IO;

namespace SevenZip
{
    public enum LzmaSpeed : int
    {
        Fastest = 5,
        VeryFast = 8,
        Fast = 16,
        Medium = 32,
        Slow = 64,
        VerySlow = 128,
    }

    public enum DictionarySize : int
    {
        VerySmall = 1 << 16,
        Small = 1 << 20,
        Medium = 1 << 22,
        Large = 1 << 23,
        Larger = 1 << 24,
        VeryLarge = 1 << 26,
    }

    public static class LZMA
    {
        public static void Compress(Stream input, Stream output, LzmaSpeed speed = LzmaSpeed.Fastest, DictionarySize dictionarySize = DictionarySize.VerySmall, Action<long, long> onProgress = null)
        {
            int posStateBits = 2;
            int litContextBits = 3;
            int litPosBits = 0;
            int numFastBytes = (int)speed;
            string matchFinder = "BT4";
            bool endMarker = true;

            CoderPropID[] propIDs =
            {
                CoderPropID.DictionarySize,
                CoderPropID.PosStateBits,
                CoderPropID.LitContextBits,
                CoderPropID.LitPosBits,
                CoderPropID.NumFastBytes,
                CoderPropID.MatchFinder,
                CoderPropID.EndMarker
            };

            object[] properties =
            {
                (int)dictionarySize,
                posStateBits,
                (int)litContextBits,
                (int)litPosBits,
                numFastBytes,
                matchFinder,
                endMarker
            };

            Encoder lzmaEncoder = new Encoder();
            lzmaEncoder.SetCoderProperties(propIDs, properties);
            lzmaEncoder.WriteCoderProperties(output);
            long fileSize = input.Length;
            for (int i = 0; i < 8; i++) output.WriteByte((byte)(fileSize >> (8 * i)));

            ICodeProgress prg = null;
            if (onProgress != null)
            {
                prg = new DelegateCodeProgress(onProgress);
            }
            lzmaEncoder.Code(input, output, -1, -1, prg);
        }

        public static void Decompress(Stream input, Stream output, Action<long, long> onProgress = null)
        {
            Decoder decoder = new Decoder();

            byte[] properties = new byte[5];
            if (input.Read(properties, 0, 5) != 5)
            {
                throw new Exception("input .lzma is too short");
            }
            decoder.SetDecoderProperties(properties);

            long fileLength = 0;
            for (int i = 0; i < 8; i++)
            {
                int v = input.ReadByte();
                if (v < 0) throw new Exception("Can't Read 1");
                fileLength |= ((long)(byte)v) << (8 * i);
            }

            ICodeProgress prg = null;
            if (onProgress != null)
            {
                prg = new DelegateCodeProgress(onProgress);
            }
            long compressedSize = input.Length - input.Position;
            decoder.Code(input, output, compressedSize, fileLength, prg);
        }

        private class DelegateCodeProgress : ICodeProgress
        {
            private readonly Action<long, long> handler;
            public DelegateCodeProgress(Action<long, long> handler) => this.handler = handler;
            public void SetProgress(long inSize, long outSize) => handler(inSize, outSize);
        }
    }
}
