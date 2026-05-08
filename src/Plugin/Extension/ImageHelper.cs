using System;
using System.IO;

namespace KK_CardCompression.Extension
{
    public static class ImageHelper
    {
        private const int IEND_MAGIC = 0x49454E44; // 'IEND' as big-endian int32

        public static byte[] ReadToEnd(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        public static long GetPngSize(BinaryReader br) => GetPngSize(br.BaseStream);

        public static long GetPngSize(Stream st)
        {
            if (st == null) return 0L;

            long position = st.Position;
            try
            {
                byte[] pngSignature = new byte[8];
                byte[] ihdrData = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
                st.Read(pngSignature, 0, 8);
                for (int i = 0; i < 8; i++)
                {
                    if (pngSignature[i] != ihdrData[i])
                    {
                        st.Seek(position, SeekOrigin.Begin);
                        return 0L;
                    }
                }

                bool flag = true;
                while (flag)
                {
                    byte[] widthBytes = new byte[4];
                    st.Read(widthBytes, 0, 4);
                    Array.Reverse(widthBytes);
                    int chunkLength = BitConverter.ToInt32(widthBytes, 0);
                    byte[] heightBytes = new byte[4];
                    st.Read(heightBytes, 0, 4);
                    int chunkType = BitConverter.ToInt32(heightBytes, 0);
                    if (chunkType == IEND_MAGIC) flag = false;
                    if (chunkLength + 4 > st.Length - st.Position)
                    {
                        st.Seek(position, SeekOrigin.Begin);
                        return 0L;
                    }
                    st.Seek(chunkLength + 4, SeekOrigin.Current);
                }

                long num = st.Position - position;
                st.Seek(position, SeekOrigin.Begin);
                return num;
            }
            catch (EndOfStreamException)
            {
                st.Seek(position, SeekOrigin.Begin);
                return 0L;
            }
        }

        public static void SkipPng(Stream st)
        {
            if (st == null) throw new ArgumentNullException(nameof(st));
            long pngSize = GetPngSize(st);
            st.Seek(pngSize, SeekOrigin.Current);
        }

        public static void SkipPng(BinaryReader br)
        {
            if (br == null) throw new ArgumentNullException(nameof(br));
            long pngSize = GetPngSize(br);
            br.BaseStream.Seek(pngSize, SeekOrigin.Current);
        }

        public static byte[] LoadPngBytes(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using (FileStream st = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                return LoadPngBytes(st);
        }

        public static byte[] LoadPngBytes(Stream st)
        {
            if (st == null) throw new ArgumentNullException(nameof(st));
            using (BinaryReader reader = new BinaryReader(st))
                return LoadPngBytes(reader);
        }

        public static byte[] LoadPngBytes(BinaryReader br)
        {
            if (br == null) throw new ArgumentNullException(nameof(br));
            long pngSize = GetPngSize(br);
            if (pngSize == 0) throw new InvalidOperationException("Failed to read PNG data from stream (invalid or missing PNG signature/chunks).");
            return br.ReadBytes((int)pngSize);
        }
    }
}
