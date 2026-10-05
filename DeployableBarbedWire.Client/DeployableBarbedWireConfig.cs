using BepInEx.Configuration;
using UnityEngine;

namespace DeployableBarbedWire.Client;

internal sealed class DeployableBarbedWireConfig
{
    internal ConfigEntry<bool> Enabled { get; }
    internal ConfigEntry<KeyboardShortcut> PlacementShortcut { get; }

    internal DeployableBarbedWireConfig(ConfigFile config)
    {
        Enabled = config.Bind(
            "01 - General",
            "Enabled",
            true,
            "Enables Deployable Barbed Wire. Restart SPT after changing this option."
        );
        PlacementShortcut = config.Bind(
            "02 - Controls",
            "Place Barbed Wire",
            new KeyboardShortcut(KeyCode.Insert),
            "Press once to enter placement mode and again to deploy the wire. Use the mouse wheel to rotate it and Escape to cancel."
        );
    }
}
