using System;
using System.IO;
using System.Text;
using System.Threading;

namespace BanglaHost.Core;

/// <summary>
/// Crash-safe file writes.
///
/// <c>File.WriteAllText</c> truncates the target before writing the new content. If the process
/// dies, the disk loses power, or antivirus intercepts the handle in that window, the file is left
/// empty or half-written. For <c>banglahost.json</c> that meant every setting — including the
/// database root password — was silently replaced by defaults on the next launch, because Load()
/// treats a parse failure as "no config".
///
/// The sequence here is the standard one: write a sibling temp file, force it to physical disk,
/// then atomically swap it in while keeping the previous good copy as <c>.bak</c>. At every instant
/// a reader sees either the complete old content or the complete new content.
/// </summary>
public static class AtomicFile
{
    /// <summary>Atomically replace <paramref name="path"/> with <paramref name="content"/>,
    /// preserving the previous version as <c>path + ".bak"</c>.</summary>
    public static void WriteAllText(string path, string content, Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path required", nameof(path));
        encoding ??= new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        var bak = path + ".bak";

        // 1. Write the full new content to a temp file and force it to the platter. Without
        //    flushToDisk the bytes can still be in the OS cache when File.Replace returns, so a
        //    power loss immediately after the swap yields a zero-length "new" file.
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs, encoding))
        {
            w.Write(content);
            w.Flush();
            fs.Flush(flushToDisk: true);
        }

        // 2. Swap. File.Replace is atomic on NTFS and keeps the old content as the backup.
        //    It requires the destination to exist, so first-write falls back to a plain move.
        RetryOnSharingViolation(() =>
        {
            if (File.Exists(path))
            {
                File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmp, path);
            }
        });
    }

    /// <summary>Read a file, falling back to its <c>.bak</c> if the primary is missing or fails
    /// <paramref name="validate"/>. Returns null when neither copy is usable — which the caller
    /// must distinguish from "file legitimately absent", because the two mean very different things.
    /// </summary>
    public static string? ReadWithBackup(string path, Func<string, bool>? validate, out ReadOutcome outcome)
    {
        validate ??= _ => true;
        var bak = path + ".bak";

        if (File.Exists(path))
        {
            try
            {
                var text = ReadShared(path);
                if (validate(text)) { outcome = ReadOutcome.Primary; return text; }
            }
            catch { /* fall through to the backup */ }

            // Primary exists but is unreadable or invalid — this is the corruption case.
            if (File.Exists(bak))
            {
                try
                {
                    var text = ReadShared(bak);
                    if (validate(text)) { outcome = ReadOutcome.RecoveredFromBackup; return text; }
                }
                catch { }
            }

            outcome = ReadOutcome.Corrupt;
            return null;
        }

        // No primary. A lone .bak means a swap was interrupted; prefer it over nothing.
        if (File.Exists(bak))
        {
            try
            {
                var text = ReadShared(bak);
                if (validate(text)) { outcome = ReadOutcome.RecoveredFromBackup; return text; }
            }
            catch { }
        }

        outcome = ReadOutcome.Absent;
        return null;
    }

    public enum ReadOutcome
    {
        /// <summary>File genuinely does not exist. Defaults are correct.</summary>
        Absent,
        /// <summary>Read the primary file successfully.</summary>
        Primary,
        /// <summary>Primary was bad; the .bak was good. Caller should rewrite the primary.</summary>
        RecoveredFromBackup,
        /// <summary>Both copies are unusable. Defaults are a data-loss event, not a fresh start.</summary>
        Corrupt,
    }

    private static string ReadShared(string path)
    {
        // FileShare.ReadWrite so a concurrent writer (or an AV scanner) doesn't turn a read into
        // an IOException that Load() would otherwise interpret as corruption.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(fs, detectEncodingFromByteOrderMarks: true);
        return r.ReadToEnd();
    }

    /// <summary>Windows hands out transient sharing violations when AV or a search indexer has the
    /// file open. Retrying briefly turns a spurious failure into a success.</summary>
    private static void RetryOnSharingViolation(Action action)
    {
        const int attempts = 5;
        for (var i = 0; ; i++)
        {
            try { action(); return; }
            catch (IOException) when (i < attempts - 1) { Thread.Sleep(40 * (i + 1)); }
            catch (UnauthorizedAccessException) when (i < attempts - 1) { Thread.Sleep(40 * (i + 1)); }
        }
    }
}
