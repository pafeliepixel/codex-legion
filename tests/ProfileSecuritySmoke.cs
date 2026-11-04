using System;
using System.IO;
using System.Text;
using CodexNativeLite;

public static class ProfileSecuritySmoke
{
    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CodexNativeLite-SecuritySmoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var security = new ProfileSecurity(root);
            security.SetPin("2468");
            if (!security.HasPin) throw new Exception("PIN record was not created.");
            if (security.VerifyPin("1357")) throw new Exception("Incorrect PIN was accepted.");
            if (!security.VerifyPin("2468")) throw new Exception("Correct PIN was rejected.");

            string auth = Path.Combine(security.ProfileDirectory, "auth.json");
            File.WriteAllText(auth, "{\"test\":true}", Encoding.UTF8);
            security.SealCredentials();
            if (File.Exists(auth)) throw new Exception("Plaintext auth file remained after sealing.");
            if (!security.HasRememberedCredentials) throw new Exception("Protected credential vault was not created.");

            security.UnsealCredentials();
            if (!File.Exists(auth)) throw new Exception("Credential vault did not unseal.");
            if (File.ReadAllText(auth, Encoding.UTF8).IndexOf("test", StringComparison.Ordinal) < 0)
                throw new Exception("Unsealed credential contents were incorrect.");

            security.ResetPinAndCredentials();
            if (security.HasPin || security.HasRememberedCredentials)
                throw new Exception("Security reset left PIN or credential data behind.");

            Console.WriteLine("Profile security smoke test passed.");
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
