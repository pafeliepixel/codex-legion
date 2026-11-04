using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace CodexNativeLite
{
    public sealed class LocalGpuInfo
    {
        public string Name { get; set; }
        public string Driver { get; set; }
        public long MemoryBytes { get; set; }
        public bool IsNvidia { get { return Contains(Name, "NVIDIA"); } }
        public bool IsAmd { get { return Contains(Name, "AMD") || Contains(Name, "Radeon"); } }

        private static bool Contains(string value, string text)
        {
            return !String.IsNullOrWhiteSpace(value) && value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class LocalHardwareProfile
    {
        public long TotalMemoryBytes { get; set; }
        public long AvailableMemoryBytes { get; set; }
        public long FreeDiskBytes { get; set; }
        public string Processor { get; set; }
        public List<LocalGpuInfo> Gpus { get; private set; }

        public LocalHardwareProfile() { Gpus = new List<LocalGpuInfo>(); }

        public double TotalMemoryGb { get { return ToGb(TotalMemoryBytes); } }
        public double FreeDiskGb { get { return ToGb(FreeDiskBytes); } }
        public double LargestVramGb
        {
            get
            {
                long largest = 0;
                foreach (LocalGpuInfo gpu in Gpus) if (gpu.MemoryBytes > largest) largest = gpu.MemoryBytes;
                return ToGb(largest);
            }
        }

        public bool HasNvidia
        {
            get { foreach (LocalGpuInfo gpu in Gpus) if (gpu.IsNvidia) return true; return false; }
        }

        public bool HasAmd
        {
            get { foreach (LocalGpuInfo gpu in Gpus) if (gpu.IsAmd) return true; return false; }
        }

        private static double ToGb(long value) { return value <= 0 ? 0 : value / 1073741824d; }
    }

    public sealed class LocalModelChoice
    {
        public string Id { get; set; }
        public string Family { get; set; }
        public string Model { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string LicenseName { get; set; }
        public double DownloadGb { get; set; }
        public double ComfortableRamGb { get; set; }
        public double MinimumRamGb { get; set; }
        public double MinimumVramGb { get; set; }
        public bool NvidiaOnly { get; set; }
        public bool DeepSeekHardware { get; set; }

        public string Rating(LocalHardwareProfile hardware)
        {
            if (hardware == null) return "Check needed";
            if (NvidiaOnly && !hardware.HasNvidia) return "Not supported";
            if (!hardware.HasNvidia && !hardware.HasAmd) return "Not supported";
            if (DeepSeekHardware && hardware.HasNvidia && hardware.LargestVramGb + 0.25 < MinimumVramGb) return "Not supported";
            if (hardware.FreeDiskGb > 0 && hardware.FreeDiskGb < DownloadGb + 10) return "Needs more disk";
            if (hardware.TotalMemoryGb + 0.25 >= ComfortableRamGb) return "Comfortable";
            if (hardware.TotalMemoryGb + 0.25 >= MinimumRamGb) return "Tight fit";
            return "Not recommended";
        }

        public string Recommendation(LocalHardwareProfile hardware)
        {
            string rating = Rating(hardware);
            return rating + " · about " + DownloadGb.ToString("0.#") + " GB download · " + Summary;
        }
    }

    public static class LocalModelCatalog
    {
        public static List<LocalModelChoice> Create()
        {
            return new List<LocalModelChoice>
            {
                new LocalModelChoice
                {
                    Id = "gyro-s", Family = "gyro", Model = "Gyro-S", Name = "Gyro-S · smallest",
                    Summary = "The lightest general model. Slower and less precise, but much easier to fit.",
                    LicenseName = "Gyro and Qwen model licenses", DownloadGb = 58.5, MinimumRamGb = 28, ComfortableRamGb = 36
                },
                new LocalModelChoice
                {
                    Id = "qwen36-q4", Family = "qwen36", Model = "UD-Q4_K_XL", Name = "Qwen 3.6 35B · balanced",
                    Summary = "A smaller, fast coding model and the friendliest starting point for a 32 GB PC.",
                    LicenseName = "Apache 2.0 and Unsloth model terms", DownloadGb = 24, MinimumRamGb = 32, ComfortableRamGb = 40
                },
                new LocalModelChoice
                {
                    Id = "coder-iq1", Family = "coder", Model = "IQ1_M", Name = "Qwen 3.8 Coder · coding first",
                    Summary = "Keeps the experts chosen for code and agent work. Best when coding matters more than general chat.",
                    LicenseName = "Qwen and ISTA-DASLab model terms", DownloadGb = 68, MinimumRamGb = 32, ComfortableRamGb = 40
                },
                new LocalModelChoice
                {
                    Id = "qwen38-q2", Family = "qwen", Model = "Q2_0", Name = "Qwen 3.8 Flash · quick",
                    Summary = "The fastest full-expert Qwen 3.8 pack.",
                    LicenseName = "Qwen and ISTA-DASLab model terms", DownloadGb = 66, MinimumRamGb = 38, ComfortableRamGb = 48
                },
                new LocalModelChoice
                {
                    Id = "qwen38-iq2", Family = "qwen", Model = "IQ2_XS", Name = "Qwen 3.8 Flash · recommended",
                    Summary = "A good quality and speed middle ground.",
                    LicenseName = "Qwen and ISTA-DASLab model terms", DownloadGb = 70, MinimumRamGb = 40, ComfortableRamGb = 48
                },
                new LocalModelChoice
                {
                    Id = "qwen38-iq3", Family = "qwen", Model = "IQ3_XXS", Name = "Qwen 3.8 Flash · quality",
                    Summary = "Higher quality, with a larger RAM footprint.",
                    LicenseName = "Qwen and ISTA-DASLab model terms", DownloadGb = 76, MinimumRamGb = 47, ComfortableRamGb = 56
                },
                new LocalModelChoice
                {
                    Id = "qwen38-iq3s", Family = "qwen", Model = "IQ3_S", Name = "Qwen 3.8 Flash · best pack",
                    Summary = "The strongest original Qwen 3.8 pack in Strata's published comparison.",
                    LicenseName = "Qwen and ISTA-DASLab model terms", DownloadGb = 82, MinimumRamGb = 55, ComfortableRamGb = 64
                },
                new LocalModelChoice
                {
                    Id = "deepseek-coder", Family = "deepseek41", Model = "ISTA-DAS-Coder", Name = "DeepSeek V4.1 Coder · huge",
                    Summary = "A very large coding model. NVMe storage strongly recommended.",
                    LicenseName = "MIT model weights and ISTA-DASLab terms", DownloadGb = 191, MinimumRamGb = 32, ComfortableRamGb = 64,
                    MinimumVramGb = 12, DeepSeekHardware = true
                },
                new LocalModelChoice
                {
                    Id = "deepseek-200", Family = "deepseek41", Model = "ist-das-2.00bpw", Name = "DeepSeek V4.1 Flash · enormous",
                    Summary = "The full 2.00 bpw set. Built for machines with lots of RAM and NVMe space.",
                    LicenseName = "MIT model weights and ISTA-DASLab terms", DownloadGb = 257, MinimumRamGb = 46, ComfortableRamGb = 64,
                    MinimumVramGb = 12, DeepSeekHardware = true
                }
            };
        }

        public static LocalModelChoice Find(string id)
        {
            foreach (LocalModelChoice choice in Create())
                if (String.Equals(choice.Id, id, StringComparison.OrdinalIgnoreCase)) return choice;
            return null;
        }
    }

    public static class LocalHardwareDetector
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

        public static LocalHardwareProfile Detect(string modelDirectory)
        {
            var profile = new LocalHardwareProfile { Processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Windows PC" };
            var memory = new MemoryStatus();
            if (GlobalMemoryStatusEx(memory))
            {
                profile.TotalMemoryBytes = ToLong(memory.TotalPhysical);
                profile.AvailableMemoryBytes = ToLong(memory.AvailablePhysical);
            }
            profile.FreeDiskBytes = ReadFreeDisk(modelDirectory);
            foreach (LocalGpuInfo gpu in ReadWmiGpus()) profile.Gpus.Add(gpu);
            MergeNvidiaSmi(profile.Gpus);
            return profile;
        }

        private static IEnumerable<LocalGpuInfo> ReadWmiGpus()
        {
            var result = new List<LocalGpuInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController"))
                using (ManagementObjectCollection objects = searcher.Get())
                {
                    foreach (ManagementObject item in objects)
                    {
                        string name = Convert.ToString(item["Name"]);
                        if (String.IsNullOrWhiteSpace(name) || name.IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        long memory = 0;
                        try { memory = Convert.ToInt64(item["AdapterRAM"]); } catch { }
                        result.Add(new LocalGpuInfo { Name = name, Driver = Convert.ToString(item["DriverVersion"]), MemoryBytes = memory });
                    }
                }
            }
            catch { }
            return result;
        }

        private static void MergeNvidiaSmi(List<LocalGpuInfo> gpus)
        {
            try
            {
                string executable = FindNvidiaSmi();
                if (String.IsNullOrWhiteSpace(executable)) return;
                var start = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--query-gpu=name,memory.total,driver_version --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process process = Process.Start(start))
                {
                    if (process == null) return;
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(4000)) { try { process.Kill(); } catch { } return; }
                    foreach (string raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = raw.Split(',');
                        if (parts.Length < 3) continue;
                        string name = parts[0].Trim();
                        double mib;
                        if (!Double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out mib)) mib = 0;
                        LocalGpuInfo existing = null;
                        foreach (LocalGpuInfo gpu in gpus)
                            if (gpu.IsNvidia && (gpu.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf(gpu.Name, StringComparison.OrdinalIgnoreCase) >= 0)) { existing = gpu; break; }
                        if (existing == null) { existing = new LocalGpuInfo { Name = name }; gpus.Add(existing); }
                        existing.MemoryBytes = (long)(mib * 1024 * 1024);
                        existing.Driver = parts[2].Trim();
                    }
                }
            }
            catch { }
        }

        private static string FindNvidiaSmi()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string known = Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");
            if (File.Exists(known)) return known;
            string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
            return File.Exists(system) ? system : null;
        }

        private static long ReadFreeDisk(string path)
        {
            try
            {
                string current = String.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetFullPath(path);
                while (!Directory.Exists(current))
                {
                    string parent = Path.GetDirectoryName(current);
                    if (String.IsNullOrWhiteSpace(parent) || parent == current) break;
                    current = parent;
                }
                string root = Path.GetPathRoot(current);
                return String.IsNullOrWhiteSpace(root) ? 0 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return 0; }
        }

        private static long ToLong(ulong value) { return value > long.MaxValue ? long.MaxValue : (long)value; }
    }
}
