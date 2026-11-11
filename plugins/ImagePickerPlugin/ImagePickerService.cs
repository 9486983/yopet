namespace ImagePickerPlugin;

internal sealed record FolderSortResult(int MovedCount, IReadOnlyList<string> FolderNames);

internal sealed record WaitMoveResult(string JpgTarget, IReadOnlyList<string> RawTargets, string WaitDir);

internal static class ImagePickerService
{
    private static readonly HashSet<string> JpgExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg",
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3fr", ".arw", ".cr2", ".cr3", ".dng", ".erf", ".kdc", ".mrw",
        ".nef", ".nrw", ".orf", ".pef", ".raf", ".rw2", ".srw", ".x3f",
    };

    public static bool IsJpg(string path)
        => JpgExtensions.Contains(Path.GetExtension(path));

    public static bool IsRaw(string path)
        => RawExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// 判断文件夹是否已经按后缀整理过。
    /// 依据内部是否已有按后缀命名的子文件夹（或 WAIT 目录）来判定，
    /// 用于再次拖入同一文件夹时跳过归档阶段，直接进入常驻。
    /// </summary>
    public static bool IsSorted(string folder)
    {
        if (!Directory.Exists(folder))
            return false;

        foreach (var sub in Directory.GetDirectories(folder, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(sub);

            if (name.Equals("WAIT", StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.Equals("NO_EXT", StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsExtensionFolderName(name) && ContainsFilesWithExtension(sub, name))
                return true;
        }

        return false;
    }

    private static bool IsExtensionFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 10)
            return false;

        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    private static bool ContainsFilesWithExtension(string directory, string extensionName)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                var extension = Path.GetExtension(file).TrimStart('.');
                if (extension.Equals(extensionName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
        }

        return false;
    }

    public static FolderSortResult SortByExtension(string folder, CancellationToken token)
    {
        var folderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moved = 0;

        foreach (var file in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(file);
            var folderName = string.IsNullOrEmpty(extension)
                ? "NO_EXT"
                : extension.TrimStart('.').ToUpperInvariant();
            var targetDir = Path.Combine(folder, folderName);

            if (PathsEqual(Path.GetDirectoryName(file), targetDir))
                continue;

            Directory.CreateDirectory(targetDir);
            var target = GetUniquePath(Path.Combine(targetDir, Path.GetFileName(file)));
            File.Move(file, target);

            moved++;
            folderNames.Add(folderName);
        }

        return new FolderSortResult(moved, folderNames.OrderBy(n => n).ToList());
    }

    public static WaitMoveResult MoveJpgToWait(string jpgPath, CancellationToken token)
    {
        var sourceDir = Path.GetDirectoryName(jpgPath)
            ?? throw new InvalidOperationException("Cannot resolve the JPG directory.");
        var waitParent = GetWaitParent(sourceDir);
        var waitDir = Path.Combine(waitParent, "WAIT");
        var jpgDir = Path.Combine(waitDir, "JPG");
        var rawDir = Path.Combine(waitDir, "RAW");

        Directory.CreateDirectory(jpgDir);
        Directory.CreateDirectory(rawDir);

        token.ThrowIfCancellationRequested();
        var jpgTarget = GetUniquePath(Path.Combine(jpgDir, Path.GetFileName(jpgPath)));
        File.Move(jpgPath, jpgTarget);

        var stem = Path.GetFileNameWithoutExtension(jpgPath);
        var rawTargets = new List<string>();

        foreach (var rawFile in FindRawFiles(sourceDir, waitDir, stem, token))
        {
            token.ThrowIfCancellationRequested();
            var rawTarget = GetUniquePath(Path.Combine(rawDir, Path.GetFileName(rawFile)));
            File.Move(rawFile, rawTarget);
            rawTargets.Add(rawTarget);
        }

        return new WaitMoveResult(jpgTarget, rawTargets, waitDir);
    }

    private static string GetWaitParent(string sourceDir)
    {
        var name = Path.GetFileName(sourceDir);
        if (!name.Equals("JPG", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("JPEG", StringComparison.OrdinalIgnoreCase))
            return sourceDir;

        return Directory.GetParent(sourceDir)?.FullName ?? sourceDir;
    }

    private static IEnumerable<string> FindRawFiles(
        string sourceDir,
        string waitDir,
        string stem,
        CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetSearchRoots(sourceDir))
        {
            token.ThrowIfCancellationRequested();

            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint,
                }).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();

                if (IsUnder(file, waitDir))
                    continue;
                if (!IsRaw(file))
                    continue;
                if (!Path.GetFileNameWithoutExtension(file).Equals(stem, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(file))
                    yield return file;
            }
        }
    }

    private static IEnumerable<string> GetSearchRoots(string sourceDir)
    {
        yield return sourceDir;

        var parent = Directory.GetParent(sourceDir)?.FullName;
        if (parent != null && !PathsEqual(parent, sourceDir))
            yield return parent;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Cannot resolve the target directory.");
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}_{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static bool IsUnder(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left == null || right == null)
            return false;
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }
}
