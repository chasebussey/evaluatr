using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Graders;

// Centralizes the file-check boundary. This is not an OS sandbox: another process
// concurrently replacing paths can race inspection and opening a file.
internal static class WorkspaceFile
{
    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || System.IO.Path.IsPathRooted(path)
            || path.Contains(':') || path.Contains('\\') || path.Contains('\0'))
        {
            throw new EvalSuiteValidationException("Assertion 'path' must be a workspace-relative path using '/' separators.");
        }

        var depth = 0;
        foreach (var part in path.Split('/'))
        {
            if (part == "..")
            {
                if (--depth < 0)
                {
                    throw new EvalSuiteValidationException("Assertion 'path' must not escape the workspace.");
                }
            }
            else if (part is not "" and not ".")
            {
                depth++;
            }
        }
    }

    public static string Resolve(string workspace, string path)
    {
        ValidatePath(path);
        var root = ResolveRoot(System.IO.Path.GetFullPath(workspace));
        var linksFollowed = 0;
        return Walk(root, root, path.Replace('/', System.IO.Path.DirectorySeparatorChar), ref linksFollowed);
    }

    private static string Walk(string root, string current, string path, ref int linksFollowed)
    {
        // Walk original components, including link targets. Normalizing '..' first
        // could conceal an escape through an intermediate directory symlink.
        foreach (var part in path.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.GetFullPath(System.IO.Path.Combine(current, part));
            EnsureInside(root, current);

            var target = new FileInfo(current).LinkTarget;
            if (target is null)
            {
                continue;
            }

            if (++linksFollowed > 40)
            {
                throw new IOException("Too many symbolic links in assertion path.");
            }

            var parent = System.IO.Path.GetDirectoryName(current)!;
            if (System.IO.Path.IsPathRooted(target))
            {
                EnsureInside(root, target);
                parent = root;
                target = target[root.Length..].TrimStart(System.IO.Path.DirectorySeparatorChar);
            }

            current = Walk(root, parent, target, ref linksFollowed);
        }

        return current;
    }

    public static bool IsFile(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ResolveRoot(string path)
    {
        var filesystemRoot = System.IO.Path.GetPathRoot(path)!;
        var linksFollowed = 0;
        var resolved = Walk(filesystemRoot, filesystemRoot, path[filesystemRoot.Length..], ref linksFollowed);

        return System.IO.Path.TrimEndingDirectorySeparator(resolved);
    }

    private static void EnsureInside(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = System.IO.Path.EndsInDirectorySeparator(root) ? root : root + System.IO.Path.DirectorySeparatorChar;
        if (!path.Equals(root, comparison) && !path.StartsWith(prefix, comparison))
        {
            throw new IOException("Assertion path resolves outside the eval workspace.");
        }
    }
}
