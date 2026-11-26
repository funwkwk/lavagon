using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Lavagon.Core
{
    /// <summary>
    /// Tiny reader for Windows PE files (exe/dll): CPU architecture and imported DLL names.
    /// Used to check that PHP matches Apache (x64 vs x86) and to preload PHP's own DLLs in
    /// dependency order.
    /// </summary>
    public static class PeFile
    {
        public const ushort X86 = 0x014C;
        public const ushort X64 = 0x8664;
        public const ushort Arm64 = 0xAA64;

        public static ushort Machine(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                using (var r = new BinaryReader(fs))
                {
                    fs.Position = 0x3C;
                    fs.Position = r.ReadInt32();
                    if (r.ReadUInt32() != 0x00004550) return 0; // "PE\0\0"
                    return r.ReadUInt16();
                }
            }
            catch
            {
                return 0;
            }
        }

        public static string MachineName(ushort machine)
        {
            switch (machine)
            {
                case X86: return "x86 (32-bit)";
                case X64: return "x64 (64-bit)";
                case Arm64: return "ARM64";
                default: return "unknown";
            }
        }

        /// <summary>Names of DLLs imported (normal + delay-load), lower case.</summary>
        public static List<string> Imports(string path)
        {
            var result = new List<string>();
            try
            {
                var data = File.ReadAllBytes(path);
                int pe = BitConverter.ToInt32(data, 0x3C);
                if (BitConverter.ToUInt32(data, pe) != 0x00004550) return result;
                int fileHeader = pe + 4;
                int sections = BitConverter.ToUInt16(data, fileHeader + 2);
                int optSize = BitConverter.ToUInt16(data, fileHeader + 16);
                int opt = fileHeader + 20;
                bool pe32Plus = BitConverter.ToUInt16(data, opt) == 0x20B;
                int dirs = opt + (pe32Plus ? 112 : 96);
                int sectionTable = opt + optSize;

                int RvaToOffset(uint rva)
                {
                    for (int i = 0; i < sections; i++)
                    {
                        int s = sectionTable + i * 40;
                        uint va = BitConverter.ToUInt32(data, s + 12);
                        uint size = Math.Max(BitConverter.ToUInt32(data, s + 8), BitConverter.ToUInt32(data, s + 16));
                        uint raw = BitConverter.ToUInt32(data, s + 20);
                        if (rva >= va && rva < va + size) return (int)(rva - va + raw);
                    }
                    return -1;
                }

                string ReadName(uint rva)
                {
                    int off = RvaToOffset(rva);
                    if (off < 0) return null;
                    int end = off;
                    while (end < data.Length && data[end] != 0) end++;
                    return Encoding.ASCII.GetString(data, off, end - off).ToLowerInvariant();
                }

                // Directory 1 = imports (20-byte descriptors, name RVA at +12)
                // Directory 13 = delay imports (32-byte descriptors, name RVA at +4)
                foreach (var (index, stride, nameOffset) in new[] { (1, 20, 12), (13, 32, 4) })
                {
                    uint dirRva = BitConverter.ToUInt32(data, dirs + index * 8);
                    if (dirRva == 0) continue;
                    int off = RvaToOffset(dirRva);
                    if (off < 0) continue;
                    for (int d = off; d + stride <= data.Length; d += stride)
                    {
                        uint nameRva = BitConverter.ToUInt32(data, d + nameOffset);
                        if (nameRva == 0) break;
                        var name = ReadName(nameRva);
                        if (!string.IsNullOrEmpty(name)) result.Add(name);
                    }
                }
            }
            catch
            {
                // not a PE file / truncated
            }
            return result.Distinct().ToList();
        }

        /// <summary>
        /// Orders the DLLs of one folder so that every DLL comes after the folder-local DLLs it
        /// imports (dependencies first).
        /// </summary>
        public static List<string> DependencyOrder(IEnumerable<string> dllPaths)
        {
            var byName = dllPaths.ToDictionary(p => Path.GetFileName(p).ToLowerInvariant(), p => p);
            var deps = byName.ToDictionary(kv => kv.Key, kv => Imports(kv.Value).Where(byName.ContainsKey).ToList());
            var ordered = new List<string>();
            var state = new Dictionary<string, int>(); // 1 = visiting, 2 = done

            void Visit(string n)
            {
                if (state.TryGetValue(n, out int s) && s > 0) return; // done, or a cycle
                state[n] = 1;
                foreach (var d in deps[n]) Visit(d);
                state[n] = 2;
                ordered.Add(byName[n]);
            }

            foreach (var n in byName.Keys.OrderBy(k => k)) Visit(n);
            return ordered;
        }
    }
}
