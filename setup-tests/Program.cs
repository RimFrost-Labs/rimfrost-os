using System;
using System.IO;
using System.Security.Cryptography;

namespace RimFrostSetup
{
    static class Program
    {
        static int failures;

        static void Check(string name, bool ok)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
            if (!ok) failures++;
        }

        static int Main(string[] args)
        {
            if (args.Length == 2)
            {
                byte[] hash;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(args[0]))
                    hash = sha.ComputeHash(fs);
                byte[] sig = Convert.FromBase64String(File.ReadAllText(args[1]).Trim());
                Check("real ISO signature verifies", Downloader.VerifySignature(hash, sig));

                var wrong = (byte[])hash.Clone();
                wrong[0] ^= 1;
                Check("a different file is rejected", !Downloader.VerifySignature(wrong, sig));

                var badSig = (byte[])sig.Clone();
                badSig[badSig.Length - 1] ^= 1;
                Check("a damaged signature is rejected", !Downloader.VerifySignature(hash, badSig));
            }

            // Space arithmetic: Windows keeps its data plus 20 GB
            var s = new SystemInfo { CSize = 500 * SystemInfo.GB, CFree = 300 * SystemInfo.GB, CSizeMin = 150 * SystemInfo.GB };
            long max = s.MaxForRimFrost(10 * SystemInfo.GB);
            Check("room next to Windows = 500 - max(150, 200) - 20 - 10 GB", max == 270 * SystemInfo.GB);
            var full = new SystemInfo { CSize = 256 * SystemInfo.GB, CFree = 20 * SystemInfo.GB, CSizeMin = 230 * SystemInfo.GB };
            Check("a full drive has no room", full.MaxForRimFrost(10 * SystemInfo.GB) == 0);

            Console.WriteLine(failures == 0 ? "all passed" : $"{failures} failed");
            return failures == 0 ? 0 : 1;
        }
    }
}
