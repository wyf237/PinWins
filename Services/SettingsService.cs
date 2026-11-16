using System;
using System.IO;
using System.Text.Json;
using PinWins.Models;

namespace PinWins.Services;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinWins");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings != null)
                    return settings;
            }
        }
        catch
        {
            // 忽略读取异常，回退到默认配置。
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 忽略写入异常。
        }
    }
}
