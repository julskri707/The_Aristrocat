using System.IO;
using UnityEngine;

/// <summary>NDJSON debug ingest for Cursor session 243ebf — remove after verification.</summary>
internal static class DebugSessionAgentLog
{
    const string FileName = "debug-243ebf.log";
    static bool _bootLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EmitBootLog()
    {
        if (_bootLogged)
            return;

        _bootLogged = true;
#region agent log
        Write(
            "H0",
            "DebugSessionAgentLog.EmitBootLog",
            "boot",
            "{\"assetsPath\":\"" + Application.dataPath.Replace("\\", "/").Replace("\"", "'") + "\"}");
#endregion
    }

    public static void Write(string hypothesisId, string location, string message, string dataJsonObject)
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
            long ts = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string loc = (location ?? "").Replace("\\", "/").Replace("\"", "'");
            string msg = (message ?? "").Replace("\"", "'");
            string line = "{\"sessionId\":\"243ebf\",\"hypothesisId\":\"" + hypothesisId +
                          "\",\"location\":\"" + loc + "\",\"message\":\"" + msg +
                          "\",\"data\":" + (string.IsNullOrEmpty(dataJsonObject) ? "{}" : dataJsonObject) +
                          ",\"timestamp\":" + ts + "}\n";
            File.AppendAllText(path, line);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[DebugSessionAgentLog] write failed: " + ex.Message);
        }
    }
}
