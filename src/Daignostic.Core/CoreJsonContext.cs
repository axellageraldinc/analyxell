using System.Text.Json.Serialization;
using Daignostic.Core.Config;
using Daignostic.Core.Settings;

namespace Daignostic.Core;

/// <summary>Source-generated JSON metadata so serialization stays safe under trimming and Native AOT.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GameProfile))]
[JsonSerializable(typeof(ExperimentList))]
[JsonSerializable(typeof(CaptureSettingsSnapshot))]
[JsonSerializable(typeof(UserSetupData))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
