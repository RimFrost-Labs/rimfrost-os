using System;
using System.Diagnostics;
using System.Text;

namespace RimFrostSetup
{
    class CommandFailed : Exception
    {
        public CommandFailed(string message) : base(message) { }
    }

    /// Runs Windows' own tools (PowerShell storage cmdlets, bcdedit, mountvol,
    /// manage-bde). Using the built-in tools keeps Setup small and means every
    /// step can be repeated by hand from the log.
    static class Shell
    {
        public static string Run(string exe, string args, bool check = true)
        {
            Log.Write($"$ {exe} {args}");
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                var stderr = p.StandardError.ReadToEndAsync();
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                string err = stderr.Result;
                if (stdout.Trim().Length > 0) Log.Write("  out: " + stdout.Trim());
                if (err.Trim().Length > 0) Log.Write("  err: " + err.Trim());
                if (check && p.ExitCode != 0)
                    throw new CommandFailed($"{exe} failed (exit {p.ExitCode}): {(err.Trim().Length > 0 ? err.Trim() : stdout.Trim())}");
                return stdout;
            }
        }

        /// Runs a PowerShell script. Errors stop the script and fail the call.
        public static string PowerShell(string script, bool check = true)
        {
            string full = "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
                          "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + script;
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            Log.Write("ps> " + script);
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return Run(System.IO.Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe"),
                       "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, check);
        }

        public static string SystemTool(string name) =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    }
}
