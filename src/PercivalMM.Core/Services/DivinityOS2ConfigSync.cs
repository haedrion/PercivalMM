using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;
using System.Xml.Linq;
using PercivalMM.Core.Models;

namespace PercivalMM.Core.Services
{
    public static class DivinityOS2ConfigSync
    {
        private record Dos2ModMeta(string Uuid, string Name, string Folder, string Version, string Md5);

        private static readonly Dos2ModMeta BaseGustavModule = new(
            Uuid: "28ac9ce2-2aba-8cda-b3b5-6e922f71b6b8",
            Name: "Divinity: Original Sin 2",
            Folder: "Gustav",
            Version: "36028797018963968",
            Md5: ""
        );

        public static string SyncLoadOrder(string gameInstallPath, IEnumerable<InstalledModItem> allItems)
        {
            var gameDef = GameRegistryService.GetByAppId("435150");
            string docsRoot = gameDef?.ResolveDeploymentRoot(gameInstallPath) ?? gameInstallPath;
            string larianDir = Path.Combine(docsRoot, "Larian Studios", "Divinity Original Sin 2 Definitive Edition");
            string profilesDir = Path.Combine(larianDir, "PlayerProfiles");

            if (!Directory.Exists(profilesDir))
                return "Proton PlayerProfiles folder not found yet (launch DOS2 once to initialize your profile).";

            var profileFolders = Directory.GetDirectories(profilesDir);
            if (profileFolders.Length == 0)
                return "No DOS2 PlayerProfiles discovered inside Proton prefix.";

            var activeContainers = allItems
                .Where(m => m.IsEnabled && !m.IsSeparator && !string.IsNullOrEmpty(m.StagingPath) && Directory.Exists(m.StagingPath))
                .OrderBy(m => m.Priority)
                .ToList();

            var orderedModules = new List<Dos2ModMeta> { BaseGustavModule };

            foreach (var container in activeContainers)
            {
                var paks = Directory.GetFiles(container.StagingPath!, "*.pak", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

                foreach (var pakPath in paks)
                {
                    var meta = TryReadPakMetadata(pakPath);
                    if (meta != null && !orderedModules.Any(x => x.Uuid.Equals(meta.Uuid, StringComparison.OrdinalIgnoreCase)))
                    {
                        orderedModules.Add(meta);
                    }
                }
            }

            int updatedProfiles = 0;
            foreach (var profileDir in profileFolders)
            {
                string modSettingsPath = Path.Combine(profileDir, "modsettings.lsx");
                WriteModSettingsLsx(modSettingsPath, orderedModules);
                updatedProfiles++;
            }

            int modCount = orderedModules.Count - 1;
            return $"Synced {modCount} active .pak module(s) into modsettings.lsx across {updatedProfiles} DOS2 profile(s).";
        }
        private static Dos2ModMeta? TryReadPakMetadata(string pakPath)
        {
            try
            {
                using var fs = File.OpenRead(pakPath);
                using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

                if (fs.Length < 40) return null;

                // Check LSPK v13 footer (last 8 bytes: FileListSize [4] + "LSPK")
                fs.Seek(-8, SeekOrigin.End);
                uint footerListSize = br.ReadUInt32();
                uint footerMagic = br.ReadUInt32();

                uint version;
                long fileListOffset;

                if (footerMagic == 0x4B50534C) // "LSPK" at EOF (DOS2:DE v13)
                {
                    fs.Seek(-(8 + 28), SeekOrigin.End);
                    version = br.ReadUInt32();
                    fileListOffset = br.ReadUInt32();
                    uint fileListSize = br.ReadUInt32();
                }
                else
                {
                    // Header at offset 0 (LSPK v15+)
                    fs.Seek(0, SeekOrigin.Begin);
                    uint headerMagic = br.ReadUInt32();
                    if (headerMagic != 0x4B50534C) return null;
                    version = br.ReadUInt32();
                    fileListOffset = br.ReadInt64();
                }

                fs.Seek(fileListOffset, SeekOrigin.Begin);
                int numFiles = br.ReadInt32();
                int compressedListSize = br.ReadInt32();
                byte[] compressedList = br.ReadBytes(compressedListSize);

                const int entryStride = 296; // 256-byte name + 40 bytes metadata in LSPK v13
                byte[] rawTable = DecompressLz4Block(compressedList, numFiles * entryStride);

                for (int i = 0; i < numFiles; i++)
                {
                    int baseOff = i * entryStride;
                    int nameLen = 0;
                    while (nameLen < 256 && rawTable[baseOff + nameLen] != 0) nameLen++;
                    string entryName = Encoding.UTF8.GetString(rawTable, baseOff, nameLen);

                    if (!entryName.EndsWith("meta.lsx", StringComparison.OrdinalIgnoreCase))
                        continue;

                    uint offsetInFile = BitConverter.ToUInt32(rawTable, baseOff + 256);
                    uint sizeOnDisk = BitConverter.ToUInt32(rawTable, baseOff + 260);
                    uint uncompressedSize = BitConverter.ToUInt32(rawTable, baseOff + 264);
                    uint archivePart = BitConverter.ToUInt32(rawTable, baseOff + 268);
                    uint flags = BitConverter.ToUInt32(rawTable, baseOff + 272);

                    if (archivePart != 0) continue;

                    fs.Seek(offsetInFile, SeekOrigin.Begin);
                    byte[] payload = br.ReadBytes((int)sizeOnDisk);

                    byte compressionMethod = (byte)(flags & 0x0F);
                    byte[] xmlBytes = compressionMethod switch
                    {
                        0 => payload,
                        1 => DecompressZlib(payload),
                        2 => DecompressLz4Block(payload, (int)uncompressedSize),
                        _ => payload
                    };

                    string xmlText = Encoding.UTF8.GetString(xmlBytes).TrimStart('\uFEFF', '\0');
                    var parsed = ParseMetaLsx(xmlText, Path.GetFileNameWithoutExtension(pakPath));
                    if (parsed != null) return parsed;
                }
            }
            catch
            {
                // Fallback below if .pak uses non-standard compression
            }

            return null;
        }
        private static Dos2ModMeta? ParseMetaLsx(string xmlText, string fallbackName)
        {
            var doc = XDocument.Parse(xmlText);
            var moduleInfo = doc.Descendants("node")
                .FirstOrDefault(n => (string?)n.Attribute("id") == "ModuleInfo");

            if (moduleInfo == null) return null;

            string GetAttr(string id, string def = "") =>
                moduleInfo.Elements("attribute")
                    .FirstOrDefault(a => (string?)a.Attribute("id") == id)
                    ?.Attribute("value")?.Value ?? def;

            string uuid = GetAttr("UUID");
            if (string.IsNullOrWhiteSpace(uuid)) return null;

            string name = GetAttr("Name", fallbackName);
            string folder = GetAttr("Folder", fallbackName);
            string version = GetAttr("Version", "268435456");
            string md5 = GetAttr("MD5", "");

            return new Dos2ModMeta(uuid, name, folder, version, md5);
        }

        private static void WriteModSettingsLsx(string modSettingsPath, List<Dos2ModMeta> modules)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" ?>");
            sb.AppendLine("<save>");
            sb.AppendLine("    <header version=\"2\" />");
            sb.AppendLine("    <version major=\"3\" minor=\"6\" revision=\"9\" build=\"0\" />");
            sb.AppendLine("    <region id=\"ModuleSettings\">");
            sb.AppendLine("        <node id=\"root\">");
            sb.AppendLine("            <children>");
            sb.AppendLine("                <node id=\"ModOrder\">");
            sb.AppendLine("                    <children>");

            foreach (var mod in modules)
            {
                sb.AppendLine("                        <node id=\"Module\">");
                sb.AppendLine($"                            <attribute id=\"UUID\" value=\"{SecurityElement.Escape(mod.Uuid)}\" type=\"FixedString\" />");
                sb.AppendLine("                        </node>");
            }

            sb.AppendLine("                    </children>");
            sb.AppendLine("                </node>");
            sb.AppendLine("                <node id=\"Mods\">");
            sb.AppendLine("                    <children>");

            foreach (var mod in modules)
            {
                sb.AppendLine("                        <node id=\"ModuleShortDesc\">");
                sb.AppendLine($"                            <attribute id=\"Folder\" value=\"{SecurityElement.Escape(mod.Folder)}\" type=\"LSWString\" />");
                sb.AppendLine($"                            <attribute id=\"MD5\" value=\"{SecurityElement.Escape(mod.Md5)}\" type=\"LSString\" />");
                sb.AppendLine($"                            <attribute id=\"Name\" value=\"{SecurityElement.Escape(mod.Name)}\" type=\"FixedString\" />");
                sb.AppendLine($"                            <attribute id=\"UUID\" value=\"{SecurityElement.Escape(mod.Uuid)}\" type=\"FixedString\" />");
                sb.AppendLine($"                            <attribute id=\"Version\" value=\"{SecurityElement.Escape(mod.Version)}\" type=\"int32\" />");
                sb.AppendLine("                        </node>");
            }

            sb.AppendLine("                    </children>");
            sb.AppendLine("                </node>");
            sb.AppendLine("            </children>");
            sb.AppendLine("        </node>");
            sb.AppendLine("    </region>");
            sb.AppendLine("</save>");

            File.WriteAllText(modSettingsPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private static byte[] DecompressZlib(byte[] compressed)
        {
            using var input = new MemoryStream(compressed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecompressLz4Block(byte[] src, int uncompressedSize)
        {
            byte[] dst = new byte[uncompressedSize];
            int s = 0, d = 0;

            while (s < src.Length && d < dst.Length)
            {
                int token = src[s++];
                int litLen = token >> 4;
                if (litLen == 15)
                {
                    int add;
                    do { add = src[s++]; litLen += add; } while (add == 255);
                }

                Buffer.BlockCopy(src, s, dst, d, litLen);
                s += litLen;
                d += litLen;
                if (d >= dst.Length || s >= src.Length) break;

                int offset = src[s] | (src[s + 1] << 8);
                s += 2;

                int matchLen = (token & 0x0F) + 4;
                if (matchLen == 19)
                {
                    int add;
                    do { add = src[s++]; matchLen += add; } while (add == 255);
                }

                int matchPos = d - offset;
                for (int i = 0; i < matchLen && d < dst.Length; i++)
                {
                    dst[d++] = dst[matchPos++];
                }
            }

            return dst;
        }
    }
}
