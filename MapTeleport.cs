using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
 /// <summary>
 /// A simple teleport plugin that allows players with permission to teleport to map marker locations, specific coordinates
 /// or the location they are looking at. Also includes a camera teleport command that moves the player's camera to the target location temporarily.
 /// === 1.0.0 ===
 /// - Initial plugin creation
 /// - Features:
 ///   - Teleportation to map marker location by clicking the marker (configurable single/double/triple click)
 /// - Chat commands:
 ///   - /mtp command for teleporting to map marker, coordinates, or look target
 ///   - /mtpc command for temporarily moving camera to map marker, coordinates, or look target
 /// - Console commands:
 ///   - mapteleport.tp for teleporting to map marker, coordinates, or look target
 ///   - mapteleport.tpc for temporarily moving camera to map marker, coordinates, or look target
 /// - Permission: mapteleport.use for access to all teleport features, admins have implicit access
 /// - Configurable options:
 ///   - Teleport height offset to prevent getting stuck in the ground
 ///   - Max raycast distance for look target teleportation
 ///   - Option to show teleport success messages with coordinates and grid reference
 ///   - Option to enable/disable map marker trigger teleportation
 ///   - Configurable trigger mode for map marker teleportation (single/double/triple click)
 ///   - Configurable time window and position tolerance for multi-click map marker triggers
 ///   - Option to remove the map marker after teleporting via marker trigger
 /// </summary>
 [Info("MapTeleport", "usagi", "1.0.0")]
 [Description("Simple teleport plugin with admin default access and mapteleport.use permission.")]
 public class MapTeleport : RustPlugin
 {
  private const float GridCalgon = 0.0066666666666667f;
  private const string PermUse = "mapteleport.use";

  private PluginConfig config;
  private readonly Dictionary<ulong, MarkerClickState> markerClickStates = new Dictionary<ulong, MarkerClickState>();

  private class MarkerClickState
  {
   public Vector3 LastPosition;
   public float LastClickTime;
   public int Count;
  }

  private class PluginConfig
  {
   [JsonProperty("Teleport Height Offset")]
   public float TeleportHeightOffset = 2.0f;

   [JsonProperty("Max Raycast Distance")]
   public float MaxRaycastDistance = 5000f;

   [JsonProperty("Show Teleport Success Message")]
   public bool ShowTeleportSuccessMessage = true;

   [JsonProperty("Enable Map Marker Trigger Teleport")]
   public bool EnableMapMarkerTriggerTeleport = true;

   [JsonProperty("Map Marker Trigger Mode (Single/Double/Triple)")]
   public string MapMarkerTriggerMode = "Double";

   [JsonProperty("Map Marker Multi-Click Time Window Seconds")]
   public float MapMarkerMultiClickTimeWindowSeconds = 1.70f;

   [JsonProperty("Map Marker Multi-Click Position Tolerance Meters")]
   public float MapMarkerMultiClickPositionToleranceMeters = 35f;

   [JsonProperty("Remove Trigger Marker After Marker Teleport")]
   public bool RemoveTriggerMarkerAfterMarkerTeleport = true;

   [JsonProperty("Camera Teleport Return Delay Seconds")]
   public float CameraTeleportReturnDelaySeconds = 0.01f;
  }

  protected override void LoadDefaultConfig()
  {
   config = new PluginConfig();
   SaveConfig();
  }

  protected override void SaveConfig() => Config.WriteObject(config, true);

  private void LoadConfigValues()
  {
   try
   {
    config = Config.ReadObject<PluginConfig>();
    if (config == null)
    {
     throw new Exception("Config was null");
    }
   }
   catch
   {
    PrintWarning("Failed to read config, generating default config.");
    LoadDefaultConfig();
   }

   SaveConfig();
  }

  private void Init()
  {
   permission.RegisterPermission(PermUse, this);
   LoadConfigValues();
  }

  private void OnPlayerDisconnected(BasePlayer player, string reason)
  {
   if (player != null)
   {
    markerClickStates.Remove(player.userID);
   }
  }

  private void Unload()
  {
   markerClickStates.Clear();
  }

  private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
  {
   if (player == null || note == null || !config.EnableMapMarkerTriggerTeleport)
   {
    return;
   }

   if (!HasAccess(player))
   {
    return;
   }

   Vector3 markerPos = note.worldPosition;
   int requiredClicks = GetRequiredMarkerClicks();
   if (requiredClicks <= 1)
   {
    if (TryTeleport(player, new Vector3(markerPos.x, GetSafeY(markerPos.x, markerPos.z), markerPos.z)))
    {
     HandlePostMarkerTeleport(player, note);
    }
    return;
   }

   float now = Time.realtimeSinceStartup;
   if (!markerClickStates.TryGetValue(player.userID, out MarkerClickState state))
   {
    state = new MarkerClickState
    {
     LastPosition = markerPos,
     LastClickTime = now,
     Count = 1
    };
    markerClickStates[player.userID] = state;
    return;
   }

   float timeWindow = Mathf.Max(0.05f, config.MapMarkerMultiClickTimeWindowSeconds);
   float tolerance = Mathf.Max(0f, config.MapMarkerMultiClickPositionToleranceMeters);
   float distance = Vector2.Distance(new Vector2(state.LastPosition.x, state.LastPosition.z), new Vector2(markerPos.x, markerPos.z));
   bool inWindow = (now - state.LastClickTime) <= timeWindow;
   bool inTolerance = distance <= tolerance;

   if (inWindow && inTolerance)
   {
    state.Count++;
   }
   else
   {
    state.Count = 1;
   }

   state.LastPosition = markerPos;
   state.LastClickTime = now;

   if (state.Count >= requiredClicks)
   {
    state.Count = 0;
    if (TryTeleport(player, new Vector3(markerPos.x, GetSafeY(markerPos.x, markerPos.z), markerPos.z)))
    {
     HandlePostMarkerTeleport(player, note);
    }
   }
  }

  private void HandlePostMarkerTeleport(BasePlayer player, ProtoBuf.MapNote note)
  {
   if (player == null)
   {
    return;
   }

   if (config.RemoveTriggerMarkerAfterMarkerTeleport)
   {
    RemoveMapMarker(player, note);
   }
  }

  private void RemoveMapMarker(BasePlayer player, ProtoBuf.MapNote note)
  {
   if (player?.State?.pointsOfInterest == null || note == null)
   {
    return;
   }

   if (player.State.pointsOfInterest.Remove(note))
   {
    if (note.ShouldPool)
    {
     note.Dispose();
    }

    player.DirtyPlayerState();
    player.SendMarkersToClient();
   }
  }

  private int GetRequiredMarkerClicks()
  {
   string mode = config.MapMarkerTriggerMode?.Trim().ToLowerInvariant() ?? "double";
   if (mode == "single")
   {
    return 1;
   }

   if (mode == "triple")
   {
    return 3;
   }

   return 2;
  }

  [ChatCommand("mtp")]
  private void ChatCommandMtp(BasePlayer player, string command, string[] args)
  {
   if (!HasAccess(player))
   {
    Reply(player, "You don't have permission. (mapteleport.use)");
    return;
   }

   if (TryResolveTarget(player, args, out Vector3 target))
   {
    _ = TryTeleport(player, target);
    return;
   }

   Reply(player, "Usage: /mtp, /mtp <x> <z>, /mtp <grid> (e.g. /mtp s10)");
  }

  [ChatCommand("mtpc")]
  private void ChatCommandMtpc(BasePlayer player, string command, string[] args)
  {
   if (!HasAccess(player))
   {
    Reply(player, "You don't have permission. (mapteleport.use)");
    return;
   }

   if (TryResolveTarget(player, args, out Vector3 target))
   {
    DoCameraTeleport(player, target);
    return;
   }

   Reply(player, "Usage: /mtpc, /mtpc <x> <z>, /mtpc <grid> (e.g. /mtpc s10)");
  }

  [ConsoleCommand("mapteleport.tp")]
  private void ConsoleCommandTp(ConsoleSystem.Arg arg)
  {
   BasePlayer player = arg.Player();
   if (player == null)
   {
    return;
   }

   if (!HasAccess(player))
   {
    Reply(player, "You don't have permission. (mapteleport.use)");
    return;
   }

   if (TryResolveTarget(player, arg.Args, out Vector3 target))
   {
    _ = TryTeleport(player, target);
    return;
   }

   Reply(player, "Could not find a valid target. Look at terrain, use x z, or use grid like s10.");
  }

  [ConsoleCommand("mapteleport.tpc")]
  private void ConsoleCommandTpc(ConsoleSystem.Arg arg)
  {
   BasePlayer player = arg.Player();
   if (player == null)
   {
    return;
   }

   if (!HasAccess(player))
   {
    Reply(player, "You don't have permission. (mapteleport.use)");
    return;
   }

   if (TryResolveTarget(player, arg.Args, out Vector3 target))
   {
    DoCameraTeleport(player, target);
    return;
   }

   Reply(player, "Could not find a valid target. Look at terrain, use x z, or use grid like s10.");
  }

  private bool TryResolveTarget(BasePlayer player, string[] args, out Vector3 target)
  {
   target = Vector3.zero;

   if (args == null || args.Length == 0)
   {
    return TryGetLookTarget(player, out target);
   }

   if (args.Length >= 2 && TryParseCoordinates(args[0], args[1], out target))
   {
    return true;
   }

   if (args.Length >= 1 && TryParseGridReference(args[0], out target))
   {
    return true;
   }

   return false;
  }

  private bool HasAccess(BasePlayer player)
  {
   return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermUse));
  }

  private bool TryParseCoordinates(string xArg, string zArg, out Vector3 result)
  {
   result = Vector3.zero;

   if (!float.TryParse(xArg, out float x) || !float.TryParse(zArg, out float z))
   {
    return false;
   }

   float y = GetSafeY(x, z);
   result = new Vector3(x, y, z);
   return true;
  }

  private bool TryParseGridReference(string gridArg, out Vector3 result)
  {
   result = Vector3.zero;
   if (string.IsNullOrWhiteSpace(gridArg))
   {
    return false;
   }

   string value = gridArg.Trim().ToUpperInvariant();
   int splitIndex = 0;
   while (splitIndex < value.Length && char.IsLetter(value[splitIndex]))
   {
    splitIndex++;
   }

   if (splitIndex == 0 || splitIndex >= value.Length)
   {
    return false;
   }

   string letters = value.Substring(0, splitIndex);
   string numberPart = value.Substring(splitIndex);

   if (!int.TryParse(numberPart, out int rowIndex) || rowIndex < 0)
   {
    return false;
   }

   int columnIndex = ColumnLettersToIndex(letters);
   if (columnIndex < 0)
   {
    return false;
   }

   float worldSize = ConVar.Server.worldsize;
   float gridWidth = GridCalgon * worldSize;
   if (gridWidth <= 0f)
   {
    return false;
   }

   float step = worldSize / gridWidth;
   int maxColumns = Mathf.FloorToInt(gridWidth);
   int maxRows = Mathf.FloorToInt(gridWidth);
   if (columnIndex >= maxColumns || rowIndex >= maxRows)
   {
    return false;
   }

   float half = worldSize * 0.5f;
   Vector3 estimated = new Vector3(-half + ((columnIndex + 0.5f) * step), 0f, half - ((rowIndex + 0.5f) * step));

   string desiredGrid = value;
   if (!TryFindPointInsideGrid(desiredGrid, estimated, step, out Vector3 insidePoint))
   {
    return false;
   }

   Vector3 center = CalculateGridCenter(desiredGrid, insidePoint, step);
   result = new Vector3(center.x, GetSafeY(center.x, center.z), center.z);
   return true;
  }

  private bool TryFindPointInsideGrid(string desiredGrid, Vector3 estimated, float step, out Vector3 insidePoint)
  {
   insidePoint = estimated;
   if (GetGridAtPosition(estimated) == desiredGrid)
   {
    return true;
   }

   float stride = Mathf.Max(5f, step * 0.5f);
   for (int radius = 1; radius <= 4; radius++)
   {
    for (int x = -radius; x <= radius; x++)
    {
     for (int z = -radius; z <= radius; z++)
     {
      Vector3 probe = new Vector3(estimated.x + (x * stride), 0f, estimated.z + (z * stride));
      if (GetGridAtPosition(probe) == desiredGrid)
      {
       insidePoint = probe;
       return true;
      }
     }
    }
   }

   return false;
  }

  private Vector3 CalculateGridCenter(string desiredGrid, Vector3 insidePoint, float step)
  {
   float halfWorld = ConVar.Server.worldsize * 0.5f;
   float searchStep = Mathf.Max(5f, step * 0.5f);

   float left = FindBoundaryOnAxis(desiredGrid, insidePoint, step, searchStep, -1f, true, halfWorld);
   float right = FindBoundaryOnAxis(desiredGrid, insidePoint, step, searchStep, 1f, true, halfWorld);
   float centerX = (left + right) * 0.5f;

   Vector3 recentered = new Vector3(centerX, 0f, insidePoint.z);
   float bottom = FindBoundaryOnAxis(desiredGrid, recentered, step, searchStep, -1f, false, halfWorld);
   float top = FindBoundaryOnAxis(desiredGrid, recentered, step, searchStep, 1f, false, halfWorld);
   float centerZ = (bottom + top) * 0.5f;

   return new Vector3(centerX, 0f, centerZ);
  }

  private float FindBoundaryOnAxis(string desiredGrid, Vector3 insidePoint, float step, float searchStep, float direction, bool xAxis, float halfWorld)
  {
   float inside = xAxis ? insidePoint.x : insidePoint.z;
   float outside = inside;

   int maxWalk = Mathf.CeilToInt((step * 2f) / searchStep);
   for (int i = 0; i < maxWalk; i++)
   {
    float candidate = outside + (direction * searchStep);
    if (Mathf.Abs(candidate) > halfWorld)
    {
     break;
    }

    Vector3 probe = xAxis
        ? new Vector3(candidate, 0f, insidePoint.z)
        : new Vector3(insidePoint.x, 0f, candidate);

    if (GetGridAtPosition(probe) == desiredGrid)
    {
     inside = candidate;
     outside = candidate;
     continue;
    }

    outside = candidate;
    break;
   }

   float a = inside;
   float b = outside;
   for (int i = 0; i < 24; i++)
   {
    float mid = (a + b) * 0.5f;
    Vector3 probe = xAxis
        ? new Vector3(mid, 0f, insidePoint.z)
        : new Vector3(insidePoint.x, 0f, mid);

    if (GetGridAtPosition(probe) == desiredGrid)
    {
     a = mid;
    }
    else
    {
     b = mid;
    }
   }

   return (a + b) * 0.5f;
  }

  private string GetGridAtPosition(Vector3 position)
  {
   return MapHelper.PositionToString(position).Trim().ToUpperInvariant();
  }

  private int ColumnLettersToIndex(string letters)
  {
   int value = 0;
   for (int i = 0; i < letters.Length; i++)
   {
    char c = letters[i];
    if (c < 'A' || c > 'Z')
    {
     return -1;
    }

    value = (value * 26) + (c - 'A' + 1);
   }

   return value - 1;
  }

  private float GetSafeY(float x, float z)
  {
   float y = TerrainMeta.HeightMap.GetHeight(new Vector3(x, 0f, z));
   return y + Mathf.Max(0.5f, config.TeleportHeightOffset);
  }

  private bool TryGetLookTarget(BasePlayer player, out Vector3 target)
  {
   target = Vector3.zero;

   if (player?.eyes == null)
   {
    return false;
   }

   Ray ray = new Ray(player.eyes.position, player.eyes.HeadForward());
   if (Physics.Raycast(ray, out RaycastHit hit, config.MaxRaycastDistance, Rust.Layers.Solid | Rust.Layers.Terrain | Rust.Layers.World))
   {
    target = new Vector3(hit.point.x, GetSafeY(hit.point.x, hit.point.z), hit.point.z);
    return true;
   }

   return false;
  }

  private bool TryTeleport(BasePlayer player, Vector3 target)
  {
   if (player == null || player.IsDead() || player.IsSleeping())
   {
    return false;
   }

   object block = Interface.CallHook("CanTeleport", player);
   if (block == null)
   {
    block = Interface.CallHook("canTeleport", player);
   }

   if (block is string reason && !string.IsNullOrEmpty(reason))
   {
    Reply(player, reason);
    return false;
   }

   player.Teleport(target);
   if (config?.ShowTeleportSuccessMessage ?? true)
   {
    string grid = GetGridAtPosition(target);
    Reply(player, $"Teleported to: {target.x:0} {target.z:0} (Grid: {grid} center)");
   }

   return true;
  }

  private void DoCameraTeleport(BasePlayer player, Vector3 target)
  {
   if (player == null || player.IsDead() || player.IsSleeping())
   {
    return;
   }

   object block = Interface.CallHook("CanTeleport", player);
   if (block == null)
   {
    block = Interface.CallHook("canTeleport", player);
   }

   if (block is string reason && !string.IsNullOrEmpty(reason))
   {
    Reply(player, reason);
    return;
   }

   Vector3 original = player.transform.position;
   player.Teleport(target);

   float delay = Mathf.Clamp(config?.CameraTeleportReturnDelaySeconds ?? 0.08f, 0.01f, 30f);
   timer.Once(delay, () =>
   {
    if (player == null || !player.IsConnected || player.IsDead())
    {
     return;
    }

    player.SendConsoleCommand("debugcamera");

    timer.Once(0.05f, () =>
    {
     if (player != null && player.IsConnected && !player.IsDead())
     {
      player.Teleport(original);
     }
    });
   });

   if (config?.ShowTeleportSuccessMessage ?? true)
   {
    string grid = GetGridAtPosition(target);
    Reply(player, $"Camera moved to: {target.x:0} {target.z:0} (Grid: {grid})");
   }
  }

  private static void Reply(BasePlayer player, string message)
  {
   player.ChatMessage($"<color=#8a5cff>MapTeleport</color>: {message}");
  }
 }
}
