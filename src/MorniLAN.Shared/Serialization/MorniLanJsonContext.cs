using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Shared.Models;

namespace MorniLAN.Shared.Serialization;

/// <summary>
/// Source-generierter JSON-Kontext für alle Nachrichten zwischen den Komponenten
/// (schnell, trimming-sicher, keine Reflection).
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppEntry))]
[JsonSerializable(typeof(List<AppEntry>))]
[JsonSerializable(typeof(ApprovalRule))]
[JsonSerializable(typeof(List<ApprovalRule>))]
[JsonSerializable(typeof(DeviceInfo))]
[JsonSerializable(typeof(DeviceStatus))]
[JsonSerializable(typeof(AdminCommand))]
[JsonSerializable(typeof(CommandResult))]
public sealed partial class MorniLanJsonContext : JsonSerializerContext;
