using System.Text.Json;
using System.Text.Json.Serialization;
using MorniLAN.Shared.Connection;
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
[JsonSerializable(typeof(HelloRequest))]
[JsonSerializable(typeof(HelloResponse))]
[JsonSerializable(typeof(PairingRequest))]
[JsonSerializable(typeof(PairingApproval))]
[JsonSerializable(typeof(DiscoveryBeacon))]
[JsonSerializable(typeof(AgentLocalStatus))]
[JsonSerializable(typeof(InventoryReport))]
[JsonSerializable(typeof(AppEntry[]))]
[JsonSerializable(typeof(AppImage))]
[JsonSerializable(typeof(string[]))]
public sealed partial class MorniLanJsonContext : JsonSerializerContext;
