using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Hebner.Agent.Tray;

public static class EnrollmentTokenStore
{
    private static string BaseDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "HebnerRemoteSupport");
    private static string TokenPath => Path.Combine(BaseDir, "enrollment-token.txt");

    private static readonly Regex TokenRegex = new("^[A-Z0-9]{4}(-[A-Z0-9]{4}){3}$", RegexOptions.Compiled);

    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var t = File.ReadAllText(TokenPath).Trim();
            return string.IsNullOrEmpty(t) ? null : t;
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveToken(string token)
    {
        try
        {
            var dir = BaseDir;
            Directory.CreateDirectory(dir);
            File.WriteAllText(TokenPath, token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim().ToUpperInvariant();
        return TokenRegex.IsMatch(token);
    }

    public static string MaskToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return "Not enrolled";
        // token format: AAAA-BBBB-CCCC-DDDD
        var parts = token.Split('-');
        if (parts.Length != 4) return "(invalid)";
        return $"{parts[0]}-****-****-{parts[3]}";
    }
}
