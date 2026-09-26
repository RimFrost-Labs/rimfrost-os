using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RimFrostSetup
{
    /// Downloads the RimFrost OS ISO, resuming after a dropped connection, and
    /// checks it twice: the published SHA-256, and our cosign signature (the
    /// same key that signs the system images, embedded in Setup).
    class Downloader
    {
        public const string BaseUrl = "https://download.rimfrost.online/";
        public const string IsoName = "rimfrost-os.iso";

        static readonly HttpClient Http = CreateClient();

        static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("RimFrostSetup/0.5");
            return c;
        }

        /// Size of the ISO on the server, so Setup can size the partition before downloading.
        public static async Task<long> GetIsoSize(CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Head, BaseUrl + IsoName))
            using (var resp = await Http.SendAsync(req, ct))
            {
                resp.EnsureSuccessStatusCode();
                return resp.Content.Headers.ContentLength ?? throw new InvalidOperationException("The server didn't say how big the download is.");
            }
        }

        /// progress(bytesDone, bytesTotal)
        public static async Task Download(string target, Action<long, long> progress, CancellationToken ct)
        {
            long total = await GetIsoSize(ct);
            for (int attempt = 1; ; attempt++)
            {
                long have = File.Exists(target) ? new FileInfo(target).Length : 0;
                if (have == total) break;
                if (have > total) { File.Delete(target); have = 0; }
                try
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl + IsoName))
                    {
                        if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
                        using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct))
                        {
                            resp.EnsureSuccessStatusCode();
                            if (have > 0 && resp.StatusCode != HttpStatusCode.PartialContent) have = 0; // server ignored the range
                            using (var src = await resp.Content.ReadAsStreamAsync())
                            using (var dst = new FileStream(target, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                            {
                                var buf = new byte[1 << 20];
                                int n;
                                while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                                {
                                    await dst.WriteAsync(buf, 0, n, ct);
                                    have += n;
                                    progress(have, total);
                                }
                            }
                        }
                    }
                }
                catch (Exception e) when (!(e is OperationCanceledException) && attempt < 20)
                {
                    Log.Write($"download interrupted ({e.Message}), resuming, attempt {attempt}");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)), ct);
                }
            }
            Log.Write($"downloaded {total} bytes to {target}");
        }

        /// Throws if the file isn't exactly what we published.
        public static async Task Verify(string file, Action<long, long> progress, CancellationToken ct)
        {
            string sumLine = await Http.GetStringAsync(BaseUrl + IsoName + ".sha256");
            string expected = sumLine.Trim().Split(' ', '\t')[0].ToLowerInvariant();
            string sigB64 = (await Http.GetStringAsync(BaseUrl + IsoName + ".sig")).Trim();

            byte[] hash;
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                var buf = new byte[1 << 20];
                long done = 0, total = fs.Length;
                int n;
                while ((n = await fs.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                {
                    sha.TransformBlock(buf, 0, n, null, 0);
                    done += n;
                    progress(done, total);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                hash = sha.Hash;
            }
            string actual = string.Concat(hash.Select(b => b.ToString("x2")));
            Log.Write($"sha256 expected {expected} got {actual}");
            if (actual != expected)
                throw new InvalidOperationException("The download is damaged (checksum doesn't match). Setup will download it again next time.");

            if (!VerifySignature(hash, Convert.FromBase64String(sigB64)))
                throw new InvalidOperationException("The download isn't signed by RimFrost Labs. Setup stopped to keep your PC safe.");
            Log.Write("signature OK");
        }

        /// cosign sign-blob: ECDSA P-256 over SHA-256, DER-encoded signature.
        static bool VerifySignature(byte[] sha256, byte[] derSig)
        {
            string pem;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("cosign.pub"))
            using (var r = new StreamReader(s, Encoding.ASCII))
                pem = r.ReadToEnd();
            string b64 = string.Concat(pem.Split('\n').Where(l => !l.StartsWith("-----")).Select(l => l.Trim()));
            byte[] spki = Convert.FromBase64String(b64);
            // A P-256 SubjectPublicKeyInfo ends with the uncompressed point 04 || X || Y
            if (spki.Length < 65 || spki[spki.Length - 65] != 0x04)
                throw new InvalidOperationException("Setup's signing key is unreadable.");
            var q = new ECPoint
            {
                X = spki.Skip(spki.Length - 64).Take(32).ToArray(),
                Y = spki.Skip(spki.Length - 32).Take(32).ToArray(),
            };
            using (var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = q }))
                return ec.VerifyHash(sha256, DerToP1363(derSig, 32));
        }

        static byte[] DerToP1363(byte[] der, int size)
        {
            int i = 0;
            if (der[i++] != 0x30) throw new FormatException("bad signature");
            ReadLen(der, ref i);
            byte[] r = ReadInt(der, ref i), s = ReadInt(der, ref i);
            var outp = new byte[2 * size];
            Buffer.BlockCopy(r, 0, outp, size - r.Length, r.Length);
            Buffer.BlockCopy(s, 0, outp, 2 * size - s.Length, s.Length);
            return outp;
        }

        static int ReadLen(byte[] d, ref int i)
        {
            int len = d[i++];
            if ((len & 0x80) == 0) return len;
            int n = len & 0x7f; len = 0;
            while (n-- > 0) len = (len << 8) | d[i++];
            return len;
        }

        static byte[] ReadInt(byte[] d, ref int i)
        {
            if (d[i++] != 0x02) throw new FormatException("bad signature");
            int len = ReadLen(d, ref i);
            var v = d.Skip(i).Take(len).SkipWhile(b => b == 0).ToArray();
            i += len;
            return v;
        }
    }
}
