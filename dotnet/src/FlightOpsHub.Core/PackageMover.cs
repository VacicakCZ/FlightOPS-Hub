namespace FlightOpsHub.Core;

/// <summary>
/// Port of move_package/_resolve_pending_moves/estimate_apply_space/
/// apply_package_changes from scenery_data.py - toggling any addon
/// (scenery, aircraft, livery) on/off by moving its folder between
/// Community and a disabled-holding location. Works purely on folder
/// name; content_type doesn't matter here.
/// </summary>
public static class PackageMover
{
    public readonly record struct PendingMove(string FolderName, string? Src, string Dst);

    public readonly record struct MoveResult(string FolderName, bool Ok, string? Error);

    public readonly record struct SpaceShortfall(string Drive, long NeededBytes, long FreeBytes);

    private static bool SameDrive(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static long DirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>
    /// Moves src to dst. On the same drive this is an instant rename
    /// (progressCallback is called once at 100%). Across drives, renaming
    /// isn't possible - copies file by file, reporting progress as it
    /// goes; the source is deleted only AFTER the whole copy succeeds.
    /// </summary>
    public static void MovePackage(string src, string dst, Action<long, long>? progressCallback = null)
    {
        if (SameDrive(src, dst))
        {
            Directory.Move(src, dst);
            progressCallback?.Invoke(1, 1);
            return;
        }

        var totalBytes = Math.Max(DirectorySize(src), 1);
        long copiedBytes = 0;

        void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
            foreach (var file in Directory.EnumerateFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile);
                try { copiedBytes += new FileInfo(file).Length; } catch (IOException) { }
                progressCallback?.Invoke(copiedBytes, totalBytes);
            }
        }

        CopyDirectory(src, dst);
        Directory.Delete(src, recursive: true);
    }

    /// <summary>
    /// Figures out which folders actually need to move and where - shared
    /// by ApplyPackageChanges and EstimateApplySpace, so the two always
    /// agree on exactly what an Apply is about to do.
    ///
    /// disabledPaths: ordered list from DisabledLocations.ResolveDisabledLocations
    /// - enabling searches the WHOLE list (a package can sit in an old or
    /// the currently-configured location), disabling always targets
    /// disabledPaths[0] (the current primary location).
    /// </summary>
    private static List<PendingMove> ResolvePendingMoves(
        string communityPath, IReadOnlyList<string> disabledPaths, IReadOnlyDictionary<string, bool> desiredStates)
    {
        var primaryDisabled = disabledPaths[0];
        var pending = new List<PendingMove>();

        foreach (var (folderName, desiredEnabled) in desiredStates)
        {
            var communityTarget = Path.Combine(communityPath, folderName);
            var currentlyInCommunity = Directory.Exists(communityTarget);

            string? currentDisabledPath = null;
            foreach (var disabledBase in disabledPaths)
            {
                var candidate = Path.Combine(disabledBase, folderName);
                if (Directory.Exists(candidate))
                {
                    currentDisabledPath = candidate;
                    break;
                }
            }

            if (desiredEnabled && currentlyInCommunity) continue;
            if (!desiredEnabled && currentDisabledPath != null) continue;

            string? src;
            string dst;
            if (desiredEnabled)
            {
                src = currentDisabledPath;
                dst = communityTarget;
            }
            else
            {
                src = communityTarget;
                dst = Path.Combine(primaryDisabled, folderName);
            }

            pending.Add(new PendingMove(folderName, src, dst));
        }
        return pending;
    }

    /// <summary>
    /// Preflight for ApplyPackageChanges: a same-drive move is an instant
    /// rename and never needs extra room, but a cross-drive one is a real
    /// file-by-file copy - if the destination drive runs out of space
    /// partway through, checking upfront lets the caller refuse the whole
    /// Apply before touching a single file.
    /// </summary>
    /// <summary>
    /// sameDriveOverride/directorySizeOverride/freeSpaceOverride let tests
    /// simulate a cross-drive move and a nearly-full destination drive
    /// without needing actual separate physical drives - the direct C#
    /// equivalent of the Python test suite's monkeypatching of
    /// _same_drive/_directory_size/shutil.disk_usage for this function.
    /// Production callers omit them and get the real filesystem checks.
    /// </summary>
    public static List<SpaceShortfall> EstimateApplySpace(
        string communityPath, IReadOnlyList<string> disabledPaths, IReadOnlyDictionary<string, bool> desiredStates,
        Func<string, string, bool>? sameDriveOverride = null,
        Func<string, long>? directorySizeOverride = null,
        Func<string, long>? freeSpaceOverride = null)
    {
        var isSameDrive = sameDriveOverride ?? SameDrive;
        var getDirectorySize = directorySizeOverride ?? DirectorySize;
        var getFreeSpace = freeSpaceOverride ?? (drive => new DriveInfo(drive).AvailableFreeSpace);

        var neededPerDrive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in ResolvePendingMoves(communityPath, disabledPaths, desiredStates))
        {
            if (move.Src is null || !Directory.Exists(move.Src) || isSameDrive(move.Src, move.Dst)) continue;
            var drive = Path.GetPathRoot(Path.GetFullPath(move.Dst)) ?? "";
            neededPerDrive[drive] = neededPerDrive.GetValueOrDefault(drive) + getDirectorySize(move.Src);
        }

        var shortfalls = new List<SpaceShortfall>();
        foreach (var (drive, neededBytes) in neededPerDrive)
        {
            long freeBytes;
            try
            {
                freeBytes = getFreeSpace(drive);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or DriveNotFoundException)
            {
                continue;
            }
            if (neededBytes > freeBytes)
            {
                shortfalls.Add(new SpaceShortfall(drive, neededBytes, freeBytes));
            }
        }
        return shortfalls;
    }

    /// <summary>
    /// desiredStates: {folderName: wantEnabled}. Moves only what differs
    /// from the current location. Never deletes, never overwrites an
    /// existing destination. Works for any content type (scenery,
    /// aircraft, liveries) - only cares about the folder name.
    ///
    /// progressCallback(itemIndex, itemCount, folderName, copiedBytes,
    /// totalBytes), called throughout each move (once at 100% for a
    /// same-drive move) - for a UI progress bar.
    ///
    /// Caller is expected to have already checked EstimateApplySpace -
    /// this method does not check free space itself.
    /// </summary>
    public static List<MoveResult> ApplyPackageChanges(
        string communityPath, IReadOnlyList<string> disabledPaths, IReadOnlyDictionary<string, bool> desiredStates,
        Action<int, int, string, long, long>? progressCallback = null)
    {
        var pending = ResolvePendingMoves(communityPath, disabledPaths, desiredStates);
        var results = new List<MoveResult>();
        var itemCount = pending.Count;

        for (var index = 0; index < pending.Count; index++)
        {
            var (folderName, src, dst) = pending[index];
            if (src is null || !Directory.Exists(src))
            {
                results.Add(new MoveResult(folderName, false, "source not found"));
                continue;
            }
            if (Directory.Exists(dst) || File.Exists(dst))
            {
                results.Add(new MoveResult(folderName, false, "destination already exists"));
                continue;
            }

            var capturedIndex = index;
            try
            {
                MovePackage(src, dst, (copied, total) => progressCallback?.Invoke(capturedIndex, itemCount, folderName, copied, total));
                results.Add(new MoveResult(folderName, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new MoveResult(folderName, false, ex.Message));
            }
        }
        return results;
    }
}
