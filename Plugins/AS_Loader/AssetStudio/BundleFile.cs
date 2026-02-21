using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AssetStudio
{
    [Flags]
    public enum ArchiveFlags
    {
        CompressionTypeMask = 0x3f,
        BlocksAndDirectoryInfoCombined = 0x40,
        BlocksInfoAtTheEnd = 0x80,
        OldWebPluginCompatibility = 0x100,
        BlockInfoNeedPaddingAtStart = 0x200
    }

    [Flags]
    public enum CnEncryptionFlags
    {
        V1 = 0x200,
        V2_V3 = 0x1400,
    }

    [Flags]
    public enum StorageBlockFlags
    {
        CompressionTypeMask = 0x3f,
        Streamed = 0x40
    }

    public enum CompressionType
    {
        Auto = -1,
        None,
        Lzma,
        Lz4,
        Lz4HC,
        Lzham,
        Zstd, //custom
        Oodle, //custom
    }

    public class BundleFile
    {
        public readonly bool IsDataAfterBundle;

        public class Header
        {
            public string signature;
            public uint version;
            public string unityVersion;
            public UnityVersion unityRevision;
            public long size;
            public uint compressedBlocksInfoSize;
            public uint uncompressedBlocksInfoSize;
            public ArchiveFlags flags;
        }

        public class StorageBlock
        {
            public uint compressedSize;
            public uint uncompressedSize;
            public StorageBlockFlags flags;
        }

        public class Node
        {
            public long offset;
            public long size;
            public uint flags;
            public string path;
        }

        public Header m_Header;
        private StorageBlock[] m_BlocksInfo;
        private Node[] m_DirectoryInfo;

        public List<StreamFile> fileList;

        public static string ReadStringToNull(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            byte b;
            while ((b = reader.ReadByte()) != 0)
                bytes.Add(b);

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        public BundleFile(FileReader reader, bool isMultiBundle = false)
        {
            m_Header = new Header();

            m_Header.signature = ReadStringToNull(reader);
            m_Header.version = reader.ReadUInt32();
            m_Header.unityVersion = ReadStringToNull(reader);

            string unityRevisionStr = ReadStringToNull(reader);
            m_Header.unityRevision = UnityVersion.TryParse(unityRevisionStr, out var ver) ? ver : new UnityVersion();

            switch (m_Header.signature)
            {
                case "UnityArchive":
                    break; //TODO
                case "UnityWeb":
                case "UnityRaw":
                    if (m_Header.version == 6)
                    {
                        goto case "UnityFS";
                    }
                    ReadHeaderAndBlocksInfo(reader);
                    using (reader)
                    {
                        ReadFiles(ReadBlocksAndDirectory(reader));
                    }
                    break;
                case "UnityFS":
                    ReadHeader(reader);

                    var bundleSize = m_Header.size;
                    var streamSize = reader.BaseStream.Length;
                    if (bundleSize > streamSize)
                        Debug.Log("Bundle size is incorrect.");
                    IsDataAfterBundle = streamSize - bundleSize > 200;

                    var unityVer = m_Header.unityRevision;

                    UnityCnCheck(reader, unityVer);

                    ReadBlocksInfoAndDirectory(reader, unityVer);

                    if (IsUncompressedBundle && !IsDataAfterBundle && !isMultiBundle)
                    {
                        Debug.Log($"[Uncompressed bundle] BlockData count: {m_BlocksInfo.Length}");
                        ReadFiles(reader.BaseStream, reader.Position);
                        break;
                    }

                    ReadFiles(ReadBlocks(reader));
                    if (!IsDataAfterBundle)
                        reader.Close();

                    break;
            }
        }

        private void ReadHeaderAndBlocksInfo(FileReader reader)
        {
            if (m_Header.version >= 4)
            {
                var hash = reader.ReadBytes(16);
                var crc = reader.ReadUInt32();
            }
            var minimumStreamedBytes = reader.ReadUInt32();
            m_Header.size = reader.ReadUInt32();
            var numberOfLevelsToDownloadBeforeStreaming = reader.ReadUInt32();
            var levelCount = reader.ReadInt32();
            m_BlocksInfo = new StorageBlock[1];
            for (int i = 0; i < levelCount; i++)
            {
                var storageBlock = new StorageBlock
                {
                    compressedSize = reader.ReadUInt32(),
                    uncompressedSize = reader.ReadUInt32(),
                };
                if (i == levelCount - 1)
                {
                    m_BlocksInfo[0] = storageBlock;
                }
            }
            if (m_Header.version >= 2)
            {
                var completeFileSize = reader.ReadUInt32();
            }
            if (m_Header.version >= 3)
            {
                var fileInfoHeaderSize = reader.ReadUInt32();
            }
            reader.Position = m_Header.size;
        }

        private Stream CreateBlocksStream(string path)
        {
            var uncompressedSizeSum = m_BlocksInfo.Sum(x => x.uncompressedSize);
            if (uncompressedSizeSum < int.MaxValue) 
                return new MemoryStream((int)uncompressedSizeSum);

            if (!Directory.Exists(Path.GetDirectoryName(path)))
            {
                var tempDir = Path.Combine(Directory.GetCurrentDirectory(), "Studio_temp");
                Directory.CreateDirectory(tempDir);
                var filename = Path.GetFileName(path);
                var hash = path.GetHashCode();
                path = Path.Combine(tempDir, $"{filename}_{hash:X}");
            }
            return new TempFileStream(path + ".temp", FileMode.Create);
        }

        private Stream ReadBlocksAndDirectory(FileReader reader)
        {
            var blocksStream = new MemoryStream();
            var isCompressed = m_Header.signature == "UnityWeb";
            foreach (var blockInfo in m_BlocksInfo)
            {
                var uncompressedBytes = reader.ReadBytes((int)blockInfo.compressedSize);
                if (isCompressed)
                {
                    using (var memoryStream = new MemoryStream(uncompressedBytes))
                    {
                        using (var decompressStream = BundleDecompressionHelper.DecompressLzmaStream(memoryStream))
                        {
                            uncompressedBytes = decompressStream.ToArray();
                        }
                    }
                }
                blocksStream.Write(uncompressedBytes, 0, uncompressedBytes.Length);
            }
            blocksStream.Position = 0;

            var blocksReader = new EndianBinaryReader(blocksStream);
            var nodesCount = blocksReader.ReadInt32();
            m_DirectoryInfo = new Node[nodesCount];
            for (var i = 0; i < nodesCount; i++)
            {
                m_DirectoryInfo[i] = new Node
                {
                    path = ReadStringToNull(blocksReader),
                    offset = blocksReader.ReadUInt32(),
                    size = blocksReader.ReadUInt32()
                };
            }

            return blocksStream;
        }

        private void ReadFiles(Stream inputStream, long blocksOffset = 0)
        {
            fileList = new List<StreamFile>(m_DirectoryInfo.Length);
            foreach (var node in m_DirectoryInfo)
            {
                var file = new StreamFile();
                fileList.Add(file);
                file.path = node.path;
                file.fileName = Path.GetFileName(node.path);
                try
                {
                    file.stream = new OffsetStream(inputStream, node.offset + blocksOffset, node.size);
                }
                catch (IOException e)
                {
                    Debug.Log($"[AssetStudio:BundleFile] Failed to access {file.fileName} file.\n{e}");
                }
            }
        }

        private void ReadHeader(FileReader reader)
        {
            m_Header.size = reader.ReadInt64();
            m_Header.compressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.uncompressedBlocksInfoSize = reader.ReadUInt32();
            m_Header.flags = (ArchiveFlags)reader.ReadUInt32();
            if (m_Header.signature != "UnityFS")
            {
                reader.ReadByte();
            }
        }

        private void ReadBlocksInfoAndDirectory(FileReader reader, UnityVersion unityVer, bool silent = false)
        {
            byte[] blocksInfoBytes;

            if (m_Header.version >= 7)
            {
                reader.AlignStream(16);
            }
            else if (unityVer >= (2019, 4) && m_Header.flags != ArchiveFlags.BlocksAndDirectoryInfoCombined)
            {
                var preAlign = reader.Position;
                var alignData = reader.ReadBytes((16 - (int)(preAlign % 16)) % 16);
                if (alignData.Any(x => x != 0))
                {
                    reader.Position = preAlign;
                }
            }

            var compressedSize = (int)m_Header.compressedBlocksInfoSize;
            var uncompressedSize = (int)m_Header.uncompressedBlocksInfoSize;
            if (uncompressedSize < 0 || compressedSize < 0 || compressedSize > reader.BaseStream.Length)
            {
                throw new IOException("[AssetStudio:BundleFile] Incorrect blockInfo length.\nBlockInfo sizes might be encrypted.\n");
            }

            if ((m_Header.flags & ArchiveFlags.BlocksInfoAtTheEnd) != 0)
            {
                var position = reader.Position;
                reader.Position = m_Header.size - compressedSize;
                blocksInfoBytes = reader.ReadBytes(compressedSize);
                reader.Position = position;
            }
            else //0x40 BlocksAndDirectoryInfoCombined
            {
                blocksInfoBytes = reader.ReadBytes(compressedSize);
            }
            
            var compressionType = (CompressionType)(m_Header.flags & ArchiveFlags.CompressionTypeMask);

            if (!silent && compressionType > CompressionType.Lzham && Enum.IsDefined(typeof(CompressionType), compressionType))
            {
                Debug.Log($"[AssetStudio:BundleFile] Non-standard blockInfo compression type: {(int)compressionType}. Trying to decompress as {compressionType} archive..");
            }
            Debug.Log($"[AssetStudio:BundleFile] BlockInfo compression: {compressionType}");

            int numWrite;
            var errorMsg = string.Empty;
            MemoryStream blocksInfoUncompressedStream;
            switch (compressionType)
            {
                case CompressionType.None:
                {
                    blocksInfoUncompressedStream = new MemoryStream(blocksInfoBytes);
                    numWrite = compressedSize;
                    break;
                }
                case CompressionType.Lzma:
                {
                    blocksInfoUncompressedStream = new MemoryStream(uncompressedSize);
                    using (var blocksInfoCompressedStream = new MemoryStream(blocksInfoBytes))
                    {
                        numWrite = (int)BundleDecompressionHelper.DecompressLzmaStream(blocksInfoCompressedStream, blocksInfoUncompressedStream, compressedSize, uncompressedSize, ref errorMsg);
                    }
                    blocksInfoUncompressedStream.Position = 0;
                    break;
                }
                case CompressionType.Lz4:
                case CompressionType.Lz4HC:
                case CompressionType.Zstd:
                case CompressionType.Oodle:
                {
                    var uncompressedBytes = new byte[uncompressedSize];
                    numWrite = BundleDecompressionHelper.DecompressBlock(compressionType, blocksInfoBytes, uncompressedBytes, ref errorMsg);
                    blocksInfoUncompressedStream = new MemoryStream(uncompressedBytes);
                    break;
                }
                case CompressionType.Lzham:
                    throw new IOException($"[AssetStudio:BundleFile] Unsupported blockInfo compression type: {compressionType}.\n");
                default:
                    throw new IOException($"[AssetStudio:BundleFile] Unknown blockInfo compression type: {compressionType}.\nYou may try to specify the compression type manually.\n");
            }

            if (numWrite != uncompressedSize)
            {
                var msg = $"[AssetStudio:BundleFile] {compressionType} blockInfo decompression error. {errorMsg}\nWrite {numWrite} bytes but expected {uncompressedSize} bytes.";
                var exMsg = compressionType > CompressionType.Lz4HC
                    ? "Wrong compression type or blockInfo data might be encrypted."
                    : "BlockInfo data might be encrypted.";
                throw new IOException($"{msg}\n{exMsg}\n");
            }

            using (var blocksInfoReader = new EndianBinaryReader(blocksInfoUncompressedStream))
            {
                var uncompressedDataHash = blocksInfoReader.ReadBytes(16);
                var blocksInfoCount = blocksInfoReader.ReadInt32();
                m_BlocksInfo = new StorageBlock[blocksInfoCount];
                for (var i = 0; i < blocksInfoCount; i++)
                {
                    m_BlocksInfo[i] = new StorageBlock
                    {
                        uncompressedSize = blocksInfoReader.ReadUInt32(),
                        compressedSize = blocksInfoReader.ReadUInt32(),
                        flags = (StorageBlockFlags)blocksInfoReader.ReadUInt16()
                    };
                }

                var nodesCount = blocksInfoReader.ReadInt32();
                m_DirectoryInfo = new Node[nodesCount];
                for (var i = 0; i < nodesCount; i++)
                {
                    m_DirectoryInfo[i] = new Node
                    {
                        offset = blocksInfoReader.ReadInt64(),
                        size = blocksInfoReader.ReadInt64(),
                        flags = blocksInfoReader.ReadUInt32(),
                        path = ReadStringToNull(blocksInfoReader),
                    };
                }
            }
            if ((m_Header.flags & ArchiveFlags.BlockInfoNeedPaddingAtStart) != 0)
            {
                reader.AlignStream(16);
            }
        }

        private Stream ReadBlocks(FileReader reader)
        {
            var blocksStream = new MemoryStream();
            var blocksCompression = m_BlocksInfo.Max(x => (CompressionType)(x.flags & StorageBlockFlags.CompressionTypeMask));
            var blockSize = (int)m_BlocksInfo.Max(x => x.uncompressedSize);


            if (blocksCompression > CompressionType.Lzham && Enum.IsDefined(typeof(CompressionType), blocksCompression))
            {
                AssetStudioLogger.Warn($"[AssetStudio:BundleFile] Non-standard block compression type: {(int)blocksCompression}. Trying to decompress as {blocksCompression} archive..");
            }

            byte[] sharedCompressedBuff = null;
            byte[] sharedUncompressedBuff = null;
            if (blocksCompression > CompressionType.Lzma && blocksCompression != CompressionType.Lzham)
            {
                sharedCompressedBuff = BigArrayPool<byte>.Shared.Rent(blockSize);
                sharedUncompressedBuff = BigArrayPool<byte>.Shared.Rent(blockSize);
            }

            try
            {
                for (var i = 0; i < m_BlocksInfo.Length; i++)
                {
                    var blockInfo = m_BlocksInfo[i];
                    var compressionType = (CompressionType)(blockInfo.flags & StorageBlockFlags.CompressionTypeMask);

                    var debugMsg = $"[AssetStudio:BundleFile] [{i:D2}] Compression: {compressionType} | UncompressedSize: {blockInfo.uncompressedSize} | CompressedSize: {blockInfo.compressedSize} ";

                    long numWrite;
                    string errorMsg = null;
                    switch (compressionType)
                    {
                        case CompressionType.None:
                            var buffer = new byte[8192];
                            long remaining = blockInfo.compressedSize;
                            while (remaining > 0)
                            {
                                int readr = reader.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                                if (readr == 0) break;
                                blocksStream.Write(buffer, 0, readr);
                                remaining -= readr;
                            }
                            numWrite = blockInfo.compressedSize;
                            break;
                        case CompressionType.Lzma:
                            numWrite = BundleDecompressionHelper.DecompressLzmaStream(reader.BaseStream, blocksStream, blockInfo.compressedSize, blockInfo.uncompressedSize, ref errorMsg);
                            break;
                        case CompressionType.Lz4:
                        case CompressionType.Lz4HC:
                        case CompressionType.Zstd:
                        case CompressionType.Oodle:
                            var compressedSize = (int)blockInfo.compressedSize;
                            var uncompressedSize = (int)blockInfo.uncompressedSize;

                            if (sharedCompressedBuff != null)
                                Array.Clear(sharedCompressedBuff, 0, sharedCompressedBuff.Length);
                            if (sharedUncompressedBuff != null)
                                Array.Clear(sharedUncompressedBuff, 0, sharedUncompressedBuff.Length);

                            var read = reader.Read(sharedCompressedBuff, 0, compressedSize);
                            debugMsg += $"(read: {read.ToString()})";

                            numWrite = BundleDecompressionHelper.DecompressBlock(
                                compressionType,
                                sharedCompressedBuff,
                                sharedUncompressedBuff,
                                ref errorMsg);

                            if (errorMsg == null)
                            {
                                blocksStream.Write(sharedUncompressedBuff, 0, uncompressedSize);
                            }
                            break;
                        case CompressionType.Lzham:
                            throw new IOException($"[AssetStudio:BundleFile] Unsupported block compression type: {compressionType}.\n");
                        default:
                            throw new IOException($"[AssetStudio:BundleFile] Unknown block compression type: {compressionType}.\nYou may try to specify the compression type manually.\n");
                    }
                    AssetStudioLogger.Log(debugMsg, true);

                    if (errorMsg != null)
                    {
                        var msg = $"[AssetStudio:BundleFile] {compressionType} block decompression error. {errorMsg}\nWrite {numWrite} bytes but expected {blockInfo.uncompressedSize} bytes.";
                        var exMsg = compressionType > CompressionType.Lz4HC
                            ? "Wrong compression type or block data might be encrypted."
                            : "Block data might be encrypted.";
                        throw new IOException($"{msg}\n{exMsg}\n");
                    }
                }
            }
            finally
            {
                if (sharedCompressedBuff != null)
                    BigArrayPool<byte>.Shared.Return(sharedCompressedBuff, true); // clearArray: true

                if (sharedUncompressedBuff != null)
                    BigArrayPool<byte>.Shared.Return(sharedUncompressedBuff, true); // clearArray: true
            }

            return blocksStream;
        }

        private void UnityCnCheck(FileReader reader, UnityVersion unityVer)
        {
            if ((m_Header.flags & ArchiveFlags.BlocksInfoAtTheEnd) != 0)
                return;

            var hasUnityCnFlag = false;
            if (!unityVer.IsStripped)
            {
                if (unityVer < 2020
                    || unityVer.IsInRange(2020, (2020, 3, 34))
                    || unityVer.IsInRange(2021, (2021, 3, 2))
                    || unityVer.IsInRange(2022, (2022, 1, 1)))
                {
                    hasUnityCnFlag = ((CnEncryptionFlags)m_Header.flags & CnEncryptionFlags.V1) != 0;
                }
                else
                {
                    hasUnityCnFlag = ((CnEncryptionFlags)m_Header.flags & CnEncryptionFlags.V2_V3) != 0;
                }
            }
            if (!hasUnityCnFlag)
                return;

            var pos = reader.Position;
            reader.Position += 70;
            try
            {
                ReadBlocksInfoAndDirectory(reader, unityVer, silent: true);
            }
            catch (Exception)
            {
                reader.Position = pos;
                return;
            }
            throw new NotSupportedException("[AssetStudio:BundleFile] Unsupported bundle file. UnityCN encryption was detected.");
        }

        private bool IsUncompressedBundle => m_BlocksInfo.All(x => (CompressionType)(x.flags & StorageBlockFlags.CompressionTypeMask) == CompressionType.None);
    }
}