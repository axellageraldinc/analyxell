using System.Text.Json.Serialization;
using Analyxell.Core.Config;
using Analyxell.Core.Settings;

namespace Analyxell.Core;

/// <summary>Source-generated JSON metadata so serialization stays safe under trimming and Native AOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GameProfile))]
[JsonSerializable(typeof(ExperimentList))]
[JsonSerializable(typeof(CaptureSettingsSnapshot))]
[JsonSerializable(typeof(UserSetupData))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
