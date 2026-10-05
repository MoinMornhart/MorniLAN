using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Shared.Serialization;

namespace MorniLAN.Shared.Connection;

/// <summary>Gleiche JSON-Einstellungen für SignalR auf beiden Seiten (Agent und Admin-Panel).</summary>
public static class SignalRJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        options.TypeInfoResolverChain.Insert(0, MorniLanJsonContext.Default);
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}
