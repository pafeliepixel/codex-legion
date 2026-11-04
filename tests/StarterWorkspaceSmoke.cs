using System;
using System.IO;
using CodexNativeLite;

public static class StarterWorkspaceSmoke
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CodexLegion-StarterWorkspace-" + Guid.NewGuid().ToString("N"));
        try
        {
            string first = StarterWorkspace.Create(root);
            string second = StarterWorkspace.Create(root);
            if (!Directory.Exists(first) || !Directory.Exists(second))
                throw new Exception("Starter workspaces were not created.");
            if (String.Equals(first, second, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Starter workspaces were not unique.");
            if (!Path.GetFullPath(first).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                throw new Exception("A starter workspace escaped its root.");
            Console.WriteLine("Starter workspace smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
