using System;
using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace AssetStudio
{
    public class Texture2DConverter
    {
        private ResourceReader reader;
        private int m_Width;
        private int m_Height;
        private int m_WidthCrop;
        private int m_HeightCrop;
        private TextureFormat m_TextureFormat;
        private byte[] m_PlatformBlob;
        private UnityVersion version;
        private BuildTarget platform;
        private int outPutDataSize;

        private bool switchSwizzled;
        private int gobsPerBlock;
        private Size blockSize;

        public int OutputDataSize => outPutDataSize;
        public bool UsesSwitchSwizzle => switchSwizzled;

        public Texture2DConverter(Texture2D m_Texture2D)
        {
            reader = m_Texture2D.image_data;
            m_WidthCrop = m_Texture2D.m_Width;
            m_HeightCrop = m_Texture2D.m_Height;
            m_TextureFormat = m_Texture2D.m_TextureFormat;
            m_PlatformBlob = m_Texture2D.m_PlatformBlob;
            version = m_Texture2D.version;
            platform = m_Texture2D.platform;
            blockSize = new Size(m_WidthCrop, m_HeightCrop);

            switchSwizzled = platform == BuildTarget.Switch && m_PlatformBlob.Length != 0;
            if (switchSwizzled)
            {
                gobsPerBlock = 1 << BitConverter.ToInt32(m_PlatformBlob, 8);
                if (m_TextureFormat == TextureFormat.RGB24) m_TextureFormat = TextureFormat.RGBA32;
                else if (m_TextureFormat == TextureFormat.BGR24) m_TextureFormat = TextureFormat.BGRA32;
            }
            else
            {
                m_Width = m_WidthCrop;
                m_Height = m_HeightCrop;
            }
            outPutDataSize = m_Width * m_Height * 4;
        }

        public static UnityEngine.Texture2D ConvertTexture2DToUnityTexture(AssetStudio.Texture2D m_Texture2D)
        {
            var converter = new Texture2DConverter(m_Texture2D);
            var outputBuffer = BigArrayPool<byte>.Shared.Rent(converter.OutputDataSize);

            var unityFormat = TextureFormatConvert(m_Texture2D.m_TextureFormat);
            if (unityFormat == null) throw new NotImplementedException();
            var format = unityFormat ?? UnityEngine.TextureFormat.RGBA32;

            try
            {
                if (!converter.DecodeTexture2D(outputBuffer)) return null;

                int width = m_Texture2D.m_Width;
                int height = m_Texture2D.m_Height;

                if (converter.UsesSwitchSwizzle)
                {
                    var uncroppedSize = new Size(width, height);
                    width = uncroppedSize.Width;
                    height = uncroppedSize.Height;
                }

                UnityEngine.Texture2D texture = CreateUnityTexture2d(converter, width, height, outputBuffer, format);

                if (converter.UsesSwitchSwizzle && (width != m_Texture2D.m_Width || height != m_Texture2D.m_Height))
                {
                    var croppedTexture = new UnityEngine.Texture2D(m_Texture2D.m_Width, m_Texture2D.m_Height, format, false);

                    var pixelsRect = new Rect(0, 0, m_Texture2D.m_Width, m_Texture2D.m_Height);
                    var croppedPixels = texture.GetPixels((int)pixelsRect.x, (int)pixelsRect.y, (int)pixelsRect.width, (int)pixelsRect.height);

                    croppedTexture.SetPixels(croppedPixels);
                    croppedTexture.Apply();

                    UnityEngine.Object.Destroy(texture);
                    texture = croppedTexture;
                }

                return texture;
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(outputBuffer, clearArray: true);
            }
        }

        private static UnityEngine.Texture2D CreateUnityTexture2d(Texture2DConverter converter, int width, int height, byte[] outputBuffer, UnityEngine.TextureFormat format)
        {
            UnityEngine.Texture2D texture;

            switch (format)
            {
                case UnityEngine.TextureFormat.RGBA32:
                    texture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
                    int byteCount = converter.OutputDataSize;
                    ConvertBgraToRgbaInPlace(outputBuffer.AsSpan(0, byteCount));
                    texture.LoadRawTextureData(outputBuffer);
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    break;
                default:
                    texture = new UnityEngine.Texture2D(width, height, format, false);
                    texture.LoadRawTextureData(outputBuffer);
                    texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                    break;
            }
            return texture;
        }

        private static UnityEngine.Texture2D FlipTextureVertically(UnityEngine.Texture2D original)
        {
            int width = original.width;
            int height = original.height;

            var originalPixels = original.GetPixels32();
            var flippedPixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    flippedPixels[(height - 1 - y) * width + x] = originalPixels[y * width + x];
                }
            }

            var flippedTexture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
            flippedTexture.SetPixels32(flippedPixels);
            flippedTexture.Apply();

            UnityEngine.Object.Destroy(original);
            return flippedTexture;
        }

        private static void ConvertBgraToRgbaInPlace(Span<byte> data)
        {
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                byte b = data[i];
                data[i] = data[i + 2];
                data[i + 2] = b;
            }
        }

        public bool DecodeTexture2D(byte[] bytes)
        {
            if (reader.Size == 0 || m_Width == 0 || m_Height == 0)
            {
                return false;
            }
            var flag = false;
            var buff = BigArrayPool<byte>.Shared.Rent(reader.Size);
            try
            {
                _ = reader.GetData(buff);
                if (switchSwizzled)
                {
                    var unswizzledData = BigArrayPool<byte>.Shared.Rent(reader.Size);
                    try
                    {
                        Texture2DSwitchDeswizzler.Unswizzle(buff, blockSize, blockSize, gobsPerBlock, unswizzledData);
                        BigArrayPool<byte>.Shared.Return(buff, clearArray: true);
                        buff = unswizzledData;
                    }
                    catch (Exception e)
                    {
                        BigArrayPool<byte>.Shared.Return(unswizzledData, clearArray: true);
                        Debug.LogWarning(e.Message);
                    }
                }

                switch (m_TextureFormat)
                {
                    case TextureFormat.Alpha8: //test pass
                        flag = DecodeAlpha8(buff, bytes);
                        break;
                    case TextureFormat.ARGB4444: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeARGB4444(buff, bytes);
                        break;
                    case TextureFormat.RGB24: //test pass
                        flag = DecodeRGB24(buff, bytes);
                        break;
                    case TextureFormat.RGBA32: //test pass
                        flag = DecodeRGBA32(buff, bytes);
                        break;
                    case TextureFormat.ARGB32: //test pass
                        flag = DecodeARGB32(buff, bytes);
                        break;
                    case TextureFormat.RGB565: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeRGB565(buff, bytes);
                        break;
                    case TextureFormat.R16: //test pass
                        flag = DecodeR16(buff, bytes);
                        break;
                    case TextureFormat.DXT1: //test pass
                        SwapBytesForXbox(buff);
                        goto default;
                    case TextureFormat.DXT3:
                        break;
                    case TextureFormat.DXT5: //test pass
                        SwapBytesForXbox(buff);
                        goto default;
                    case TextureFormat.RGBA4444: //test pass
                        flag = DecodeRGBA4444(buff, bytes);
                        break;
                    case TextureFormat.BGRA32: //test pass
                        flag = DecodeBGRA32(buff, bytes);
                        break;
                    case TextureFormat.RHalf:
                        flag = DecodeRHalf(buff, bytes);
                        break;
                    case TextureFormat.RGHalf:
                        flag = DecodeRGHalf(buff, bytes);
                        break;
                    case TextureFormat.RGBAHalf: //test pass
                        flag = DecodeRGBAHalf(buff, bytes);
                        break;
                    case TextureFormat.RFloat:
                        flag = DecodeRFloat(buff, bytes);
                        break;
                    case TextureFormat.RGFloat:
                        flag = DecodeRGFloat(buff, bytes);
                        break;
                    case TextureFormat.RGBAFloat:
                        flag = DecodeRGBAFloat(buff, bytes);
                        break;
                    case TextureFormat.YUY2: //test pass
                        flag = DecodeYUY2(buff, bytes);
                        break;
                    case TextureFormat.RGB9e5Float: //test pass
                        flag = DecodeRGB9e5Float(buff, bytes);
                        break;
                    case TextureFormat.DXT1Crunched: //test pass
                        flag = DecodeDXT1Crunched(buff, bytes);
                        break;
                    case TextureFormat.DXT5Crunched: //test pass
                        flag = DecodeDXT5Crunched(buff, bytes);
                        break;
                    case TextureFormat.RG16: //test pass
                        flag = DecodeRG16(buff, bytes);
                        break;
                    case TextureFormat.R8: //test pass
                        flag = DecodeR8(buff, bytes);
                        break;
                    case TextureFormat.ETC_RGB4Crunched: //test pass
                        flag = DecodeETC1Crunched(buff, bytes);
                        break;
                    case TextureFormat.ETC2_RGBA8Crunched: //test pass
                        flag = DecodeETC2A8Crunched(buff, bytes);
                        break;
                    case TextureFormat.RG32: //test pass
                        flag = DecodeRG32(buff, bytes);
                        break;
                    case TextureFormat.RGB48: //test pass
                        flag = DecodeRGB48(buff, bytes);
                        break;
                    case TextureFormat.RGBA64: //test pass
                        flag = DecodeRGBA64(buff, bytes);
                        break;
                    default: // NotImplementedException => just return compressed data
                        if (bytes.Length >= reader.Size)
                        {
                            Buffer.BlockCopy(buff, 0, bytes, 0, reader.Size);
                            flag = true;
                        }
                        else
                        {
                            Debug.LogError("Output buffer too small");
                            flag = false;
                        }
                        break;
                }
            }
            finally
            {
                BigArrayPool<byte>.Shared.Return(buff, clearArray: true);
            }
            return flag;
        }

        public static UnityEngine.TextureFormat? TextureFormatConvert(AssetStudio.TextureFormat format)
        {
            return format switch
            {
                AssetStudio.TextureFormat.Alpha8 => UnityEngine.TextureFormat.Alpha8,
                AssetStudio.TextureFormat.ARGB4444 => UnityEngine.TextureFormat.ARGB4444,
                AssetStudio.TextureFormat.RGB24 => UnityEngine.TextureFormat.RGB24,
                AssetStudio.TextureFormat.RGBA32 => UnityEngine.TextureFormat.RGBA32,
                AssetStudio.TextureFormat.ARGB32 => UnityEngine.TextureFormat.ARGB32,
                AssetStudio.TextureFormat.RGB565 => UnityEngine.TextureFormat.RGB565,
                AssetStudio.TextureFormat.R16 => UnityEngine.TextureFormat.R16,
                AssetStudio.TextureFormat.DXT1 => UnityEngine.TextureFormat.DXT1,
                AssetStudio.TextureFormat.DXT5 => UnityEngine.TextureFormat.DXT5,
                AssetStudio.TextureFormat.RGBA4444 => UnityEngine.TextureFormat.RGBA4444,
                AssetStudio.TextureFormat.BGRA32 => UnityEngine.TextureFormat.BGRA32,
                AssetStudio.TextureFormat.RHalf => UnityEngine.TextureFormat.RHalf,
                AssetStudio.TextureFormat.RGHalf => UnityEngine.TextureFormat.RGHalf,
                AssetStudio.TextureFormat.RGBAHalf => UnityEngine.TextureFormat.RGBAHalf,
                AssetStudio.TextureFormat.RFloat => UnityEngine.TextureFormat.RFloat,
                AssetStudio.TextureFormat.RGFloat => UnityEngine.TextureFormat.RGFloat,
                AssetStudio.TextureFormat.RGBAFloat => UnityEngine.TextureFormat.RGBAFloat,
                AssetStudio.TextureFormat.YUY2 => UnityEngine.TextureFormat.YUY2,
                AssetStudio.TextureFormat.RGB9e5Float => UnityEngine.TextureFormat.RGB9e5Float,
                AssetStudio.TextureFormat.BC6H => UnityEngine.TextureFormat.BC6H,
                AssetStudio.TextureFormat.BC7 => UnityEngine.TextureFormat.BC7,
                AssetStudio.TextureFormat.BC4 => UnityEngine.TextureFormat.BC4,
                AssetStudio.TextureFormat.BC5 => UnityEngine.TextureFormat.BC5,
                AssetStudio.TextureFormat.DXT1Crunched => UnityEngine.TextureFormat.DXT1Crunched,
                AssetStudio.TextureFormat.DXT5Crunched => UnityEngine.TextureFormat.DXT5Crunched,
                AssetStudio.TextureFormat.ETC_RGB4 => UnityEngine.TextureFormat.ETC_RGB4,
                AssetStudio.TextureFormat.EAC_R => UnityEngine.TextureFormat.EAC_R,
                AssetStudio.TextureFormat.EAC_R_SIGNED => UnityEngine.TextureFormat.EAC_R_SIGNED,
                AssetStudio.TextureFormat.EAC_RG => UnityEngine.TextureFormat.EAC_RG,
                AssetStudio.TextureFormat.EAC_RG_SIGNED => UnityEngine.TextureFormat.EAC_RG_SIGNED,
                AssetStudio.TextureFormat.ETC2_RGB => UnityEngine.TextureFormat.ETC2_RGB,
                AssetStudio.TextureFormat.ETC2_RGBA1 => UnityEngine.TextureFormat.ETC2_RGBA1,
                AssetStudio.TextureFormat.ETC2_RGBA8 => UnityEngine.TextureFormat.ETC2_RGBA8,
                AssetStudio.TextureFormat.ASTC_RGB_4x4 => UnityEngine.TextureFormat.ASTC_4x4,
                AssetStudio.TextureFormat.ASTC_RGB_5x5 => UnityEngine.TextureFormat.ASTC_5x5,
                AssetStudio.TextureFormat.ASTC_RGB_6x6 => UnityEngine.TextureFormat.ASTC_6x6,
                AssetStudio.TextureFormat.ASTC_RGB_8x8 => UnityEngine.TextureFormat.ASTC_8x8,
                AssetStudio.TextureFormat.ASTC_RGB_10x10 => UnityEngine.TextureFormat.ASTC_10x10,
                AssetStudio.TextureFormat.ASTC_RGB_12x12 => UnityEngine.TextureFormat.ASTC_12x12,
                AssetStudio.TextureFormat.ASTC_RGBA_4x4 => UnityEngine.TextureFormat.ASTC_4x4,
                AssetStudio.TextureFormat.ASTC_RGBA_5x5 => UnityEngine.TextureFormat.ASTC_5x5,
                AssetStudio.TextureFormat.ASTC_RGBA_6x6 => UnityEngine.TextureFormat.ASTC_6x6,
                AssetStudio.TextureFormat.ASTC_RGBA_8x8 => UnityEngine.TextureFormat.ASTC_8x8,
                AssetStudio.TextureFormat.ASTC_RGBA_10x10 => UnityEngine.TextureFormat.ASTC_10x10,
                AssetStudio.TextureFormat.ASTC_RGBA_12x12 => UnityEngine.TextureFormat.ASTC_12x12,
                AssetStudio.TextureFormat.RG16 => UnityEngine.TextureFormat.RG16,
                AssetStudio.TextureFormat.R8 => UnityEngine.TextureFormat.R8,
                AssetStudio.TextureFormat.ETC_RGB4Crunched => UnityEngine.TextureFormat.ETC_RGB4Crunched,
                AssetStudio.TextureFormat.ETC2_RGBA8Crunched => UnityEngine.TextureFormat.ETC2_RGBA8Crunched,
                AssetStudio.TextureFormat.ASTC_HDR_4x4 => UnityEngine.TextureFormat.ASTC_HDR_4x4,
                AssetStudio.TextureFormat.ASTC_HDR_5x5 => UnityEngine.TextureFormat.ASTC_HDR_5x5,
                AssetStudio.TextureFormat.ASTC_HDR_6x6 => UnityEngine.TextureFormat.ASTC_HDR_6x6,
                AssetStudio.TextureFormat.ASTC_HDR_8x8 => UnityEngine.TextureFormat.ASTC_HDR_8x8,
                AssetStudio.TextureFormat.ASTC_HDR_10x10 => UnityEngine.TextureFormat.ASTC_HDR_10x10,
                AssetStudio.TextureFormat.ASTC_HDR_12x12 => UnityEngine.TextureFormat.ASTC_HDR_12x12,
                AssetStudio.TextureFormat.RG32 => UnityEngine.TextureFormat.RG32,
                AssetStudio.TextureFormat.RGB48 => UnityEngine.TextureFormat.RGB48,
                AssetStudio.TextureFormat.RGBA64 => UnityEngine.TextureFormat.RGBA64,
                _ => null
            };
        }

        private void SwapBytesForXbox(Span<byte> image_data)
        {
            if (platform == BuildTarget.XBOX360)
            {
                for (var i = 0; i < reader.Size / 2; i++)
                {
                    image_data.Slice(i * 2, 2).Reverse();
                }
            }
        }

        private bool DecodeAlpha8(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            buff.Fill(0xFF);
            for (var i = 0; i < size; i++)
            {
                buff[i * 4 + 3] = image_data[i];
            }
            return true;
        }

        private bool DecodeARGB4444(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            var pixelNew = new byte[4].AsSpan();
            for (var i = 0; i < size; i++)
            {
                var pixelOldShort = BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2));
                pixelNew[0] = (byte)(pixelOldShort & 0x000f);
                pixelNew[1] = (byte)((pixelOldShort & 0x00f0) >> 4);
                pixelNew[2] = (byte)((pixelOldShort & 0x0f00) >> 8);
                pixelNew[3] = (byte)((pixelOldShort & 0xf000) >> 12);
                for (var j = 0; j < 4; j++)
                    pixelNew[j] = (byte)((pixelNew[j] << 4) | pixelNew[j]);
                pixelNew.CopyTo(buff.Slice(i * 4));
            }
            return true;
        }

        private bool DecodeRGB24(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = image_data[i * 3 + 2];
                buff[i * 4 + 1] = image_data[i * 3 + 1];
                buff[i * 4 + 2] = image_data[i * 3 + 0];
                buff[i * 4 + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBA32(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = image_data[i + 2];
                buff[i + 1] = image_data[i + 1];
                buff[i + 2] = image_data[i + 0];
                buff[i + 3] = image_data[i + 3];
            }
            return true;
        }

        private bool DecodeARGB32(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = image_data[i + 3];
                buff[i + 1] = image_data[i + 2];
                buff[i + 2] = image_data[i + 1];
                buff[i + 3] = image_data[i + 0];
            }
            return true;
        }

        private bool DecodeRGB565(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                var p = BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2));
                buff[i * 4] = (byte)((p << 3) | (p >> 2 & 7));
                buff[i * 4 + 1] = (byte)((p >> 3 & 0xfc) | (p >> 9 & 3));
                buff[i * 4 + 2] = (byte)((p >> 8 & 0xf8) | (p >> 13));
                buff[i * 4 + 3] = 255;
            }
            return true;
        }

        private bool DecodeR16(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //b
                buff[i * 4 + 1] = 0; //g
                buff[i * 4 + 2] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2))); //r
                buff[i * 4 + 3] = 255; //a
            }
            return true;
        }

        private bool DecodeRGBA4444(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            var pixelNew = new byte[4].AsSpan();
            for (var i = 0; i < size; i++)
            {
                var pixelOldShort = BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2));
                pixelNew[0] = (byte)((pixelOldShort & 0x00f0) >> 4);
                pixelNew[1] = (byte)((pixelOldShort & 0x0f00) >> 8);
                pixelNew[2] = (byte)((pixelOldShort & 0xf000) >> 12);
                pixelNew[3] = (byte)(pixelOldShort & 0x000f);
                for (var j = 0; j < 4; j++)
                    pixelNew[j] = (byte)((pixelNew[j] << 4) | pixelNew[j]);
                pixelNew.CopyTo(buff.Slice(i * 4));
            }
            return true;
        }

        private bool DecodeBGRA32(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = image_data[i];
                buff[i + 1] = image_data[i + 1];
                buff[i + 2] = image_data[i + 2];
                buff[i + 3] = image_data[i + 3];
            }
            return true;
        }

        private bool DecodeRHalf(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = 0;

                // Чтение half из двух байтов и преобразование в float
                ushort halfValue = (ushort)(image_data[i / 2] | (image_data[i / 2 + 1] << 8));
                buff[i + 2] = (byte)math.round(math.f16tof32(halfValue) * 255f);

                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGHalf(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = 0;

                // Чтение G компонента
                ushort gHalf = (ushort)(image_data[i + 2] | (image_data[i + 3] << 8));
                buff[i + 1] = (byte)math.round(math.f16tof32(gHalf) * 255f);

                // Чтение R компонента
                ushort rHalf = (ushort)(image_data[i] | (image_data[i + 1] << 8));
                buff[i + 2] = (byte)math.round(math.f16tof32(rHalf) * 255f);

                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBAHalf(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                ushort bHalf = (ushort)(image_data[i * 2 + 4] | (image_data[i * 2 + 5] << 8));
                buff[i] = (byte)math.round(math.f16tof32(bHalf) * 255f);

                ushort gHalf = (ushort)(image_data[i * 2 + 2] | (image_data[i * 2 + 3] << 8));
                buff[i + 1] = (byte)math.round(math.f16tof32(gHalf) * 255f);

                ushort rHalf = (ushort)(image_data[i * 2] | (image_data[i * 2 + 1] << 8));
                buff[i + 2] = (byte)math.round(math.f16tof32(rHalf) * 255f);

                ushort aHalf = (ushort)(image_data[i * 2 + 6] | (image_data[i * 2 + 7] << 8));
                buff[i + 3] = (byte)math.round(math.f16tof32(aHalf) * 255f);
            }
            return true;
        }

        private bool DecodeRFloat(byte[] image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = 0;
                buff[i + 2] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGFloat(byte[] image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 2 + 4) * 255f);
                buff[i + 2] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 2) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBAFloat(byte[] image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 4 + 8) * 255f);
                buff[i + 1] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 4 + 4) * 255f);
                buff[i + 2] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 4) * 255f);
                buff[i + 3] = (byte)MathF.Round(BitConverter.ToSingle(image_data, i * 4 + 12) * 255f);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ClampByte(int x)
        {
            return (byte)(byte.MaxValue < x ? byte.MaxValue : (x > byte.MinValue ? x : byte.MinValue));
        }

        private bool DecodeYUY2(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            int p = 0;
            int o = 0;
            int halfWidth = m_Width / 2;
            for (int j = 0; j < m_Height; j++)
            {
                for (int i = 0; i < halfWidth; ++i)
                {
                    int y0 = image_data[p++];
                    int u0 = image_data[p++];
                    int y1 = image_data[p++];
                    int v0 = image_data[p++];
                    int c = y0 - 16;
                    int d = u0 - 128;
                    int e = v0 - 128;
                    buff[o++] = ClampByte((298 * c + 516 * d + 128) >> 8);            // b
                    buff[o++] = ClampByte((298 * c - 100 * d - 208 * e + 128) >> 8);  // g
                    buff[o++] = ClampByte((298 * c + 409 * e + 128) >> 8);            // r
                    buff[o++] = 255;
                    c = y1 - 16;
                    buff[o++] = ClampByte((298 * c + 516 * d + 128) >> 8);            // b
                    buff[o++] = ClampByte((298 * c - 100 * d - 208 * e + 128) >> 8);  // g
                    buff[o++] = ClampByte((298 * c + 409 * e + 128) >> 8);            // r
                    buff[o++] = 255;
                }
            }
            return true;
        }

        private bool DecodeRGB9e5Float(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                var n = BinaryPrimitives.ReadInt32LittleEndian(image_data.Slice(i));
                var scale = n >> 27 & 0x1f;
                var scalef = MathF.Pow(2, scale - 24);
                var b = n >> 18 & 0x1ff;
                var g = n >> 9 & 0x1ff;
                var r = n & 0x1ff;
                buff[i] = (byte)MathF.Round(b * scalef * 255f);
                buff[i + 1] = (byte)MathF.Round(g * scalef * 255f);
                buff[i + 2] = (byte)MathF.Round(r * scalef * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeDXT1Crunched(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                //NotImplementedException => just return compressed
                return true;
            }
            return false;
        }

        private bool DecodeDXT5Crunched(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                //NotImplementedException => just return compressed
                return true;
            }
            return false;
        }

        private bool DecodeRG16(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //B
                buff[i * 4 + 1] = image_data[i * 2 + 1];//G
                buff[i * 4 + 2] = image_data[i * 2];//R
                buff[i * 4 + 3] = 255;//A
            }
            return true;
        }

        private bool DecodeR8(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //B
                buff[i * 4 + 1] = 0; //G
                buff[i * 4 + 2] = image_data[i];//R
                buff[i * 4 + 3] = 255;//A
            }
            return true;
        }

        private bool DecodeETC1Crunched(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                //NotImplementedException => just return compressed
                return true;
            }
            return false;
        }

        private bool DecodeETC2A8Crunched(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                //NotImplementedException => just return compressed
                return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte DownScaleFrom16BitTo8Bit(ushort component)
        {
            return (byte)(((component * 255) + 32895) >> 16);
        }

        private bool DecodeRG32(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = 0;                                                                                                  //b
                buff[i + 1] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i + 2)));     //g
                buff[i + 2] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i)));         //r
                buff[i + 3] = byte.MaxValue;                                                                                  //a
            }
            return true;
        }

        private bool DecodeRGB48(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 6 + 4)));     //b
                buff[i * 4 + 1] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 6 + 2))); //g
                buff[i * 4 + 2] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 6)));     //r
                buff[i * 4 + 3] = byte.MaxValue;                                                                                  //a
            }
            return true;
        }

        private bool DecodeRGBA64(ReadOnlySpan<byte> image_data, Span<byte> buff)
        {
            for (var i = 0; i < outPutDataSize; i += 4)
            {
                buff[i] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2 + 4)));     //b
                buff[i + 1] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2 + 2))); //g
                buff[i + 2] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2)));     //r
                buff[i + 3] = DownScaleFrom16BitTo8Bit(BinaryPrimitives.ReadUInt16LittleEndian(image_data.Slice(i * 2 + 6))); //a
            }
            return true;
        }

        private bool UnpackCrunch(ReadOnlySpan<byte> image_data, out ReadOnlySpan<byte> result)
        {
            result = ReadOnlySpan<byte>.Empty;

            UnityEngine.TextureFormat texFormat;
            switch (m_TextureFormat)
            {
                case TextureFormat.ETC_RGB4Crunched:
                case TextureFormat.ETC_RGB4:
                    texFormat = UnityEngine.TextureFormat.ETC_RGB4;
                    break;
                case TextureFormat.ETC2_RGBA8Crunched:
                case TextureFormat.ETC2_RGBA8:
                    texFormat = UnityEngine.TextureFormat.ETC2_RGBA8;
                    break;
                default:
                    return false;
            }

            try
            {
                int width = m_Width;
                int height = m_Height;

                var tex = new UnityEngine.Texture2D(width, height, texFormat, false, false);

                byte[] raw = image_data.ToArray();
                tex.LoadRawTextureData(raw);
                tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                var rawPixels = tex.GetRawTextureData<byte>();
                result = new ReadOnlySpan<byte>(rawPixels.ToArray());
                UnityEngine.Object.Destroy(tex);
                return !result.IsEmpty;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
