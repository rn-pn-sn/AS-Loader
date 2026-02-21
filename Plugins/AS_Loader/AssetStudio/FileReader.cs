using System;
using System.IO;
using System.Text;

namespace AssetStudio
{
    public class FileReader : EndianBinaryReader
    {
        public string FullPath;
        public string FileName;
        public FileType FileType;

        private static readonly byte[] gzipMagic = { 0x1f, 0x8b };
        private static readonly byte[] brotliMagic = { 0x62, 0x72, 0x6F, 0x74, 0x6C, 0x69 };
        private static readonly byte[] zipMagic = { 0x50, 0x4B, 0x03, 0x04 };
        private static readonly byte[] zipSpannedMagic = { 0x50, 0x4B, 0x07, 0x08 };
        private static readonly byte[] unityFsMagic = { 0x55, 0x6E, 0x69, 0x74, 0x79, 0x46, 0x53, 0x00 };
        private static readonly int headerBuffLen = 1152;
        private static byte[] headerBuff = new byte[headerBuffLen];

        public FileReader(string path) : this(path, TryOpenFile(path)) { }

        private static FileStream TryOpenFile(string path)
        {
            try
            {
                return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            }
            catch (Exception ex)
            {
                throw new IOException($"[AssetStudio:FileReader] Failed to open file: {path}", ex);
            }
        }

        public FileReader(string path, Stream stream) : base(stream, EndianType.BigEndian)
        {
            FullPath = Path.GetFullPath(path);
            FileName = Path.GetFileName(path);
            FileType = CheckFileType();
        }

        private FileType CheckFileType()
        {
            long originalPosition = Position;

            var headerBuff = new byte[headerBuffLen];
            int dataLen = Read(headerBuff, 0, headerBuffLen);

            Position = originalPosition;

            string signature = ReadStringToNullFromArray(headerBuff, 0, 20);
            switch (signature)
            {
                case "UnityWeb":
                case "UnityRaw":
                case "UnityArchive":
                case "UnityFS":
                    CheckBundleDataOffset(headerBuff, dataLen);
                    return FileType.BundleFile;
                case "UnityWebData1.0":
                case "TuanjieWebData1.0":
                    return FileType.WebFile;
                default:
                    {
                        if (dataLen > 2 && ByteArraysEqual(headerBuff, 0, gzipMagic, 0, 2))
                        {
                            return FileType.GZipFile;
                        }

                        if (dataLen > 38 && ByteArraysEqual(headerBuff, 32, brotliMagic, 0, 6))
                        {
                            return FileType.BrotliFile;
                        }

                        if (IsSerializedFile(headerBuff, dataLen))
                        {
                            return FileType.AssetsFile;
                        }

                        if (dataLen > 4 &&
                            (ByteArraysEqual(headerBuff, 0, zipMagic, 0, 4) ||
                             ByteArraysEqual(headerBuff, 0, zipSpannedMagic, 0, 4)))
                        {
                            return FileType.ZipFile;
                        }

                        if (CheckBundleDataOffset(headerBuff, dataLen))
                        {
                            return FileType.BundleFile;
                        }

                        return FileType.ResourceFile;
                    }
            }
        }

        private bool IsSerializedFile(byte[] buff, int dataLen)
        {
            var fileSize = BaseStream.Length;
            if (fileSize < 20)
            {
                return false;
            }
            var isBigEndian = Endian == EndianType.BigEndian;

            long m_FileSize = ReadUInt32FromArray(buff, 4, isBigEndian);
            var m_Version = ReadUInt32FromArray(buff, 8, isBigEndian);
            long m_DataOffset = ReadUInt32FromArray(buff, 12, isBigEndian);

            if (m_Version >= 22)
            {
                if (fileSize < 48)
                {
                    return false;
                }
                m_FileSize = ReadInt64FromArray(buff, 24, isBigEndian);
                m_DataOffset = ReadInt64FromArray(buff, 32, isBigEndian);
            }

            if (m_FileSize != fileSize || m_DataOffset > fileSize)
            {
                return false;
            }

            return true;
        }

        private bool CheckBundleDataOffset(byte[] buff, int dataLen)
        {
            int lastOffset = LastIndexOf(buff, dataLen, unityFsMagic);
            if (lastOffset <= 0)
                return false;

            int firstOffset = IndexOf(buff, dataLen, unityFsMagic, 0);
            if (firstOffset == lastOffset || lastOffset - firstOffset < 200)
            {
                Position = lastOffset;
                return true;
            }

            int pos = firstOffset + 12;
            pos += ReadStringToNullFromArray(buff, pos).Length + 1;
            pos += ReadStringToNullFromArray(buff, pos).Length + 1;

            if (pos + 8 <= dataLen)
            {
                var bundleSize = ReadInt64FromArray(buff, pos, Endian == EndianType.BigEndian);
                if (bundleSize > 200 && firstOffset + bundleSize < lastOffset)
                {
                    Position = firstOffset;
                    return true;
                }
            }

            Position = lastOffset;
            return true;
        }

        private static string ReadStringToNullFromArray(byte[] data, int offset, int maxLength = int.MaxValue)
        {
            int end = offset;
            int remaining = Math.Min(data.Length - offset, maxLength);

            while (end < data.Length && end - offset < remaining && data[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(data, offset, end - offset);
        }

        private static uint ReadUInt32FromArray(byte[] data, int offset, bool isBigEndian)
        {
            if (offset + 4 > data.Length)
                return 0;

            uint value;
            if (isBigEndian)
            {
                value = (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                               (data[offset + 2] << 8) | data[offset + 3]);
            }
            else
            {
                value = (uint)(data[offset] | (data[offset + 1] << 8) |
                               (data[offset + 2] << 16) | (data[offset + 3] << 24));
            }
            return value;
        }

        private static long ReadInt64FromArray(byte[] data, int offset, bool isBigEndian)
        {
            if (offset + 8 > data.Length)
                return 0;

            ulong value;
            if (isBigEndian)
            {
                value = ((ulong)data[offset] << 56) | ((ulong)data[offset + 1] << 48) |
                        ((ulong)data[offset + 2] << 40) | ((ulong)data[offset + 3] << 32) |
                        ((ulong)data[offset + 4] << 24) | ((ulong)data[offset + 5] << 16) |
                        ((ulong)data[offset + 6] << 8) | data[offset + 7];
            }
            else
            {
                value = data[offset] | ((ulong)data[offset + 1] << 8) |
                        ((ulong)data[offset + 2] << 16) | ((ulong)data[offset + 3] << 24) |
                        ((ulong)data[offset + 4] << 32) | ((ulong)data[offset + 5] << 40) |
                        ((ulong)data[offset + 6] << 48) | ((ulong)data[offset + 7] << 56);
            }
            return (long)value;
        }

        private static bool ByteArraysEqual(byte[] array1, int offset1, byte[] array2, int offset2, int length)
        {
            if (offset1 + length > array1.Length || offset2 + length > array2.Length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (array1[offset1 + i] != array2[offset2 + i])
                    return false;
            }
            return true;
        }

        private static int IndexOf(byte[] array, int arrayLength, byte[] pattern, int startIndex)
        {
            if (pattern.Length == 0 || arrayLength == 0 || startIndex >= arrayLength)
                return -1;

            for (int i = startIndex; i <= arrayLength - pattern.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (array[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }

        private static int LastIndexOf(byte[] array, int arrayLength, byte[] pattern)
        {
            if (pattern.Length == 0 || arrayLength == 0)
                return -1;

            for (int i = arrayLength - pattern.Length; i >= 0; i--)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (array[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }
    }
}
