using System;
using System.IO;

namespace Extension
{
    public static class ImageHelper
    {
        public static byte[] ReadToEnd(Stream stream)
        {
            long originalPosition = stream.Position;
            try
            {
                byte[] readBuffer = new byte[4096];
                int totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = stream.Read(readBuffer, totalBytesRead, readBuffer.Length - totalBytesRead)) > 0)
                {
                    totalBytesRead += bytesRead;
                    if (totalBytesRead == readBuffer.Length)
                    {
                        int nextByte = stream.ReadByte();
                        if (nextByte != -1)
                        {
                            byte[] temp = new byte[readBuffer.Length * 2];
                            Buffer.BlockCopy(readBuffer, 0, temp, 0, readBuffer.Length);
                            Buffer.SetByte(temp, totalBytesRead, (byte)nextByte);
                            readBuffer = temp;
                            totalBytesRead++;
                        }
                    }
                }

                byte[] buffer = readBuffer;
                if (readBuffer.Length != totalBytesRead)
                {
                    buffer = new byte[totalBytesRead];
                    Buffer.BlockCopy(readBuffer, 0, buffer, 0, totalBytesRead);
                }
                return buffer;
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        public static long GetPngSize(BinaryReader br) => GetPngSize(br.BaseStream);

        public static long GetPngSize(Stream st)
        {
            if (st == null) return 0L;

            long position = st.Position;
            try
            {
                byte[] array = new byte[8];
                byte[] array2 = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
                st.Read(array, 0, 8);
                for (int i = 0; i < 8; i++)
                {
                    if (array[i] != array2[i])
                    {
                        st.Seek(position, SeekOrigin.Begin);
                        return 0L;
                    }
                }

                bool flag = true;
                while (flag)
                {
                    byte[] array3 = new byte[4];
                    st.Read(array3, 0, 4);
                    Array.Reverse(array3);
                    int num2 = BitConverter.ToInt32(array3, 0);
                    byte[] array4 = new byte[4];
                    st.Read(array4, 0, 4);
                    int num3 = BitConverter.ToInt32(array4, 0);
                    if (num3 == 1145980233) flag = false;
                    if (num2 + 4 > st.Length - st.Position)
                    {
                        st.Seek(position, SeekOrigin.Begin);
                        return 0L;
                    }
                    st.Seek(num2 + 4, SeekOrigin.Current);
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
            long pngSize = GetPngSize(st);
            st.Seek(pngSize, SeekOrigin.Current);
        }

        public static void SkipPng(BinaryReader br)
        {
            long pngSize = GetPngSize(br);
            br.BaseStream.Seek(pngSize, SeekOrigin.Current);
        }

        public static byte[] LoadPngBytes(string path)
        {
            using (FileStream st = new FileStream(path, FileMode.Open, FileAccess.Read))
                return LoadPngBytes(st);
        }

        public static byte[] LoadPngBytes(Stream st)
        {
            using (BinaryReader br = new BinaryReader(st))
                return LoadPngBytes(br);
        }

        public static byte[] LoadPngBytes(BinaryReader br)
        {
            long pngSize = GetPngSize(br);
            if (pngSize == 0) return null;
            return br.ReadBytes((int)pngSize);
        }
    }
}
