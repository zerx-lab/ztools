using System.Collections.Generic;
using System.Text.Json.Serialization;
using ztools.Models;

namespace ztools.Services;

/// <summary>
/// Source-generated JSON serialization context.
/// Avoids reflection-based System.Text.Json so the app stays trimming/AOT
/// compatible and skips runtime metadata generation cost.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class AppJsonContext : JsonSerializerContext
{
}
