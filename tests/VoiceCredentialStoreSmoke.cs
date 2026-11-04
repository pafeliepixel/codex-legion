using System;
using System.IO;
using CodexNativeLite;

public static class VoiceCredentialStoreSmoke
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CodexLegion-VoiceVault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VoiceCredentialStore(root);
            if (store.HasKey) throw new Exception("A new voice vault unexpectedly contained a key.");
            store.Save("sk-test-not-a-real-key");
            if (!store.HasKey) throw new Exception("The protected voice key was not written.");
            if (store.Load() != "sk-test-not-a-real-key") throw new Exception("The voice key did not round-trip through DPAPI.");

            string protectedFile = Path.Combine(root, "voice-key.protected");
            string raw = Convert.ToBase64String(File.ReadAllBytes(protectedFile));
            if (raw.IndexOf("sk-test-not-a-real-key", StringComparison.Ordinal) >= 0)
                throw new Exception("The voice key was stored as plaintext.");

            store.Clear();
            if (store.HasKey) throw new Exception("The voice key was not removed.");
            Console.WriteLine("Voice credential vault smoke test passed.");
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
