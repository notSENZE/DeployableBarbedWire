using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;

namespace DeployableBarbedWire.Server;

[Injectable(InjectionType.Singleton)]
public sealed class DeployableBarbedWireServerConfig
{
    private const string ConfigFileName = "com.senze.deployablebarbedwire.cfg";

    public bool Enabled { get; private set; } = true;

    public DeployableBarbedWireServerConfig(ISptLogger<DeployableBarbedWireServerConfig> logger)
    {
        var configPath = FindConfigPath();
        if (configPath is null)
        {
            logger.Info($"{ModInfo.LogPrefix} {ConfigFileName} was not found yet. The mod will use its default settings.");
            return;
        }

        try
        {
            foreach (var sourceLine in File.ReadLines(configPath))
            {
                var line = sourceLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (name.Equals("Enabled", StringComparison.OrdinalIgnoreCase)
                    && bool.TryParse(value, out var enabled))
                {
                    Enabled = enabled;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"{ModInfo.LogPrefix} Could not read {ConfigFileName}: {exception.Message}. The mod will use its default settings.");
        }
    }

    private static string? FindConfigPath()
    {
        var modDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var current = string.IsNullOrWhiteSpace(modDirectory)
            ? null
            : new DirectoryInfo(modDirectory);

        while (current is not null)
        {
            var bepinexDirectory = Path.Combine(current.FullName, "BepInEx");
            if (Directory.Exists(bepinexDirectory))
            {
                var configPath = Path.Combine(bepinexDirectory, "config", ConfigFileName);
                return File.Exists(configPath) ? configPath : null;
            }

            current = current.Parent;
        }

        return null;
    }
}
