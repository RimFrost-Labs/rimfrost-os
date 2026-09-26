using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RimFrostSetup
{
    enum Verdict { Ok, Warn, Block }

    class CheckItem
    {
        public string Title;
        public string Detail;
        public Verdict Verdict;
    }

    /// What Setup learns about the PC before touching anything.
    class SystemInfo
    {
        public bool Uefi;
        public bool? SecureBoot;
        public long RamBytes;
        public List<string> Gpus = new List<string>();
        public int DiskNumber;
        public string DiskName;
        public string PartitionStyle;
        public string BusType;
        public long DiskSize;
        public long COffset;
        public long CSize;
        public long CSizeMin;          // smallest C: can be shrunk to
        public long CFree;
        public int BitLockerProtection; // 0 off, 1 on, -1 unknown
        public long LargestFreeExtent;

        public const long GB = 1024L * 1024 * 1024;

        /// Space RimFrost OS can get next to Windows. Windows keeps its own
        /// data plus 20 GB to breathe.
        public long MaxForRimFrost(long stagingBytes)
        {
            long keepForWindows = Math.Max(CSizeMin, CSize - CFree) + 20 * GB;
            return Math.Max(0, CSize - keepForWindows - stagingBytes);
        }

        public string NvidiaGpu => Gpus.FirstOrDefault(g => g.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    static class SystemCheck
    {
        public static SystemInfo Gather()
        {
            // One PowerShell run, key=value lines back
            string outp = Shell.PowerShell(@"
$c = Get-Partition -DriveLetter C
$d = Get-Disk -Number $c.DiskNumber
$s = Get-PartitionSupportedSize -DriveLetter C
$v = Get-Volume -DriveLetter C
'uefi=' + $env:firmware_type
try { 'secureboot=' + (Confirm-SecureBootUEFI) } catch { 'secureboot=unknown' }
'ram=' + (Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory
Get-CimInstance Win32_VideoController | ForEach-Object { 'gpu=' + $_.Name }
'disk=' + $d.Number
'diskname=' + $d.FriendlyName
'style=' + $d.PartitionStyle
'bus=' + $d.BusType
'disksize=' + $d.Size
'largestfree=' + $d.LargestFreeExtent
'coffset=' + $c.Offset
'csize=' + $c.Size
'cmin=' + $s.SizeMin
'cfree=' + $v.SizeRemaining
try {
  $b = Get-CimInstance -Namespace root/cimv2/security/microsoftvolumeencryption -ClassName Win32_EncryptableVolume -Filter ""DriveLetter='C:'""
  if ($b) { 'bitlocker=' + $b.ProtectionStatus } else { 'bitlocker=0' }
} catch { 'bitlocker=-1' }
");
            var info = new SystemInfo();
            foreach (var raw in outp.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq), val = line.Substring(eq + 1).Trim();
                switch (k)
                {
                    case "uefi": info.Uefi = val.Equals("UEFI", StringComparison.OrdinalIgnoreCase); break;
                    case "secureboot": info.SecureBoot = val == "True" ? true : val == "False" ? (bool?)false : null; break;
                    case "ram": info.RamBytes = L(val); break;
                    case "gpu": if (val.Length > 0) info.Gpus.Add(val); break;
                    case "disk": info.DiskNumber = (int)L(val); break;
                    case "diskname": info.DiskName = val; break;
                    case "style": info.PartitionStyle = val; break;
                    case "bus": info.BusType = val; break;
                    case "disksize": info.DiskSize = L(val); break;
                    case "largestfree": info.LargestFreeExtent = L(val); break;
                    case "coffset": info.COffset = L(val); break;
                    case "csize": info.CSize = L(val); break;
                    case "cmin": info.CSizeMin = L(val); break;
                    case "cfree": info.CFree = L(val); break;
                    case "bitlocker": info.BitLockerProtection = (int)L(val); break;
                }
            }
            return info;
        }

        static long L(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

        public static string Gb(long bytes) => (bytes / (double)SystemInfo.GB).ToString("0", CultureInfo.InvariantCulture) + " GB";

        /// Plain-language verdicts shown on the first page.
        public static List<CheckItem> Evaluate(SystemInfo i, long stagingBytes, long minRimFrost)
        {
            var list = new List<CheckItem>();

            list.Add(i.Uefi
                ? new CheckItem { Title = "Modern startup (UEFI)", Detail = "Your PC starts the modern way.", Verdict = Verdict.Ok }
                : new CheckItem { Title = "Old startup mode (Legacy/CSM)", Detail = "RimFrost OS needs UEFI. Switch off CSM/Legacy in your PC's firmware settings, then run Setup again.", Verdict = Verdict.Block });

            if (!string.Equals(i.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
                list.Add(new CheckItem { Title = "Drive layout", Detail = "Your Windows drive uses the old MBR layout. RimFrost Setup needs GPT.", Verdict = Verdict.Block });

            if (string.Equals(i.BusType, "RAID", StringComparison.OrdinalIgnoreCase))
                list.Add(new CheckItem { Title = "Drive controller in RAID/RST mode", Detail = "Your drive runs through Intel RST, which Linux can't see. Switch the SATA mode to AHCI in the firmware settings first (Windows needs a small change before that; see the guide).", Verdict = Verdict.Block });

            long ramGb = (long)Math.Round(i.RamBytes / (double)SystemInfo.GB);
            list.Add(ramGb >= 8
                ? new CheckItem { Title = "Memory", Detail = $"{ramGb} GB", Verdict = ramGb >= 16 ? Verdict.Ok : Verdict.Warn }
                : new CheckItem { Title = "Memory", Detail = $"{ramGb} GB. RimFrost OS needs at least 8 GB.", Verdict = Verdict.Block });

            string nv = i.NvidiaGpu;
            string gpuText = i.Gpus.Count == 0 ? "Not detected" : string.Join(", ", i.Gpus);
            list.Add(new CheckItem
            {
                Title = "Graphics",
                Detail = nv != null ? gpuText + ". The Nvidia driver is set up by itself on the first start." : gpuText,
                Verdict = Verdict.Ok,
            });

            if (i.BitLockerProtection == 1)
                list.Add(new CheckItem { Title = "BitLocker is on", Detail = "Setup pauses it until the install is done. Make sure you have your recovery key (aka.ms/myrecoverykey).", Verdict = Verdict.Warn });

            if (i.SecureBoot == true)
                list.Add(new CheckItem { Title = "Secure Boot is on", Detail = "That's fine. After the install you approve RimFrost's key once on a blue screen.", Verdict = Verdict.Ok });

            long room = i.MaxForRimFrost(stagingBytes);
            list.Add(room >= minRimFrost
                ? new CheckItem { Title = "Space next to Windows", Detail = $"Up to {Gb(room)} available on {i.DiskName}.", Verdict = Verdict.Ok }
                : new CheckItem { Title = "Space next to Windows", Detail = $"Only {Gb(room)} can be freed on {i.DiskName}; RimFrost OS needs {Gb(minRimFrost)}. You can still replace Windows.", Verdict = Verdict.Warn });

            return list;
        }
    }
}
