using System;
using BepInEx;
using BepInEx.Logging;
using DeployableBarbedWire.Client.BarbedWire;

namespace DeployableBarbedWire.Client;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", "4.1.6")]
public sealed class DeployableBarbedWirePlugin : BaseUnityPlugin
{
    internal const string PluginGuid = "com.senze.deployablebarbedwire";
    internal const string PluginName = "Deployable Barbed Wire";
    internal const string PluginVersion = "1.0.0";
    private const string LegacyModuleTypeName = "SPTEssentials.Client.BarbedWire.DeployableBarbedWireModule";

    internal static ManualLogSource Log { get; private set; }
    internal static DeployableBarbedWireConfig Settings { get; private set; }

    private DeployableBarbedWireModule _barbedWire;
    private bool _legacyConflictReported;

    private void Awake()
    {
        Log = Logger;
        if (LegacyEssentialsContainsModule())
        {
            ReportLegacyConflict();
            return;
        }

        Settings = new DeployableBarbedWireConfig(Config);
        _barbedWire = new DeployableBarbedWireModule();

        try
        {
            _barbedWire.Enable();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded for SPT 4.1.6 through 4.2.0.");
        }
        catch (Exception exception)
        {
            Log.LogError($"Initialization failed; the plugin has been disabled: {exception}");
            _barbedWire.Shutdown();
            _barbedWire = null;
        }
    }

    private void Update()
    {
        if (_barbedWire != null && LegacyEssentialsContainsModule())
        {
            _barbedWire.Shutdown();
            _barbedWire = null;
            ReportLegacyConflict();
            return;
        }

        _barbedWire?.Update();
    }

    private void OnGUI()
    {
        _barbedWire?.OnGui();
    }

    private void OnDestroy()
    {
        _barbedWire?.Shutdown();
        _barbedWire = null;
        Settings = null;
        Log = null;
    }

    private void ReportLegacyConflict()
    {
        if (_legacyConflictReported)
        {
            return;
        }

        _legacyConflictReported = true;
        Log.LogError("The installed SPT Essentials build still contains Deployable Barbed Wire. The standalone client component has been disabled.");
    }

    private static bool LegacyEssentialsContainsModule()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(LegacyModuleTypeName, false) != null)
            {
                return true;
            }
        }

        return false;
    }
}
