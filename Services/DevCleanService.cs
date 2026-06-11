using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ztools.Models;

namespace ztools.Services;

/// <summary>
/// Scans directories for heavy development artifacts (node_modules, Rust
/// target, …) and deletes them as fast as possible:
///   - parallel work-stealing directory walk (skips junctions / system dirs)
///   - rename-then-delete so the original path is freed instantly
///   - parallel recursive delete with read-only attribute fallback
///     (Go module cache files are read-only by design).
/// </summary>
public static class DevCleanService
{
    public readonly record struct ScanHit(string Path, CleanCategory Category);

    // Directories never worth descending into.
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", "Windows",
        "Program Files", "Program Files (x86)", "ProgramData",
        ".git", ".svn", ".hg", ".vs", ".idea",
    };

    // ── Scan ─────────────────────────────────────────────────────────────────

    /// <summary>Parallel scan. Callbacks may fire on any thread.</summary>
    public static void Scan(IReadOnlyList<string> roots,
                            Action<ScanHit> onHit,
                            Action<string> onProgress,
                            CancellationToken ct)
    {
        var po = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Environment.ProcessorCount,
        };

        long lastReportTicks = 0;

        void Walk(string dir)
        {
            ct.ThrowIfCancellationRequested();

            // Throttled progress (~ every 150 ms).
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref lastReportTicks);
            if (now - last > 150 &&
                Interlocked.CompareExchange(ref lastReportTicks, now, last) == last)
            {
                onProgress(dir);
            }

            List<string>? recurse = null;
            IEnumerable<string> subs;
            try { subs = Directory.EnumerateDirectories(dir); }
            catch { return; } // access denied / vanished

            foreach (var sub in subs)
            {
                var name = Path.GetFileName(sub);
                if (SkipDirs.Contains(name)) continue;

                FileAttributes attrs;
                try { attrs = File.GetAttributes(sub); }
                catch { continue; }
                if ((attrs & FileAttributes.ReparsePoint) != 0) continue; // junction/symlink

                if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    onHit(new ScanHit(sub, CleanCategory.NodeModules));
                    continue; // never descend
                }

                if (name.Equals("target", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(dir, "Cargo.toml")))
                {
                    onHit(new ScanHit(sub, CleanCategory.RustTarget));
                    continue;
                }

                (recurse ??= []).Add(sub);
            }

            if (recurse is null) return;
            if (recurse.Count == 1) Walk(recurse[0]);
            else Parallel.ForEach(recurse, po, Walk);
        }

        var existing = new List<string>();
        foreach (var r in roots)
            if (Directory.Exists(r)) existing.Add(r);

        Parallel.ForEach(existing, po, Walk);
    }

    // ── Size ─────────────────────────────────────────────────────────────────

    public static long GetDirectorySize(string path)
    {
        try
        {
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };
            long total = 0;
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opts))
                total += f.Length;
            return total;
        }
        catch
        {
            return 0;
        }
    }

    // ── Delete (fast) ────────────────────────────────────────────────────────

    /// <summary>
    /// Fast delete: rename the directory to a temp sibling first (instant —
    /// frees the original path), then delete first-level children in parallel.
    /// </summary>
    public static async Task DeleteDirectoryFastAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path)) return;

        // 1) Rename out of the way (same volume ⇒ metadata-only, instant).
        string victim = path;
        try
        {
            string temp = path + ".ztdel-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(path, temp);
            victim = temp;
        }
        catch
        {
            // Rename failed (locked file etc.) — delete in place.
        }

        await Task.Run(() =>
        {
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(victim); }
            catch { subdirs = []; }

            if (subdirs.Length > 1)
            {
                Parallel.ForEach(subdirs, new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                }, DeleteTree);
            }

            DeleteTree(victim);
        }, ct).ConfigureAwait(false);
    }

    private static void DeleteTree(string dir)
    {
        try
        {
            Directory.Delete(dir, true);
            return;
        }
        catch (DirectoryNotFoundException) { return; }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // Fallback: clear read-only attributes (Go mod cache!) and retry.
        ClearAttributes(dir);
        Directory.Delete(dir, true);
    }

    private static void ClearAttributes(string dir)
    {
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        try
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", opts))
            {
                try { entry.Attributes = FileAttributes.Normal; }
                catch { /* best effort */ }
            }
        }
        catch { /* best effort */ }
    }

    // ── Well-known global caches ─────────────────────────────────────────────

    public static IEnumerable<ScanHit> GetKnownCaches()
    {
        string home  = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new (string Path, CleanCategory Category)[]
        {
            (Path.Combine(local, "npm-cache"),                CleanCategory.NpmCache),
            (Path.Combine(local, "pnpm", "store"),            CleanCategory.PnpmStore),
            (Path.Combine(local, "Yarn", "Cache"),            CleanCategory.YarnCache),
            (Path.Combine(home,  ".bun", "install", "cache"), CleanCategory.BunCache),
            (GoModPath(home),                                  CleanCategory.GoModCache),
            (Path.Combine(local, "go-build"),                 CleanCategory.GoBuildCache),
            (Path.Combine(home,  ".cargo", "registry"),       CleanCategory.CargoRegistry),
        };

        foreach (var (p, cat) in candidates)
            if (Directory.Exists(p))
                yield return new ScanHit(p, cat);
    }

    private static string GoModPath(string home)
    {
        var gopath = Environment.GetEnvironmentVariable("GOPATH");
        if (string.IsNullOrWhiteSpace(gopath))
            gopath = Path.Combine(home, "go");
        return Path.Combine(gopath, "pkg", "mod");
    }
}
