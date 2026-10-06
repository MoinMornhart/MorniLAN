using System.Text.Json;

namespace MorniLAN.Agent;

/// <summary>
/// Schreibt die externen Agent-Einstellungen (agent-settings.json), die auch das Geräte-Setup anlegt. Liegt außerhalb
/// des Programmordners, damit sie Updates übersteht. Aktuell nur die Adresse des Admin-PCs. Gleiches Format wie der
/// Installer: { "MorniLAN": { "Connection": { "AdminHost": "…" } } }.
/// </summary>
internal static class AgentSettingsFile
{
    public static string PathIn(string dataDirectory) => System.IO.Path.Combine(dataDirectory, "agent-settings.json");

    public static void WriteAdminHost(string dataDirectory, string host)
    {
        Directory.CreateDirectory(dataDirectory);
        // JsonEncodedText escapt sicher (ohne Reflection/Trimming-Warnung); Anführungszeichen setzen wir selbst
        var encoded = JsonEncodedText.Encode(host);
        var json = $$"""{ "MorniLAN": { "Connection": { "AdminHost": "{{encoded}}" } } }""";
        File.WriteAllText(PathIn(dataDirectory), json);
    }
}
