using System.Text.Json.Serialization;
using Daignostic.Core.Settings;

namespace Daignostic.Core;

/// <summary>Source-generated JSON metadata so serialization stays safe under trimming and Native AOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
