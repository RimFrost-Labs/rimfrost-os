using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RimFrostSetup
{
    enum InstallMode { NextToWindows, ReplaceWindows }

    /// Puts the RimFrost OS installer on the PC's own drive and makes the PC
    /// start it once. The layout it leaves (proven in a VM before Setup existed):
    ///
    ///   [ Windows C: (shrunk) ][ free space for RimFrost OS ][ RFBOOT ][ RFSETUP ]
    ///
    ///   RFBOOT   FAT32, 512 MB: the installer's kernel and initrd (GRUB reads FAT only)
    ///   RFSETUP  exFAT: rimfrost-os.iso; the installer finds it with iso-scan
    ///   ESP      \EFI\RimFrostSetup\ shim + GRUB + grub.cfg, and a one-time boot entry
    ///
    /// The installer puts RimFrost OS into the free space; the staging partitions
    /// sit right after it, so the installed system takes their room afterwards.
    /// In "replace" mode there is no free-space gap: the installer uses Windows' room.
    class Stager
    {
        public const string BootLabel = "RFBOOT";
        public const string SetupLabel = "RFSETUP";
        public const string EspDir = @"EFI\RimFrostSetup";
        const string BasicDataGpt = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}";
        const long MB = 1024L * 1024;
        public const long BootSize = 512 * MB;

        readonly SystemInfo sys;
        readonly InstallMode mode;
        readonly long rimfrostBytes;
        readonly Action<string, double> report; // step text, 0..1 (or -1 for unknown)

        public Stager(SystemInfo sys, InstallMode mode, long rimfrostBytes, Action<string, double> report)
        {
            this.sys = sys; this.mode = mode; this.rimfrostBytes = rimfrostBytes; this.report = report;
        }

        public static long SetupSizeFor(long isoBytes) => AlignUp(isoBytes + 256 * MB);
        static long AlignUp(long v) => (v + MB - 1) / MB * MB;
        static long AlignDown(long v) => v / MB * MB;
        static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

        public async Task Run(CancellationToken ct)
        {
            Log.Write($"--- stage: mode={mode} rimfrost={rimfrostBytes} disk={sys.DiskNumber}");

            report("Checking the download size", -1);
            long isoBytes = await Downloader.GetIsoSize(ct);
            long setupSize = SetupSizeFor(isoBytes);

            var parts = FindStaging();
            if (parts.Boot == null || parts.Setup == null)
            {
                if (sys.BitLockerProtection == 1)
                {
                    report("Pausing BitLocker until the install is done", -1);
                    // A few restarts: into the installer, into RimFrost OS, back to Windows
                    Shell.Run(Shell.SystemTool("manage-bde.exe"), "-protectors -disable C: -RebootCount 3");
                }
                report("Making room on the drive", -1);
                CreateStaging(setupSize);
                parts = FindStaging();
                if (parts.Boot == null || parts.Setup == null)
                    throw new CommandFailed("The new partitions didn't show up after creating them.");
            }
            else Log.Write("staging partitions already exist, resuming");

            string bootDrive = EnsureLetter(parts.Boot);
            string setupDrive = EnsureLetter(parts.Setup);
            string iso = Path.Combine(setupDrive, Downloader.IsoName);

            report("Downloading RimFrost OS", 0);
            await Downloader.Download(iso, (d, t) => report($"Downloading RimFrost OS ({SystemCheck.Gb(d)} of {SystemCheck.Gb(t)})", d / (double)t), ct);

            report("Checking the download", 0);
            await Downloader.Verify(iso, (d, t) => report("Checking the download", d / (double)t), ct);

            report("Preparing the installer", -1);
            string isoLabel = CopyFromIso(iso, bootDrive);
            WriteBootFiles(isoLabel);

            report("Adding a one-time start entry", -1);
            AddBootEntry();

            // Out of sight in Explorer; GRUB and the installer find them by label
            RemoveLetter(parts.Boot, bootDrive);
            RemoveLetter(parts.Setup, setupDrive);
            Log.Write("--- stage done");
        }

        class Staging { public string Boot, Setup; } // "offset" of each partition, or null

        /// Our partitions on the Windows disk, found by label.
        Staging FindStaging()
        {
            string outp = Shell.PowerShell($@"
Get-Partition -DiskNumber {sys.DiskNumber} | ForEach-Object {{
  $v = $_ | Get-Volume -ErrorAction SilentlyContinue
  if ($v) {{ $v.FileSystemLabel + '=' + $_.Offset }}
}}");
            var st = new Staging();
            foreach (var line in outp.Split('\n').Select(l => l.Trim()))
            {
                if (line.StartsWith(BootLabel + "=")) st.Boot = line.Substring(BootLabel.Length + 1);
                if (line.StartsWith(SetupLabel + "=")) st.Setup = line.Substring(SetupLabel.Length + 1);
            }
            return st;
        }

        void CreateStaging(long setupSize)
        {
            long staging = BootSize + setupSize;
            long shrinkBy = staging + (mode == InstallMode.NextToWindows ? rimfrostBytes : 0);
            long newCSize = AlignDown(sys.CSize - shrinkBy);
            if (newCSize < sys.CSizeMin)
                throw new CommandFailed($"Windows can't make that much room (C: can shrink to {SystemCheck.Gb(sys.CSizeMin)} at most).");

            long cEnd = sys.COffset + newCSize;
            long bootOffset = AlignUp(cEnd + (mode == InstallMode.NextToWindows ? rimfrostBytes : 0));
            long setupOffset = bootOffset + BootSize;

            Shell.PowerShell($"Resize-Partition -DriveLetter C -Size {N(newCSize)}");
            Shell.PowerShell($@"
$p = New-Partition -DiskNumber {sys.DiskNumber} -Offset {N(bootOffset)} -Size {N(BootSize)} -GptType '{BasicDataGpt}'
$p | Format-Volume -FileSystem FAT32 -NewFileSystemLabel {BootLabel} -Force -Confirm:$false | Out-Null
$q = New-Partition -DiskNumber {sys.DiskNumber} -Offset {N(setupOffset)} -Size {N(setupSize)} -GptType '{BasicDataGpt}'
$q | Format-Volume -FileSystem exFAT -NewFileSystemLabel {SetupLabel} -Force -Confirm:$false | Out-Null");
        }

        string EnsureLetter(string offset)
        {
            string letter = Shell.PowerShell($@"
$p = Get-Partition -DiskNumber {sys.DiskNumber} | Where-Object Offset -eq {offset}
if (-not $p.DriveLetter -or $p.DriveLetter -eq [char]0) {{ $p | Add-PartitionAccessPath -AssignDriveLetter; $p = Get-Partition -DiskNumber {sys.DiskNumber} | Where-Object Offset -eq {offset} }}
$p.DriveLetter").Trim();
            if (letter.Length != 1) throw new CommandFailed("Couldn't give the setup partition a drive letter.");
            return letter + @":\";
        }

        void RemoveLetter(string offset, string drive)
        {
            Shell.PowerShell($@"Get-Partition -DiskNumber {sys.DiskNumber} | Where-Object Offset -eq {offset} | Remove-PartitionAccessPath -AccessPath '{drive}'", check: false);
        }

        string espLetter;

        /// Copies the installer's kernel/initrd to RFBOOT and shim/GRUB to the ESP.
        /// Returns the ISO's volume label (the installer looks for it).
        string CopyFromIso(string iso, string bootDrive)
        {
            string letter = Shell.PowerShell($@"(Mount-DiskImage -ImagePath '{iso}' -StorageType ISO -Access ReadOnly -PassThru | Get-Volume).DriveLetter").Trim();
            string label = Shell.PowerShell($@"(Get-DiskImage -ImagePath '{iso}' | Get-Volume).FileSystemLabel").Trim();
            try
            {
                string src = letter + @":\";
                File.Copy(Path.Combine(src, @"images\pxeboot\vmlinuz"), Path.Combine(bootDrive, "vmlinuz"), true);
                File.Copy(Path.Combine(src, @"images\pxeboot\initrd.img"), Path.Combine(bootDrive, "initrd.img"), true);

                espLetter = MountEsp();
                string dir = Path.Combine(espLetter, EspDir);
                Directory.CreateDirectory(dir);
                foreach (var f in new[] { "shimx64.efi", "grubx64.efi", "mmx64.efi" })
                    File.Copy(Path.Combine(src, @"EFI\fedora", f), Path.Combine(dir, f), true);
            }
            finally
            {
                Shell.PowerShell($@"Dismount-DiskImage -ImagePath '{iso}' | Out-Null", check: false);
            }
            if (string.IsNullOrEmpty(label)) throw new CommandFailed("Couldn't read the installer's label.");
            return label;
        }

        void WriteBootFiles(string isoLabel)
        {
            string modeArg = mode == InstallMode.NextToWindows ? "next-to-windows" : "replace-windows";
            string args = $"quiet rhgb iso-scan/filename=/{Downloader.IsoName} root=live:CDLABEL={isoLabel} enforcing=0 rd.live.image rimfrost.setup={modeArg}";
            string cfg =
$@"# Written by RimFrost Setup. Starts the RimFrost OS installer from the
# {SetupLabel} partition; removed again after the install.
set timeout=3
set default=0
insmod part_gpt
insmod fat
search --no-floppy --set=root --label {BootLabel}
menuentry 'Install RimFrost OS' {{
  linux /vmlinuz {args}
  initrd /initrd.img
}}
menuentry 'Install RimFrost OS (basic graphics)' {{
  linux /vmlinuz {args} nomodeset
  initrd /initrd.img
}}
menuentry 'Back to Windows' {{
  chainloader /EFI/Microsoft/Boot/bootmgfw.efi
}}
";
            // The Windows entry needs the ESP as root; GRUB switches with search
            cfg = cfg.Replace("  chainloader /EFI/Microsoft/Boot/bootmgfw.efi",
                              "  search --no-floppy --set=root --file /EFI/Microsoft/Boot/bootmgfw.efi\n  chainloader /EFI/Microsoft/Boot/bootmgfw.efi");
            File.WriteAllText(Path.Combine(espLetter, EspDir, "grub.cfg"), cfg.Replace("\r\n", "\n"), new UTF8Encoding(false));
            UnmountEsp();
        }

        static string FreeLetter()
        {
            var used = new HashSet<char>(DriveInfo.GetDrives().Select(d => char.ToUpperInvariant(d.Name[0])));
            for (char c = 'S'; c <= 'Z'; c++) if (!used.Contains(c)) return c + ":";
            for (char c = 'R'; c >= 'H'; c--) if (!used.Contains(c)) return c + ":";
            throw new CommandFailed("No free drive letter for the EFI partition.");
        }

        static string mountedEsp;

        static string MountEsp()
        {
            mountedEsp = FreeLetter();
            Shell.Run(Shell.SystemTool("mountvol.exe"), mountedEsp + " /s");
            return mountedEsp + @"\";
        }

        static void UnmountEsp()
        {
            if (mountedEsp == null) return;
            Shell.Run(Shell.SystemTool("mountvol.exe"), mountedEsp + " /d", check: false);
            mountedEsp = null;
        }

        static string Bcd(string args) => Shell.Run(Shell.SystemTool("bcdedit.exe"), args);

        void AddBootEntry()
        {
            string existing = FindBootEntry();
            string id = existing;
            if (id == null)
            {
                string outp = Bcd("/copy {bootmgr} /d \"RimFrost OS Setup\"");
                var m = Regex.Match(outp, @"\{[0-9a-fA-F-]{36}\}");
                if (!m.Success) throw new CommandFailed("bcdedit didn't return the new entry.");
                id = m.Value;
            }
            Bcd($"/set {id} path \\{EspDir}\\shimx64.efi");
            // Start it once on the next restart; after that the PC starts as before
            Bcd($"/set {{fwbootmgr}} bootsequence {id}");
            File.WriteAllText(Path.Combine(Log.Dir, "boot-entry.txt"), id);
            Log.Write("boot entry " + id);
        }

        /// Our firmware entry, if an earlier run made one.
        static string FindBootEntry()
        {
            string outp = Shell.Run(Shell.SystemTool("bcdedit.exe"), "/enum firmware", check: false);
            string current = null;
            foreach (var line in outp.Split('\n'))
            {
                var m = Regex.Match(line, @"^identifier\s+(\{[0-9a-fA-F-]{36}\})");
                if (m.Success) current = m.Groups[1].Value;
                if (current != null && line.Contains("RimFrost OS Setup")) return current;
            }
            return null;
        }

        /// Undoes everything Setup did: boot entry, ESP folder, partitions, and
        /// gives the room back to C:. Safe to run more than once.
        public static void Undo(SystemInfo sys)
        {
            Log.Write("--- undo");
            string id = FindBootEntry();
            if (id != null) Shell.Run(Shell.SystemTool("bcdedit.exe"), $"/delete {id}", check: false);
            try
            {
                string esp = MountEsp();
                string dir = Path.Combine(esp, EspDir);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e) { Log.Write("undo esp: " + e.Message); }
            finally { UnmountEsp(); }

            Shell.PowerShell($@"
Get-Partition -DiskNumber {sys.DiskNumber} | Where-Object {{
  $v = $_ | Get-Volume -ErrorAction SilentlyContinue
  $v -and ($v.FileSystemLabel -eq '{BootLabel}' -or $v.FileSystemLabel -eq '{SetupLabel}')
}} | Remove-Partition -Confirm:$false
$max = (Get-PartitionSupportedSize -DriveLetter C).SizeMax
Resize-Partition -DriveLetter C -Size $max", check: false);
            Shell.Run(Shell.SystemTool("manage-bde.exe"), "-protectors -enable C:", check: false);
            Log.Write("--- undo done");
        }

        public static bool HasStaging(SystemInfo sys)
        {
            string outp = Shell.PowerShell($@"
Get-Partition -DiskNumber {sys.DiskNumber} | ForEach-Object {{ ($_ | Get-Volume -ErrorAction SilentlyContinue).FileSystemLabel }}", check: false);
            return outp.Split('\n').Any(l => l.Trim() == BootLabel || l.Trim() == SetupLabel);
        }
    }
}
