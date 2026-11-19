using System.Text.Encodings.Web;
using System.Text.Json;

namespace DesktopOrbit.Core.Config;

public static class ConfigStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopOrbit");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "config.json");

    public static AppConfig LoadOrCreate(string path, out bool created)
    {
        created = false;
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (!File.Exists(path))
        {
            AppConfig defaults = AppConfig.CreateDefault();
            Save(path, defaults);
            created = true;
            return defaults;
        }

        try
        {
            string json = File.ReadAllText(path);
            AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return config ?? AppConfig.CreateDefault();
        }
        catch (Exception)
        {
            try
            {
                File.Copy(path, path + ".broken", overwrite: true);
            }
            catch (IOException)
            {
            }

            return AppConfig.CreateDefault();
        }
    }

    public static void Save(string path, AppConfig config)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(config, JsonOptions);
        string temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class ConfigHolder
{
    private AppConfig _current;

    public ConfigHolder(AppConfig config)
    {
        _current = config;
    }

    public AppConfig Current => Volatile.Read(ref _current);

    public event Action<AppConfig>? Changed;

    public void Update(AppConfig config)
    {
        Volatile.Write(ref _current, config);
        Changed?.Invoke(config);
    }
}
