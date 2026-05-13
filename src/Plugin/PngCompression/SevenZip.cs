using KK_CardCompression.PngCompression;
using System;
using System.IO;

namespace KK_CardCompression.SevenZip
{
    public enum LzmaSpeed : int
    {
        Fastest = 5,
    }

    public enum DictionarySize : int
    {
        VeryLarge = 1 << 26,
    }

    public static class LZMA
    {
        public static void Compress(Stream input, Stream output, LzmaSpeed speed = LzmaSpeed.Fastest, DictionarySize dictionarySize = DictionarySize.VeryLarge, LongProgressCallback onProgress = null)
        {
            int posStateBits = 2;
            int litContextBits = 3;
            int litPosBits = 0;
            int numFastBytes = (int)speed;
            string matchFinder = "BT4";
            bool endMarker = true;

            global::SevenZip.CoderPropID[] propIDs =
            {
                global::SevenZip.CoderPropID.DictionarySize,
                global::SevenZip.CoderPropID.PosStateBits,
                global::SevenZip.CoderPropID.LitContextBits,
                global::SevenZip.CoderPropID.LitPosBits,
                global::SevenZip.CoderPropID.NumFastBytes,
                global::SevenZip.CoderPropID.MatchFinder,
                global::SevenZip.CoderPropID.EndMarker
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

            global::SevenZip.Compression.LZMA.Encoder lzmaEncoder = new global::SevenZip.Compression.LZMA.Encoder();
            lzmaEncoder.SetCoderProperties(propIDs, properties);
            lzmaEncoder.WriteCoderProperties(output);
            long fileSize = input.Length;
            for (int i = 0; i < 8; i++) output.WriteByte((byte)(fileSize >> (8 * i)));

            global::SevenZip.ICodeProgress prg = null;
            if (onProgress != null)
            {
                prg = new DelegateCodeProgress(onProgress);
            }
            lzmaEncoder.Code(input, output, -1, -1, prg);
        }

        public static void Decompress(Stream input, Stream output, LongProgressCallback onProgress = null)
        {
            global::SevenZip.Compression.LZMA.Decoder decoder = new global::SevenZip.Compression.LZMA.Decoder();

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

            global::SevenZip.ICodeProgress prg = null;
            if (onProgress != null)
            {
                prg = new DelegateCodeProgress(onProgress);
            }
            long compressedSize = input.Length - input.Position;
            decoder.Code(input, output, compressedSize, fileLength, prg);
        }

        private class DelegateCodeProgress : global::SevenZip.ICodeProgress
        {
            private readonly LongProgressCallback handler;
            public DelegateCodeProgress(LongProgressCallback handler) => this.handler = handler;
            public void SetProgress(long inSize, long outSize) => handler(inSize, outSize);
        }
    }
}
