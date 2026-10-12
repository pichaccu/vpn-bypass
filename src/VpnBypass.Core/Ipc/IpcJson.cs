using System.Text.Json;
using System.Text.Json.Serialization;
using VpnBypass.Core.Config;

namespace VpnBypass.Core.Ipc;

// Forrásgenerált (AOT-barát) JSON-kontextus az IPC-üzenetekhez és a konfighoz.
// A property-nevek camelCase-ek, az enumok pedig olvasható sztringként szerializálódnak.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IpcRequest))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(AppsResponse))]
[JsonSerializable(typeof(IpcResult))]
[JsonSerializable(typeof(AppConfig))]
public partial class IpcJsonContext : JsonSerializerContext
{
}

// Vékony segéd a sorosításhoz. Minden üzenet egyetlen sor (nincs behúzás).
public static class IpcJson
{
    public static string Serialize(IpcRequest request) =>
        JsonSerializer.Serialize(request, IpcJsonContext.Default.IpcRequest);

    public static IpcRequest? DeserializeRequest(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.IpcRequest);

    public static string Serialize(StatusResponse response) =>
        JsonSerializer.Serialize(response, IpcJsonContext.Default.StatusResponse);

    public static StatusResponse? DeserializeStatus(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.StatusResponse);

    public static string Serialize(AppsResponse response) =>
        JsonSerializer.Serialize(response, IpcJsonContext.Default.AppsResponse);

    public static AppsResponse? DeserializeApps(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.AppsResponse);

    public static string Serialize(IpcResult result) =>
        JsonSerializer.Serialize(result, IpcJsonContext.Default.IpcResult);

    public static IpcResult? DeserializeResult(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.IpcResult);

    public static string Serialize(AppConfig config) =>
        JsonSerializer.Serialize(config, IpcJsonContext.Default.AppConfig);

    public static AppConfig? DeserializeConfig(string json) =>
        JsonSerializer.Deserialize(json, IpcJsonContext.Default.AppConfig);
}
