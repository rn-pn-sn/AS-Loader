using AssetStudioCLI;
using System;
using System.Collections.Generic;

namespace AssetStudio
{
    internal static class AssetStudioExporter
    {
        public static byte[] ExportAsset(AssetStudio.Object asset, WorkMode workMode)
        {
            AssetStudioLogger.Log($"{workMode.ToString()}: {asset.type} : {asset.m_PathID}", true);
            byte[] exportedBytes = null;

            switch (workMode)
            {
                case WorkMode.ExportRaw:
                    exportedBytes = Exporter.ExportRawFile(asset);
                    break;

                case WorkMode.Dump:
                    exportedBytes = Exporter.ExportDumpFile(asset);
                    break;

                case WorkMode.Export:
                    exportedBytes = Exporter.ExportConvertFile(asset);
                    break;
            }

            return exportedBytes;
        }

        public static List<byte[]> ExportAssets(List<Object> assets, WorkMode workMode)
        {
            List<byte[]> exported = new List<byte[]>();
            var toExportCount = assets.Count;
            var exportedCount = 0;

            foreach (var asset in assets)
            {
                AssetStudioLogger.Log($"[AssetStudioExporter] {workMode.ToString()}: {asset.type} ({asset.m_PathID})", true);
                byte[] exportedBytes = null;

                try
                {
                    switch (workMode)
                    {
                        case WorkMode.ExportRaw:
                            exportedBytes = Exporter.ExportRawFile(asset);
                            break;

                        case WorkMode.Dump:
                            exportedBytes = Exporter.ExportDumpFile(asset);
                            break;

                        case WorkMode.Export:
                            exportedBytes = Exporter.ExportConvertFile(asset);
                            break;
                    }

                    if (exportedBytes != null)
                    {
                        exported.Add(exportedBytes);
                        exportedCount++;
                    } else
                    {
                        AssetStudioLogger.Warn($"[AssetStudioExporter] {asset.type} : Export null\n           Try RAW/DUMP workers");
                    }
                }
                catch (Exception ex)
                {
                    AssetStudioLogger.Error($"[AssetStudioExporter] {asset.type} : Export error\n{ex}");
                }
            }

            AssetStudioLogger.Log($"[AssetStudioExporter] Exported assets: {exportedCount}/{toExportCount}\r", true);

            if (toExportCount > exportedCount)
            {
                AssetStudioLogger.Log($"[AssetStudioExporter] {toExportCount - exportedCount} asset(s) skipped (not extractable).", true);
            }
            else
            {
                AssetStudioLogger.Log($"[AssetStudioExporter] Export finished complete!", true);
            }

            return exported;
        }
    }
}