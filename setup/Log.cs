using System;
using System.IO;

namespace RimFrostSetup
{
    /// Everything Setup does goes to %ProgramData%\RimFrostSetup\setup.log, so a
    /// failed install can be reported with the log attached.
    static class Log
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "RimFrostSetup");
        public static readonly string FilePath = Path.Combine(Dir, "setup.log");
        static readonly object Gate = new object();

        public static void Write(string line)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Dir);
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
                }
                catch { /* logging must never stop the install */ }
            }
        }
    }
}
