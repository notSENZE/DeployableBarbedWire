using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.Communications;
using EFT.InputSystem;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using EFT.UI.Screens;
using HarmonyLib;
using UnityEngine;

namespace DeployableBarbedWire.Client.BarbedWire;

internal sealed class DeployableBarbedWireModule
{
    internal const string KitTemplateId = "6aa4e71b2f4c8d0935e6a201";

    private const int MaximumKits = 2;
    private const float PlacementDistance = 2.5f;
    private const float PlacementGroundClearance = 0.16f;
    private const float PlacementEndInset = 0.08f;
    private const float PlacementSideInset = 0.05f;
    private const float PickupDistance = 4f;
    private const float PickupSphereRadius = 0.18f;
    private const float PickupHoldDuration = 0.75f;

    private readonly List<PlacedBarbedWire> _placedWires = new();
    private readonly Harmony _harmony = new(DeployableBarbedWirePlugin.PluginGuid + ".runtime");

    private static DeployableBarbedWireModule _activeModule;

    private BarbedWireVisualAssets _visualAssets;
    private Mesh _wireMesh;
    private Material _placedMaterial;
    private Material _previewMaterial;
    private GameObject _preview;
    private bool _placementMode;
    private bool _validPlacement;
    private bool _inventoryOperationPending;
    private float _rotation;
    private float _pickupProgress;
    private PlacedBarbedWire _pickupTarget;
    private GUIStyle _labelStyle;
    private PlayerCameraController _playerCameraController;
    private PendingPlacement _pendingPlacement;
    private string _pendingPlacementError;

    internal void Enable()
    {
        _activeModule = this;
        _harmony.CreateClassProcessor(typeof(InputDispatchPatch)).Patch();
        try
        {
            _visualAssets = BarbedWireVisualAssets.Load();
            _wireMesh = _visualAssets.Mesh;
            _placedMaterial = _visualAssets.Material;
            DeployableBarbedWirePlugin.Log.LogInfo("Licensed PBR visual asset loaded.");
        }
        catch (Exception exception)
        {
            DeployableBarbedWirePlugin.Log.LogError($"PBR visual asset could not be loaded. Using the fallback mesh. {exception}");
            _wireMesh = BarbedWireMeshFactory.Create();
            _placedMaterial = CreateOpaqueMaterial(new Color(0.55f, 0.57f, 0.6f, 1f));
        }

        _previewMaterial = CreatePreviewMaterial(_placedMaterial, new Color(0.2f, 1f, 0.25f, 1f));
    }

    internal void Update()
    {
        if (!DeployableBarbedWirePlugin.Settings.Enabled.Value || !TryGetRaidPlayer(out var player))
        {
            LeavePlacementMode();
            ResetPickup();
            _pendingPlacement = null;
            _pendingPlacementError = null;
            return;
        }

        RemoveDestroyedWires();
        CompletePendingPlacement(player);

        if (_inventoryOperationPending || player.IsInventoryOpened)
        {
            ResetPickup();
            return;
        }

        if (_placementMode)
        {
            UpdatePlacement(player);
            return;
        }

        if (DeployableBarbedWirePlugin.Settings.PlacementShortcut.Value.IsDown())
        {
            EnterPlacementMode(player);
            return;
        }

        UpdatePickup(player);
    }

    internal void OnGui()
    {
        if (!_placementMode && _pickupTarget == null)
        {
            return;
        }

        _labelStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        var area = new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.72f, 600f, 70f);
        if (_placementMode)
        {
            var shortcut = DeployableBarbedWirePlugin.Settings.PlacementShortcut.Value.MainKey;
            GUI.Label(area, $"Mouse wheel: rotate   {shortcut}: deploy   Esc: cancel", _labelStyle);
            return;
        }

        if (_pickupProgress <= 0f)
        {
            GUI.Label(area, "Hold F to recover the Barbed Wire Kit", _labelStyle);
            return;
        }

        GUI.Label(area, $"Recovering Barbed Wire Kit… {Mathf.RoundToInt(_pickupProgress * 100f)}%", _labelStyle);
    }

    internal void Shutdown()
    {
        _harmony.UnpatchSelf();
        if (ReferenceEquals(_activeModule, this))
        {
            _activeModule = null;
        }

        LeavePlacementMode();
        foreach (var wire in _placedWires)
        {
            if (wire != null)
            {
                UnityEngine.Object.Destroy(wire.gameObject);
            }
        }

        _placedWires.Clear();
        if (_visualAssets != null)
        {
            _visualAssets.Dispose();
            _visualAssets = null;
            _wireMesh = null;
            _placedMaterial = null;
        }
        else if (_wireMesh != null)
        {
            UnityEngine.Object.Destroy(_wireMesh);
        }

        if (_placedMaterial != null)
        {
            UnityEngine.Object.Destroy(_placedMaterial);
        }

        if (_previewMaterial != null)
        {
            UnityEngine.Object.Destroy(_previewMaterial);
        }
    }

    private void EnterPlacementMode(Player player)
    {
        if (!HasKit(player))
        {
            Notify("You do not have a Barbed Wire Kit.");
            return;
        }

        _placementMode = true;
        _rotation = player.Transform.eulerAngles.y;
        _preview = CreateVisualObject("Deployable Barbed Wire Preview", _previewMaterial);
        UpdatePreviewColor(false);
    }

    private void UpdatePlacement(Player player)
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1) || !HasKit(player))
        {
            LeavePlacementMode();
            return;
        }

        _rotation += Input.mouseScrollDelta.y * 15f;
        _validPlacement = TryFindPlacement(player, out var position, out var rotation, out var hasSurface);
        if (_preview != null)
        {
            _preview.SetActive(hasSurface);
            if (hasSurface)
            {
                _preview.transform.SetPositionAndRotation(position, rotation);
            }
        }

        UpdatePreviewColor(_validPlacement);
        if (!DeployableBarbedWirePlugin.Settings.PlacementShortcut.Value.IsDown())
        {
            return;
        }

        if (!_validPlacement)
        {
            Notify("Barbed wire cannot be placed here.");
            return;
        }

        PlaceWire(player, position, rotation);
    }

    private bool TryFindPlacement(
        Player player,
        out Vector3 position,
        out Quaternion rotation,
        out bool hasSurface)
    {
        position = default;
        rotation = default;
        hasSurface = false;
        var camera = GetPlayerCamera(player);
        if (camera == null)
        {
            return false;
        }

        var forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.ProjectOnPlane(player.Transform.forward, Vector3.up);
        }

        forward.Normalize();
        var rayOrigin = player.Transform.position + forward * PlacementDistance + Vector3.up * 1.6f;
        if (!TryFindGround(player, rayOrigin, out var hit))
        {
            return false;
        }

        hasSurface = true;
        position = hit.point + hit.normal * 0.02f;
        rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, _rotation, 0f);
        if (Vector3.Dot(hit.normal, Vector3.up) < 0.72f)
        {
            return false;
        }

        var collisionHeight = BarbedWireMeshFactory.Height - PlacementGroundClearance;
        var colliders = Physics.OverlapBox(
            position + hit.normal * (PlacementGroundClearance + collisionHeight * 0.5f),
            new Vector3(
                BarbedWireMeshFactory.Length * 0.5f - PlacementEndInset,
                collisionHeight * 0.5f,
                BarbedWireMeshFactory.Depth * 0.5f - PlacementSideInset),
            rotation,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        foreach (var collider in colliders)
        {
            if (collider == null
                || ReferenceEquals(collider, hit.collider)
                || collider.transform.IsChildOf(player.Transform.Original))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void PlaceWire(Player player, Vector3 position, Quaternion rotation)
    {
        var kit = player.Inventory.GetAllItemByTemplate(KitTemplateId).FirstOrDefault();
        if (kit == null)
        {
            LeavePlacementMode();
            return;
        }

        var controller = player.InventoryController;
        var removeResult = ItemManipulator.Remove(kit, controller, true);
        if (removeResult.Failed)
        {
            DeployableBarbedWirePlugin.Log.LogError($"Kit removal failed: {removeResult.Error}");
            Notify("The Barbed Wire Kit could not be used. Please check the log.");
            return;
        }

        _inventoryOperationPending = true;
        LeavePlacementMode();
        try
        {
            var world = Singleton<GameWorld>.Instance;
            controller.RunNetworkTransaction(removeResult.Value, result =>
            {
                _inventoryOperationPending = false;
                if (result == null || result.Failed)
                {
                    _pendingPlacementError = result?.Error ?? "The inventory transaction returned no result.";
                    return;
                }

                _pendingPlacement = new PendingPlacement(world, player, position, rotation);
            });
        }
        catch (Exception exception)
        {
            _inventoryOperationPending = false;
            DeployableBarbedWirePlugin.Log.LogError($"Kit transaction could not start: {exception}");
            Notify("The Barbed Wire Kit could not be used. Please check the log.");
        }
    }

    private PlacedBarbedWire CreatePlacedWire(Vector3 position, Quaternion rotation)
    {
        var root = CreateVisualObject("Deployable Barbed Wire", _placedMaterial);
        try
        {
            root.SetActive(false);
            root.transform.SetPositionAndRotation(position, rotation);

            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, BarbedWireMeshFactory.Height * 0.5f, 0f);
            collider.size = new Vector3(
                BarbedWireMeshFactory.Length,
                BarbedWireMeshFactory.Height,
                BarbedWireMeshFactory.Depth);
            collider.isTrigger = true;

            var marker = root.AddComponent<PlacedBarbedWire>();
            var damage = root.AddComponent<DeployableBarbedWireDamage>();
            damage._soundBank = FindNativeBarbedWireSoundBank();
            root.SetActive(true);
            return marker;
        }
        catch
        {
            UnityEngine.Object.Destroy(root);
            throw;
        }
    }

    private void UpdatePickup(Player player)
    {
        var camera = GetPlayerCamera(player);
        if (camera == null || !TryFindPickupTarget(player, camera, out var target))
        {
            ResetPickup();
            return;
        }

        if (!ReferenceEquals(_pickupTarget, target))
        {
            _pickupTarget = target;
            _pickupProgress = 0f;
        }

        if (!Input.GetKey(KeyCode.F))
        {
            _pickupProgress = 0f;
            return;
        }

        _pickupProgress += Time.unscaledDeltaTime / PickupHoldDuration;
        if (_pickupProgress < 1f)
        {
            return;
        }

        ResetPickup();
        _ = PickupWireAsync(target, player);
    }

    private async Task PickupWireAsync(PlacedBarbedWire wire, Player player)
    {
        if (wire == null || CountKits(player) >= MaximumKits)
        {
            Notify("You can carry no more than two Barbed Wire Kits.");
            return;
        }

        _inventoryOperationPending = true;
        try
        {
            var controller = player.InventoryController;
            var kit = Singleton<ItemFactory>.Instance.CreateItem(controller.NextId.ToString(), KitTemplateId, null);
            kit.SpawnedInSession = false;
            await ChangeItemsOperation.LoadBundles(kit);

            if (!TryFindInventoryAddress(player, kit, out var address))
            {
                Notify("No room for the Barbed Wire Kit. The wire was left in place.");
                return;
            }

            var addResult = ItemManipulator.Add(kit, address, controller, false);
            if (addResult.Failed)
            {
                DeployableBarbedWirePlugin.Log.LogError($"Kit pickup failed: {addResult.Error}");
                Notify("The Barbed Wire Kit could not be recovered. The wire was left in place.");
                return;
            }

            addResult.Value.RaiseEvents(controller, CommandStatus.Begin);
            addResult.Value.RaiseEvents(controller, CommandStatus.Succeed);
            _placedWires.Remove(wire);
            if (wire != null)
            {
                UnityEngine.Object.Destroy(wire.gameObject);
            }

            Notify("Barbed Wire Kit recovered.");
        }
        catch (Exception exception)
        {
            DeployableBarbedWirePlugin.Log.LogError($"Pickup interrupted: {exception}");
            Notify("The Barbed Wire Kit could not be recovered. The wire was left in place.");
        }
        finally
        {
            _inventoryOperationPending = false;
        }
    }

    private static bool TryFindInventoryAddress(Player player, Item item, out ItemAddress address)
    {
        address = null;
        foreach (var grid in player.Inventory.Equipment.GetPrioritizedGridsForLoot(item))
        {
            var candidate = grid.FindLocationForItem(item);
            if (candidate == null)
            {
                continue;
            }

            address = candidate;
            return true;
        }

        return false;
    }

    private static bool TryGetRaidPlayer(out Player player)
    {
        player = null;
        var screenManager = EftScreenManager.Instance;
        if (screenManager?.CurrentScreenController?.ScreenType != EEftScreenType.BattleUI
            || !Singleton<GameWorld>.Instantiated)
        {
            return false;
        }

        player = Singleton<GameWorld>.Instance?.MainPlayer;
        return player is LocalPlayer
            && player.IsYourPlayer
            && player.HealthController.IsAlive
            && player.InventoryController != null;
    }

    private static bool HasKit(Player player)
    {
        return CountKits(player) > 0;
    }

    private static int CountKits(Player player)
    {
        return player?.Inventory?.GetAllItemByTemplate(KitTemplateId).Count() ?? 0;
    }

    private void LeavePlacementMode()
    {
        _placementMode = false;
        _validPlacement = false;
        if (_preview != null)
        {
            UnityEngine.Object.Destroy(_preview);
            _preview = null;
        }
    }

    private void ResetPickup()
    {
        _pickupTarget = null;
        _pickupProgress = 0f;
    }

    private void RemoveDestroyedWires()
    {
        _placedWires.RemoveAll(wire => wire == null);
    }

    private void UpdatePreviewColor(bool valid)
    {
        if (_previewMaterial != null)
        {
            var color = valid
                ? new Color(0.2f, 1f, 0.25f, 1f)
                : new Color(1f, 0.2f, 0.15f, 1f);
            _previewMaterial.color = color;
            if (_previewMaterial.HasProperty("_EmissionColor"))
            {
                _previewMaterial.SetColor("_EmissionColor", color * 0.35f);
            }
        }
    }

    private GameObject CreateVisualObject(string name, Material material)
    {
        var root = new GameObject(name);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        var bounds = _wireMesh.bounds;
        visual.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        visual.AddComponent<MeshFilter>().sharedMesh = _wireMesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return root;
    }

    private static Material CreateOpaqueMaterial(Color color)
    {
        var shader = Shader.Find("Standard")
            ?? Shader.Find("Legacy Shaders/Diffuse")
            ?? Shader.Find("Unlit/Color");
        if (shader == null)
        {
            throw new InvalidOperationException("No compatible opaque Unity shader was found for deployable barbed wire.");
        }

        var material = new Material(shader);
        material.color = color;
        material.renderQueue = -1;
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetInt("_ZWrite", 1);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0.7f);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0.25f);
        }

        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        return material;
    }

    private static Material CreatePreviewMaterial(Material source, Color color)
    {
        if (source == null)
        {
            throw new InvalidOperationException("No source material was found for the barbed wire preview.");
        }

        var material = new Material(source)
        {
            name = "Deployable Barbed Wire Preview Material"
        };
        material.color = color;
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.SetColor("_EmissionColor", color * 0.35f);
            material.EnableKeyword("_EMISSION");
        }

        return material;
    }

    private bool TryFindPickupTarget(Player player, Camera camera, out PlacedBarbedWire target)
    {
        var hits = Physics.SphereCastAll(
            camera.transform.position,
            PickupSphereRadius,
            camera.transform.forward,
            PickupDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(player.Transform.Original))
            {
                continue;
            }

            var candidate = hit.collider.GetComponentInParent<PlacedBarbedWire>();
            if (candidate != null && _placedWires.Contains(candidate))
            {
                target = candidate;
                return true;
            }

            if (!hit.collider.isTrigger)
            {
                break;
            }
        }

        target = null;
        return false;
    }

    private static SoundBank FindNativeBarbedWireSoundBank()
    {
        return UnityEngine.Object.FindObjectsOfType<EFT.Interactive.BarbedWire>()
            .FirstOrDefault(wire => wire is not DeployableBarbedWireDamage && wire._soundBank != null)
            ?._soundBank;
    }

    private void CompletePendingPlacement(Player currentPlayer)
    {
        if (!string.IsNullOrEmpty(_pendingPlacementError))
        {
            DeployableBarbedWirePlugin.Log.LogError($"Kit transaction failed: {_pendingPlacementError}");
            _pendingPlacementError = null;
            Notify("The Barbed Wire Kit could not be used. Please check the log.");
        }

        var placement = _pendingPlacement;
        if (placement == null)
        {
            return;
        }

        _pendingPlacement = null;
        if (!Singleton<GameWorld>.Instantiated
            || !ReferenceEquals(Singleton<GameWorld>.Instance, placement.World)
            || !ReferenceEquals(currentPlayer, placement.Player))
        {
            return;
        }

        try
        {
            var wire = CreatePlacedWire(placement.Position, placement.Rotation);
            _placedWires.Add(wire);
            DeployableBarbedWirePlugin.Log.LogInfo("Wire created with an active damage trigger.");
            Notify("Barbed wire deployed.");
        }
        catch (Exception exception)
        {
            DeployableBarbedWirePlugin.Log.LogError($"Wire creation failed: {exception}");
            Notify("The wire could not be created after the kit was used. Please check the log.");
        }
    }

    private static void Notify(string message)
    {
        NotificationManager.DisplayMessageNotification(
            message,
            ENotificationDurationType.Default,
            ENotificationIconType.Default,
            null);
    }

    private Camera GetPlayerCamera(Player player)
    {
        if (_playerCameraController != null
            && ReferenceEquals(_playerCameraController.Player, player)
            && _playerCameraController.Camera != null)
        {
            return _playerCameraController.Camera;
        }

        _playerCameraController = UnityEngine.Object.FindObjectsOfType<PlayerCameraController>()
            .FirstOrDefault(controller => ReferenceEquals(controller.Player, player) && controller.Camera != null);
        return _playerCameraController?.Camera;
    }

    private static bool TryFindGround(Player player, Vector3 rayOrigin, out RaycastHit groundHit)
    {
        var hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            4f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(player.Transform.Original))
            {
                continue;
            }

            groundHit = hit;
            return true;
        }

        groundHit = default;
        return false;
    }

    [HarmonyPatch(typeof(InputManager), nameof(InputManager.DispatchInput))]
    private static class InputDispatchPatch
    {
        private static void Prefix(List<ECommand> commandsList)
        {
            if (_activeModule?._placementMode != true || commandsList == null)
            {
                return;
            }

            commandsList.RemoveAll(command =>
                command == ECommand.Escape
                || command == ECommand.ScrollNext
                || command == ECommand.ScrollPrevious
                || command == ECommand.DecreaseWalkSpeed);
        }
    }

    private sealed class PendingPlacement
    {
        internal readonly GameWorld World;
        internal readonly Player Player;
        internal readonly Vector3 Position;
        internal readonly Quaternion Rotation;

        internal PendingPlacement(GameWorld world, Player player, Vector3 position, Quaternion rotation)
        {
            World = world;
            Player = player;
            Position = position;
            Rotation = rotation;
        }
    }
}
