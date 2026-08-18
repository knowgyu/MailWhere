using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace MailWhere.Windows;

internal enum SkillInstallTarget
{
    Codex,
    Claude
}

internal sealed record SkillInstallPlan(bool CanInstall, bool RequiresOverwritePrompt, string SourcePath, string TargetPath, string StatusCode);

internal sealed record SkillInstallResult(bool Installed, string TargetPath, string StatusCode);

internal static class MailWhereSkillInstaller
{
    public static string GetTargetPath(SkillInstallTarget target) =>
        TryGetExpectedTarget(target, out var targetPath, out _) ? targetPath : string.Empty;

    public static SkillInstallPlan PlanInstall(string portableRoot, SkillInstallTarget target)
    {
        var source = Path.GetFullPath(Path.Combine(portableRoot, "skills", "mailwhere"));
        if (!TryGetExpectedTarget(target, out var destination, out var root))
        {
            return new SkillInstallPlan(false, false, source, destination, "invalid-user-profile");
        }

        var validation = ValidatePaths(source, destination, root);
        if (validation is not null)
        {
            return new SkillInstallPlan(false, false, source, destination, validation);
        }

        if (!Directory.Exists(source))
        {
            return new SkillInstallPlan(false, false, source, destination, "bundled-skill-not-found");
        }

        if (!Directory.Exists(destination))
        {
            return new SkillInstallPlan(true, false, source, destination, "missing");
        }

        return DirectoriesMatch(source, destination)
            ? new SkillInstallPlan(true, false, source, destination, "current")
            : new SkillInstallPlan(true, true, source, destination, "user-modified");
    }

    public static SkillInstallResult InstallBundledSkill(string portableRoot, SkillInstallTarget target, bool overwrite)
    {
        var plan = PlanInstall(portableRoot, target);
        if (!plan.CanInstall)
        {
            return new SkillInstallResult(false, plan.TargetPath, plan.StatusCode);
        }

        if (Directory.Exists(plan.TargetPath))
        {
            if (plan.RequiresOverwritePrompt && !overwrite)
            {
                return new SkillInstallResult(false, plan.TargetPath, "preserved-existing-skill");
            }

            if (ContainsReparsePoint(plan.TargetPath))
            {
                return new SkillInstallResult(false, plan.TargetPath, "unsafe-reparse-point-target");
            }

            FileSystem.DeleteDirectory(plan.TargetPath, DeleteDirectoryOption.DeleteAllContents);
        }

        CopyDirectory(plan.SourcePath, plan.TargetPath);
        return new SkillInstallResult(true, plan.TargetPath, plan.StatusCode == "current" ? "repaired" : "installed");
    }

    private static bool TryGetExpectedTarget(SkillInstallTarget target, out string targetPath, out string skillRoot)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile) || !Path.IsPathFullyQualified(profile))
        {
            targetPath = string.Empty;
            skillRoot = string.Empty;
            return false;
        }

        var profileRoot = Path.GetFullPath(profile);
        skillRoot = Path.GetFullPath(Path.Combine(profileRoot, target == SkillInstallTarget.Codex ? ".agents" : ".claude", "skills"));
        targetPath = Path.GetFullPath(Path.Combine(skillRoot, "mailwhere"));
        return IsUnder(targetPath, skillRoot) && string.Equals(Path.GetFileName(targetPath), "mailwhere", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidatePaths(string source, string destination, string skillRoot)
    {
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
        {
            return "source-target-conflict";
        }

        var expectedTarget = Path.GetFullPath(Path.Combine(skillRoot, "mailwhere"));
        if (!string.Equals(destination, expectedTarget, StringComparison.OrdinalIgnoreCase) || !IsUnder(destination, skillRoot))
        {
            return "invalid-skill-target";
        }

        return ContainsReparsePointOnPath(skillRoot, destination) || ContainsReparsePoint(destination)
            ? "unsafe-reparse-point-target"
            : null;
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsReparsePointOnPath(string root, string target)
    {
        var current = Path.GetFullPath(root);
        var fullTarget = Path.GetFullPath(target);
        while (IsUnder(fullTarget, current) || string.Equals(fullTarget, current, StringComparison.OrdinalIgnoreCase))
        {
            if (HasReparsePoint(current))
            {
                return true;
            }

            if (string.Equals(current, fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            current = Path.Combine(current, fullTarget[current.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]);
        }

        return false;
    }

    private static bool ContainsReparsePoint(string path)
    {
        if (!Directory.Exists(path))
        {
            return File.Exists(path) && HasReparsePoint(path);
        }

        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (HasReparsePoint(directory))
            {
                return true;
            }

            foreach (var childDirectory in Directory.EnumerateDirectories(directory))
            {
                pending.Push(childDirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (HasReparsePoint(file))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    }

    private static bool DirectoriesMatch(string source, string destination)
    {
        var sourceFiles = RelativeFiles(source).ToArray();
        var destinationFiles = RelativeFiles(destination).ToArray();
        if (!sourceFiles.SequenceEqual(destinationFiles, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var relativePath in sourceFiles)
        {
            if (!FileContentsMatch(Path.Combine(source, relativePath), Path.Combine(destination, relativePath)))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> RelativeFiles(string root) =>
        Directory.EnumerateFiles(root, "*", System.IO.SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static bool FileContentsMatch(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        return File.ReadAllBytes(left).SequenceEqual(File.ReadAllBytes(right));
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", System.IO.SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }
}
