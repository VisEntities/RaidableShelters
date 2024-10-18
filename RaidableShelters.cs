/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Raidable Shelters", "VisEntities", "1.7.0")]
    [Description("Spawns shelters filled with loot for players to raid.")]
    public class RaidableShelters : RustPlugin
    {
        #region Fields

        private static RaidableShelters _plugin;
        private static Configuration _config;
        private StoredData _storedData;

        private System.Random _randomGenerator = new System.Random();
        private Timer _sheltersRespawnTimer;
        
        private const int LAYER_GROUND = Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Default;
        private const int LAYER_PLAYER = Layers.Mask.Player_Server;
        private const int LAYER_ENTITIES = Layers.Mask.Deployed | Layers.Mask.Construction;

        private const string PREFAB_AUTO_TURRET = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";
        private const string PREFAB_LEGACY_SHELTER = "assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.deployed.prefab";
        private const string PREFAB_LANDMINE = "assets/prefabs/deployable/landmine/landmine.prefab";
        private const string PREFAB_BEAR_TRAP = "assets/prefabs/deployable/bear trap/beartrap.prefab";

        private const float SPAWNABLE_AREA_RADIUS_INSIDE_SHELTER = 1.7f;

        private static readonly Vector3 _autoTurretPosition = new Vector3(0.084f, 3.146f, 0.481f);
        private static readonly Vector3 _autoTurretRotation = new Vector3(355.103f, 0f, 0f);
        
        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Enable Debug")]
            public bool EnableDebug { get; set; }

            [JsonProperty("Number Of Attempts To Find Shelter Position Near Players")]
            public int NumberOfAttemptsToFindShelterPositionNearPlayers { get; set; }

            [JsonProperty("Minimum Search Radius For Shelter Position Around Player")]
            public float MinimumSearchRadiusForShelterPositionAroundPlayer { get; set; }

            [JsonProperty("Maximum Search Radius For Shelter Position Around Player")]
            public float MaximumSearchRadiusForShelterPositionAroundPlayer { get; set; }

            [JsonProperty("Shelters Respawn Frequency Minutes")]
            public float SheltersRespawnFrequencyMinutes { get; set; }

            [JsonProperty("Delay Between Each Shelter Spawn Seconds")]
            public float DelayBetweenEachShelterSpawnSeconds { get; set; }

            [JsonProperty("Nearby Entities Avoidance Radius")]
            public float NearbyEntitiesAvoidanceRadius { get; set; }

            [JsonProperty("Rocks Avoidance Radius")]
            public float RocksAvoidanceRadius { get; set; }

            [JsonProperty("Distance From No Build Zones")]
            public float DistanceFromNoBuildZones { get; set; }

            [JsonProperty("Shelter Health")]
            public float ShelterHealth { get; set; }

            [JsonProperty("Shelter Lifetime Seconds")]
            public float ShelterLifetimeSeconds { get; set; }

            [JsonProperty("Number Of Attempts For Determining Entity Position Inside Shelter")]
            public int NumberOfAttemptsForDeterminingEntityPositionInsideShelter { get; set; }

            [JsonProperty("Number Of Attempts For Determining Entity Rotation Inside Shelter")]
            public int NumberOfAttemptsForDeterminingEntityRotationInsideShelter { get; set; }

            [JsonProperty("Door")]
            public DoorConfig Door { get; set; }

            [JsonProperty("Turret")]
            public TurretConfig Turret { get; set; }

            [JsonProperty("Trap")]
            public TrapConfig Trap { get; set; }

            [JsonProperty("Notification")]
            public NotificationConfig Notification { get; set; }

            [JsonProperty("Interior Entities")]
            public List<InteriorEntityConfig> InteriorEntities { get; set; }

            [JsonProperty("Items To Spawn Inside Entity Containers")]
            public List<ItemInfo> ItemsToSpawnInsideEntityContainers { get; set; }
        }

        public class DoorConfig
        {
            [JsonProperty("Skin Ids")]
            public List<ulong> SkinIds { get; set; }
        }

        public class TurretConfig
        {
            [JsonProperty("Spawn Auto Turret")]
            public bool SpawnAutoTurret { get; set; }

            [JsonProperty("Health")]
            public float Health { get; set; }

            [JsonProperty("Weapon Short Name")]
            public string WeaponShortName { get; set; }

            [JsonProperty("Clip Ammo")]
            public ItemInfo ClipAmmo { get; set; }

            [JsonProperty("Reserve Ammo")]
            public List<ItemInfo> ReserveAmmo { get; set; }

            [JsonProperty("Attachment Short Names")]
            public List<string> AttachmentShortNames { get; set; }

            [JsonProperty("Peacekeeper")]
            public bool Peacekeeper { get; set; }
        }

        public class TrapConfig
        {
            [JsonProperty("Spawn Landmines")]
            public bool SpawnLandmines { get; set; }

            [JsonProperty("Spawn Bear Traps")]
            public bool SpawnBearTraps { get; set; }

            [JsonProperty("Minimum Number Of Traps To Spawn")]
            public int MinimumNumberOfTrapsToSpawn { get; set; }

            [JsonProperty("Maximum Number Of Traps To Spawn")]
            public int MaximumNumberOfTrapsToSpawn { get; set; }

            [JsonProperty("Minimum Spawn Radius Around Shelter")]
            public float MinimumSpawnRadiusAroundShelter { get; set; }

            [JsonProperty("Maximum Spawn Radius Around Shelter")]
            public float MaximumSpawnRadiusAroundShelter { get; set; }
        }

        public class NotificationConfig
        {
            [JsonProperty("Notify Surrounding Players Of Shelter Spawn")]
            public bool NotifySurroundingPlayersOfShelterSpawn { get; set; }

            [JsonProperty("Radius For Notifying Nearby Players")]
            public float RadiusForNotifyingNearbyPlayers { get; set; }

            [JsonProperty("Send As Toast")]
            public bool SendAsToast { get; set; }
        }

        public class InteriorEntityConfig
        {
            [JsonProperty("Prefab Name")]
            public string PrefabName { get; set; }

            [JsonProperty("Skin Ids")]
            public List<ulong> SkinIds { get; set; }

            [JsonProperty("Spawn Chance")]
            public int SpawnChance { get; set; }

            [JsonProperty("Minimum Number To Spawn")]
            public int MinimumNumberToSpawn { get; set; }

            [JsonProperty("Maximum Number To Spawn")]
            public int MaximumNumberToSpawn { get; set; }

            [JsonProperty("Percentage To Fill Container With Items If Present")]
            public int PercentageToFillContainerWithItemsIfPresent { get; set; }
        }

        public class ItemInfo
        {
            [JsonProperty("ShortName")]
            public string ShortName { get; set; }

            [JsonProperty("Skin Id")]
            public ulong SkinId { get; set; }

            [JsonProperty("Minimum Amount")]
            public int MinimumAmount { get; set; }

            [JsonProperty("Maximum Amount")]
            public int MaximumAmount { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                _config.Notification = defaultConfig.Notification;

                foreach (InteriorEntityConfig interiorEntityConfig in _config.InteriorEntities)
                {
                    interiorEntityConfig.SkinIds = new List<ulong>();
                }
            }

            if (string.Compare(_config.Version, "1.2.0") < 0)
            {
                _config.Door = defaultConfig.Door;
            }

            if (string.Compare(_config.Version, "1.3.0") < 0)
            {
                foreach(InteriorEntityConfig interiorEntityConfig in _config.InteriorEntities)
                {
                    interiorEntityConfig.SpawnChance = 50;
                }
            }

            if (string.Compare(_config.Version, "1.4.0") < 0)
            {
                _config.EnableDebug = defaultConfig.EnableDebug;
            }

            if (string.Compare(_config.Version, "1.5.0") < 0)
            {
                _config.ShelterHealth = defaultConfig.ShelterHealth;
            }

            if (string.Compare(_config.Version, "1.6.0") < 0)
            {
                _config.Turret = defaultConfig.Turret;
            }

            if (string.Compare(_config.Version, "1.7.0") < 0)
            {
                _config.Trap = defaultConfig.Trap;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                EnableDebug = false,
                NumberOfAttemptsToFindShelterPositionNearPlayers = 5,
                MinimumSearchRadiusForShelterPositionAroundPlayer = 20f,
                MaximumSearchRadiusForShelterPositionAroundPlayer = 50f,
                NearbyEntitiesAvoidanceRadius = 6f,
                RocksAvoidanceRadius = 5f,
                DistanceFromNoBuildZones = 10f,
                ShelterHealth = 500f,
                ShelterLifetimeSeconds = 600f,
                NumberOfAttemptsForDeterminingEntityPositionInsideShelter = 30,
                NumberOfAttemptsForDeterminingEntityRotationInsideShelter = 30,
                SheltersRespawnFrequencyMinutes = 60f,
                DelayBetweenEachShelterSpawnSeconds = 5f,
                Door = new DoorConfig
                {
                    SkinIds = new List<ulong>
                    {
                        809253752,
                        2246937402,
                        2483070538,
                        3076134051
                    }
                },
                Turret = new TurretConfig
                {
                    SpawnAutoTurret = false,
                    Health = 1000f,
                    WeaponShortName = "rifle.ak",
                    ClipAmmo = new ItemInfo
                    {
                        ShortName = "ammo.rifle",
                        MinimumAmount = 30,
                        MaximumAmount = 30
                    },
                    ReserveAmmo = new List<ItemInfo>
                    {
                        new ItemInfo
                        {
                            ShortName = "ammo.rifle",
                            MinimumAmount = 128,
                            MaximumAmount = 128
                        }
                    },
                    AttachmentShortNames = new List<string>
                    {
                        "weapon.mod.lasersight"
                    },
                    Peacekeeper = false,
                },
                Trap = new TrapConfig
                {
                    SpawnLandmines = false,
                    SpawnBearTraps = true,
                    MinimumSpawnRadiusAroundShelter = 3f,
                    MaximumSpawnRadiusAroundShelter = 5f,
                    MinimumNumberOfTrapsToSpawn = 1,
                    MaximumNumberOfTrapsToSpawn = 5
                },
                Notification = new NotificationConfig
                {
                    NotifySurroundingPlayersOfShelterSpawn = false,
                    RadiusForNotifyingNearbyPlayers = 40f,
                    SendAsToast = true
                },
                InteriorEntities = new List<InteriorEntityConfig>
                {
                    new InteriorEntityConfig
                    {
                        PrefabName = "assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab",
                        SkinIds = new List<ulong>
                        {
                            0
                        },
                        SpawnChance = 50,
                        MinimumNumberToSpawn = 1,
                        MaximumNumberToSpawn = 3,
                        PercentageToFillContainerWithItemsIfPresent = 20,
                    },
                    new InteriorEntityConfig
                    {
                        PrefabName = "assets/prefabs/deployable/furnace/furnace.prefab",
                        SkinIds = new List<ulong>
                        {
                            0
                        },
                        SpawnChance = 50,
                        MinimumNumberToSpawn = 1,
                        MaximumNumberToSpawn = 1,
                        PercentageToFillContainerWithItemsIfPresent = 0,
                    },
                },
                ItemsToSpawnInsideEntityContainers = new List<ItemInfo>()
                {
                    new ItemInfo
                    {
                        ShortName = "fat.animal",
                        SkinId = 0,
                        MinimumAmount = 10,
                        MaximumAmount = 25,
                    },
                    new ItemInfo
                    {
                        ShortName = "cloth",
                        SkinId = 0,
                        MinimumAmount = 20,
                        MaximumAmount = 30,
                    },
                    new ItemInfo
                    {
                        ShortName = "wood",
                        SkinId = 0,
                        MinimumAmount = 200,
                        MaximumAmount = 400,
                    },
                    new ItemInfo
                    {
                        ShortName = "syringe.medical",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        ShortName = "rope",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 3,
                    },
                    new ItemInfo
                    {
                        ShortName = "cctv.camera",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "roadsigns",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        ShortName = "stones",
                        SkinId = 0,
                        MinimumAmount = 150,
                        MaximumAmount = 350,
                    },
                    new ItemInfo
                    {
                        ShortName = "metal.fragments",
                        SkinId = 0,
                        MinimumAmount = 30,
                        MaximumAmount = 90,
                    },
                    new ItemInfo
                    {
                        ShortName = "ammo.grenadelauncher.he",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        ShortName = "coffeecan.helmet",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "scrap",
                        SkinId = 0,
                        MinimumAmount = 10,
                        MaximumAmount = 25,
                    },
                    new ItemInfo
                    {
                        ShortName = "icepick.salvaged",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "ptz.cctv.camera",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "corn",
                        SkinId = 0,
                        MinimumAmount = 3,
                        MaximumAmount = 5,
                    },
                    new ItemInfo
                    {
                        ShortName = "ammo.rocket.mlrs",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "wall.frame.garagedoor",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        ShortName = "pistol.revolver",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                }
            };
        }

        #endregion Configuration

        #region Data Utility

        public class DataFileUtil
        {
            private const string FOLDER = "";

            public static string GetFilePath(string filename = null)
            {
                if (filename == null)
                    filename = _plugin.Name;

                return Path.Combine(FOLDER, filename);
            }

            public static string[] GetAllFilePaths()
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(FOLDER);

                for (int i = 0; i < filePaths.Length; i++)
                {
                    // Remove the redundant '.json' from the filepath. This is necessary because the filepaths are returned with a double '.json'.
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);
                }

                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        #endregion Data Utility

        #region Stored Data

        public class StoredData
        {
            [JsonProperty("Raidable Shelters")]
            public Dictionary<ulong, ShelterData> Shelters { get; set; } = new Dictionary<ulong, ShelterData>();
        }

        public class ShelterData
        {
            [JsonProperty("Interior Entities")]
            public List<ulong> InteriorEntities { get; set; } = new List<ulong>();

            [JsonProperty("Removal Timer")]
            public double RemovalTimer { get; set; }
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            _storedData = DataFileUtil.LoadOrCreate<StoredData>(DataFileUtil.GetFilePath());
        }

        private void Unload()
        {
            if (_sheltersRespawnTimer != null)
                _sheltersRespawnTimer.Destroy();

            CoroutineUtil.StopAllCoroutines();
            KillAllShelters();
            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            _sheltersRespawnTimer = timer.Every(_config.SheltersRespawnFrequencyMinutes * 60f, () =>
            {
                CoroutineUtil.StartCoroutine("RespawnSheltersCoroutine", RespawnSheltersCoroutine());
            });

            ResumeShelterRemovalTimers();
        }

        #endregion Oxide Hooks

        #region True PVE Hooks

        private object CanEntityTakeDamage(LegacyShelter shelter)
        {
            if (shelter == null || !API_IsShelterRaidable(shelter))
                return null;

            return true;
        }

        #endregion True PVE Hooks

        #region Shelters Spawning and Setup

        private IEnumerator RespawnSheltersCoroutine()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && !PlayerUtil.Wounded(player) && !PlayerUtil.Sleeping(player) && !PlayerUtil.InBase(player)
                    && !PlayerUtil.Swimming(player) && !PlayerUtil.Boating(player) && !PlayerUtil.Flying(player) && PlayerUtil.OnGround(player)
                    && !PlayerUtil.NearEnemyBase(player) && !TerrainUtil.InRadTown(player.transform.position))
                {
                    Vector3 shelterPosition;
                    Quaternion shelterRotation;

                    if (TryFindSuitableShelterSpawnPoint(player.transform.position, _config.MinimumSearchRadiusForShelterPositionAroundPlayer, _config.MaximumSearchRadiusForShelterPositionAroundPlayer,
                        _config.NumberOfAttemptsToFindShelterPositionNearPlayers, out shelterPosition, out shelterRotation))
                    {
                        LegacyShelter shelter = SpawnLegacyShelter(shelterPosition, shelterRotation, player);
                        if (shelter != null)
                        {
                            NotifyOfShelterSpawn(shelter, player);

                            if (_config.Trap.SpawnLandmines || _config.Trap.SpawnBearTraps)
                                SpawnTrapsAroundShelter(shelter);

                            if (_config.Turret.SpawnAutoTurret)
                                DeployAutoTurret(shelter);

                            if (_config.EnableDebug)
                                DrawDebugInfo(shelterPosition, "Shelter spawn successful", ParseColor("#27AE60"), 4f);
                        }
                    }
                }
                else if (_config.EnableDebug)
                {
                    string reason = "unknown reason";

                    if (PlayerUtil.Wounded(player))
                        reason = "player wounded";
                    else if (PlayerUtil.Sleeping(player))
                        reason = "player sleeping";
                    else if (PlayerUtil.InBase(player))
                        reason = "player in base";
                    else if (PlayerUtil.Swimming(player))
                        reason = "player swimming";
                    else if (PlayerUtil.Boating(player))
                        reason = "player boating";
                    else if (PlayerUtil.Flying(player))
                        reason = "player flying";
                    else if (!PlayerUtil.OnGround(player))
                        reason = "player not on ground";
                    else if (PlayerUtil.NearEnemyBase(player))
                        reason = "player near enemy base";
                    else if (TerrainUtil.InRadTown(player.transform.position))
                        reason = "player in radtown";

                    DrawDebugInfo(player.transform.position, $"Shelter spawn failed:\n{reason}", ParseColor("#E12126"));
                }

                yield return CoroutineEx.waitForSeconds(_config.DelayBetweenEachShelterSpawnSeconds);
            }
        }

        private bool TryFindSuitableShelterSpawnPoint(Vector3 center, float minSearchRadius, float maxSearchRadius, int maxAttempts, out Vector3 suitablePosition, out Quaternion suitableRotation)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vector3 position = TerrainUtil.GetRandomPositionAround(center, minSearchRadius, maxSearchRadius);

                if (TerrainUtil.InsideRock(position, _config.RocksAvoidanceRadius))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nInside rock", ParseColor("#E12126"), _config.RocksAvoidanceRadius);
                    continue;
                }

                if (TerrainUtil.InRadTown(position))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nIn radtown", ParseColor("#E12126"));
                    continue;
                }

                if (TerrainUtil.HasEntityNearby(position, _config.NearbyEntitiesAvoidanceRadius, LAYER_ENTITIES))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nNearby entities", ParseColor("#E12126"), _config.NearbyEntitiesAvoidanceRadius);
                    continue;
                }

                if (PlayerUtil.HasPlayerNearby(position, _config.NearbyEntitiesAvoidanceRadius))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nNearby players", ParseColor("#E12126"), _config.NearbyEntitiesAvoidanceRadius);
                    continue;
                }

                if (TerrainUtil.InWater(position))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nIn water", ParseColor("#E12126"));
                    continue;
                }

                if (TerrainUtil.OnRoadOrRail(position))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nOn road or rail", ParseColor("#E12126"));
                    continue;
                }

                if (!TerrainUtil.OnTerrain(position, 4f))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nNot on terrain", ParseColor("#E12126"), 4f);
                    continue;
                }

                if (TerrainUtil.InNoBuildZone(position, _config.DistanceFromNoBuildZones))
                {
                    if (_config.EnableDebug)
                        DrawDebugInfo(position, "Spawn failed:\nIn no-build zone", ParseColor("#E12126"), _config.DistanceFromNoBuildZones);
                    continue;
                }

                RaycastHit groundHit;
                if (TerrainUtil.GetGroundInfo(position, out groundHit, 5f, LAYER_GROUND))
                {
                    suitablePosition = groundHit.point;

                    Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, groundHit.normal);
                    Quaternion randomYRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                    suitableRotation = surfaceRotation * randomYRotation;

                    return true;
                }
            }

            suitablePosition = Vector3.zero;
            suitableRotation = Quaternion.identity;
            return false;
        }

        private LegacyShelter SpawnLegacyShelter(Vector3 position, Quaternion rotation, BasePlayer player)
        {
            LegacyShelter shelter = GameManager.server.CreateEntity(PREFAB_LEGACY_SHELTER, position, rotation) as LegacyShelter;
            if (shelter == null)
                return null;

            shelter.OnPlaced(player);
            shelter.Spawn();

            shelter.InitializeHealth(_config.ShelterHealth, _config.ShelterHealth);
            shelter.SendNetworkUpdateImmediate();

            // Set the lock owner id to 0 to prevent the player from opening the shelter door.
            LegacyShelterDoor shelterDoor = shelter.GetChildDoor();
            if (shelterDoor != null)
            {
                shelterDoor.InitializeHealth(_config.ShelterHealth, _config.ShelterHealth);

                BaseLock baseLock = shelterDoor.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
                if (baseLock != null)
                    baseLock.OwnerID = 0;

                if (_config.Door.SkinIds != null && _config.Door.SkinIds.Count > 0)
                {
                    shelterDoor.skinID = _config.Door.SkinIds[Random.Range(0, _config.Door.SkinIds.Count)];
                }
            }

            EntityPrivilege entityPrivilege = shelter.GetEntityPrivilege();
            if (entityPrivilege != null)
            {
                entityPrivilege.authorizedPlayers.Clear();
                entityPrivilege.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }

            ShelterData shelterData = new ShelterData
            {
                RemovalTimer = Time.realtimeSinceStartup + _config.ShelterLifetimeSeconds
            };
            _storedData.Shelters[shelter.net.ID.Value] = shelterData;

            SpawnShelterInteriorEntities(shelter, shelterData);
            StartRemovalTimer(shelter, _config.ShelterLifetimeSeconds, shelterData);

            return shelter;
        }

        #endregion Shelters Spawning and Setup

        #region Shelter Spawn Notification

        private void NotifyOfShelterSpawn(LegacyShelter shelter, BasePlayer player)
        {
            if (_config.Notification.NotifySurroundingPlayersOfShelterSpawn)
            {
                List<BasePlayer> nearbyPlayers = Pool.Get<List<BasePlayer>>();
                Vis.Entities(shelter.transform.position, _config.Notification.RadiusForNotifyingNearbyPlayers, nearbyPlayers, LAYER_PLAYER, QueryTriggerInteraction.Ignore);

                foreach (BasePlayer nearbyPlayer in nearbyPlayers)
                {
                    if (nearbyPlayer == null)
                        continue;

                    if (_config.Notification.SendAsToast)
                        SendToast(nearbyPlayer, Lang.RaidableShelterSpawned);
                    else
                        SendMessage(nearbyPlayer, Lang.RaidableShelterSpawned);
                }

                Pool.FreeUnmanaged(ref nearbyPlayers);
            }
            else
            {
                if (_config.Notification.SendAsToast)
                    SendToast(player, Lang.RaidableShelterSpawned);
                else
                    SendMessage(player, Lang.RaidableShelterSpawned);
            }
        }

        #endregion Shelter Spawn Notification

        #region Interior Entities Spawning

        private void SpawnShelterInteriorEntities(LegacyShelter shelter, ShelterData shelterData)
        {
            Vector3 shelterCenter = shelter.transform.position;
            float spawnRadius = SPAWNABLE_AREA_RADIUS_INSIDE_SHELTER;

            foreach (InteriorEntityConfig interiorEntityConfig in _config.InteriorEntities)
            {
                int numberToSpawn = Random.Range(interiorEntityConfig.MinimumNumberToSpawn, interiorEntityConfig.MaximumNumberToSpawn + 1);

                for (int i = 0; i < numberToSpawn; i++)
                {
                    if (!ChanceSucceeded(interiorEntityConfig.SpawnChance))
                        continue;

                    Vector3 randomPosition;
                    int maxPositionAttempts = _config.NumberOfAttemptsForDeterminingEntityPositionInsideShelter;
                    int maxRotationAttempts = _config.NumberOfAttemptsForDeterminingEntityRotationInsideShelter;

                    for (int posAttempt = 0; posAttempt < maxPositionAttempts; posAttempt++)
                    {
                        randomPosition = TerrainUtil.GetRandomPositionAround(shelterCenter, minimumRadius: 0f, maximumRadius: spawnRadius);

                        RaycastHit groundHit;
                        if (!TerrainUtil.GetGroundInfo(randomPosition, out groundHit, 2f, LAYER_GROUND))
                            continue;

                        randomPosition = groundHit.point;

                        for (int rotAttempt = 0; rotAttempt < maxRotationAttempts; rotAttempt++)
                        {
                            Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, groundHit.normal);
                            Quaternion randomYRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                            Quaternion finalRotation = surfaceRotation * randomYRotation;

                            if (!EntityFitsInShelter(shelter, interiorEntityConfig.PrefabName, randomPosition, finalRotation))
                                continue;

                            if (ExposedHook.OnShelterInteriorEntitySpawn(shelter, interiorEntityConfig.PrefabName, randomPosition, finalRotation))
                                continue;

                            BaseEntity entity = SpawnInteriorEntity(shelter, randomPosition, finalRotation, interiorEntityConfig);
                            if (entity == null)
                                continue;

                            shelterData.InteriorEntities.Add(entity.net.ID.Value);
                            ExposedHook.OnShelterInteriorEntitySpawned(shelter, entity);

                            posAttempt = maxPositionAttempts;
                            break;
                        }
                    }
                }
            }
        }

        private bool EntityFitsInShelter(LegacyShelter shelter, string prefabPath, Vector3 potentialPosition, Quaternion potentialRotation)
        {
            BaseEntity tempEntity = GameManager.server.CreateEntity(prefabPath, potentialPosition, potentialRotation, false);
            if (tempEntity == null)
                return false;

            OBB bounds = tempEntity.WorldSpaceBounds();
            tempEntity.Kill();

            Vector3[] corners = new Vector3[8];
            corners[0] = bounds.GetPoint(-1f, -1f, -1f);
            corners[1] = bounds.GetPoint(-1f, -1f, 1f);
            corners[2] = bounds.GetPoint(-1f, 1f, -1f);
            corners[3] = bounds.GetPoint(-1f, 1f, 1f);
            corners[4] = bounds.GetPoint(1f, -1f, -1f);
            corners[5] = bounds.GetPoint(1f, -1f, 1f);
            corners[6] = bounds.GetPoint(1f, 1f, -1f);
            corners[7] = bounds.GetPoint(1f, 1f, 1f);

            // Ensure the entity is within the shelter bounds and not clipping through or colliding with other objects inside.
            Collider[] colliders = Physics.OverlapBox(bounds.position, bounds.extents, bounds.rotation, LAYER_ENTITIES);
            foreach (Collider collider in colliders)
            {
                if (collider.gameObject != tempEntity.gameObject && collider.gameObject != shelter.gameObject)
                    return false;
            }

            return true;
        }

        private BaseEntity SpawnInteriorEntity(LegacyShelter shelter, Vector3 position, Quaternion rotation, InteriorEntityConfig interiorEntityConfig)
        {
            BaseEntity entity = GameManager.server.CreateEntity(interiorEntityConfig.PrefabName, position, rotation);
            if (entity == null)
                return null;

            if (interiorEntityConfig.SkinIds != null && interiorEntityConfig.SkinIds.Count > 0)
            {
                entity.skinID = interiorEntityConfig.SkinIds[Random.Range(0, interiorEntityConfig.SkinIds.Count)];
            }

            entity.Spawn();
            RemoveProblematicComponents(entity);

            if (entity is StorageContainer storageContainer)
                PopulateItems(storageContainer.inventory, _config.ItemsToSpawnInsideEntityContainers, interiorEntityConfig.PercentageToFillContainerWithItemsIfPresent);

            return entity;
        }

        #endregion Interior Entities Spawning

        #region Entity Containers Filling

        private void PopulateItems(ItemContainer itemContainer, List<ItemInfo> items, int fillContainerPercentage)
        {
            Shuffle(items);
            int slotsToFill = Mathf.CeilToInt(itemContainer.capacity * (fillContainerPercentage / 100f));

            for (int i = 0; i < slotsToFill; i++)
            {
                if (i >= items.Count)
                    break;

                ItemInfo itemInfo = items[i];
                var itemDefinition = ItemManager.FindItemDefinition(itemInfo.ShortName);
                if (itemDefinition != null)
                {
                    int amountToAdd = Random.Range(itemInfo.MinimumAmount, itemInfo.MaximumAmount + 1);
                    Item item = ItemManager.Create(itemDefinition, amountToAdd, itemInfo.SkinId);
                    if (!item.MoveToContainer(itemContainer))
                    {
                        item.Remove();
                    }
                }
            }
        }

        #endregion Entity Container Filling

        #region Turret Deployment and Setup

        private void DeployAutoTurret(LegacyShelter shelter)
        {
            AutoTurret autoTurret = SpawnAutoTurret(shelter, _autoTurretPosition, Quaternion.Euler(_autoTurretRotation));
            if (autoTurret != null)
            {
                AddWeaponToTurret(autoTurret);

                LoadTurretWithReserveAmmo(autoTurret.inventory);
                autoTurret.UpdateTotalAmmo();
                autoTurret.EnsureReloaded();

                autoTurret.SetPeacekeepermode(_config.Turret.Peacekeeper);
                autoTurret.InitiateStartup();

                autoTurret.SendNetworkUpdate();
            }
        }

        private AutoTurret SpawnAutoTurret(LegacyShelter shelter, Vector3 position, Quaternion rotation)
        {
            AutoTurret autoTurret = GameManager.server.CreateEntity(PREFAB_AUTO_TURRET, position, rotation) as AutoTurret;
            if (autoTurret == null)
                return null;

            autoTurret.SetParent(shelter);
            autoTurret.Spawn();

            autoTurret.InitializeHealth(_config.Turret.Health, _config.Turret.Health);
            autoTurret.SendNetworkUpdateImmediate();

            RemoveProblematicComponents(autoTurret);
            HideIOInputsAndOutputs(autoTurret);

            return autoTurret;
        }

        private Item AddWeaponToTurret(AutoTurret autoTurret)
        {
            Item item = ItemManager.CreateByName(_config.Turret.WeaponShortName);
            if (item == null)
                return null;

            if (_config.Turret.AttachmentShortNames != null)
            {
                foreach (string attachmentShortname in _config.Turret.AttachmentShortNames)
                {
                    var attachmentItem = ItemManager.CreateByName(attachmentShortname);
                    if (attachmentItem != null)
                    {
                        if (!attachmentItem.MoveToContainer(item.contents))
                        {
                            attachmentItem.Remove();
                        }
                    }
                }
            }

            if (!item.MoveToContainer(autoTurret.inventory, 0))
            {
                item.Remove();
                return null;
            }

            BaseProjectile weapon = item.GetHeldEntity() as BaseProjectile;
            if (weapon != null)
            {
                if (_config.Turret.AttachmentShortNames != null)
                {
                    // Ensures the weapon's magazine capacity reflects the modifications applied, such as the extended magazine mod.
                    weapon.DelayedModsChanged();
                    weapon.CancelInvoke(weapon.DelayedModsChanged);
                }

                autoTurret.UpdateAttachedWeapon();
                autoTurret.CancelInvoke(autoTurret.UpdateAttachedWeapon);

                if (_config.Turret.ClipAmmo != null)
                {
                    ItemDefinition loadedAmmoItemDefinition = ItemManager.FindItemDefinition(_config.Turret.ClipAmmo.ShortName);
                    if (loadedAmmoItemDefinition != null)
                    {
                        weapon.primaryMagazine.ammoType = loadedAmmoItemDefinition;

                        int clipAmmoAmount = UnityEngine.Random.Range(_config.Turret.ClipAmmo.MinimumAmount, _config.Turret.ClipAmmo.MaximumAmount + 1);
                        weapon.primaryMagazine.contents = Mathf.Min(weapon.primaryMagazine.capacity, clipAmmoAmount);
                    }
                }
            }

            return item;
        }

        private void LoadTurretWithReserveAmmo(ItemContainer autoTurretContainer)
        {
            if (_config.Turret.ReserveAmmo == null)
                return;

            // Starting from slot 1, because slot 0 is reserved for the weapon.
            int currentSlot = 1;
            int maximumAvailableSlots = autoTurretContainer.capacity - 1;

            foreach (ItemInfo ammo in _config.Turret.ReserveAmmo)
            {
                if (currentSlot > maximumAvailableSlots)
                    break;

                if (ammo.MinimumAmount <= 0 && ammo.MaximumAmount <= 0)
                    continue;

                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(ammo.ShortName);
                if (itemDefinition == null)
                    continue;

                int reserveAmmoAmount = UnityEngine.Random.Range(ammo.MinimumAmount, ammo.MaximumAmount + 1);

                int amountToAdd = Math.Min(reserveAmmoAmount, itemDefinition.stackable);
                Item ammoItem = ItemManager.Create(itemDefinition, amountToAdd);
                if (!ammoItem.MoveToContainer(autoTurretContainer, currentSlot))
                {
                    ammoItem.Remove();
                }

                if (ammoItem.parent != autoTurretContainer)
                {
                    Item destinationItem = autoTurretContainer.GetSlot(currentSlot);
                    if (destinationItem != null)
                    {
                        destinationItem.amount = amountToAdd;
                        destinationItem.MarkDirty();
                    }

                    ammoItem.Remove();
                }

                currentSlot++;
            }
        }

        #endregion Turret Deployment and Setup

        #region Traps Spawning

        private void SpawnTrapsAroundShelter(LegacyShelter shelter)
        {
            Vector3 shelterPosition = shelter.transform.position;
            int numberOfTraps = Random.Range(_config.Trap.MinimumNumberOfTrapsToSpawn, _config.Trap.MaximumNumberOfTrapsToSpawn + 1);

            for (int i = 0; i < numberOfTraps; i++)
            {
                Vector3 randomPosition = TerrainUtil.GetRandomPositionAround(shelterPosition, _config.Trap.MinimumSpawnRadiusAroundShelter, _config.Trap.MaximumSpawnRadiusAroundShelter);
                
                RaycastHit groundHit;
                if (!TerrainUtil.GetGroundInfo(randomPosition, out groundHit, 5f, LAYER_GROUND))
                    continue;

                randomPosition = groundHit.point;
                Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, groundHit.normal);

                string trapPrefab = GetRandomTrapPrefab();
                if (string.IsNullOrEmpty(trapPrefab))
                    continue;

                BaseEntity trapEntity = GameManager.server.CreateEntity(trapPrefab, randomPosition, surfaceRotation);
                if (trapEntity != null)
                {
                    trapEntity.SetParent(shelter, true);
                    trapEntity.Spawn();
                    RemoveProblematicComponents(trapEntity);
                }
            }
        }

        private string GetRandomTrapPrefab()
        {
            if (_config.Trap.SpawnLandmines && _config.Trap.SpawnBearTraps)
            {
                if (CoinFlip())
                    return PREFAB_LANDMINE;
                else
                    return PREFAB_BEAR_TRAP;
            }
            else if (_config.Trap.SpawnLandmines)
            {
                return PREFAB_LANDMINE;
            }
            else if (_config.Trap.SpawnBearTraps)
            {
                return PREFAB_BEAR_TRAP;
            }

            return null;
        }

        #endregion Traps Spawning

        #region Shelters Removal

        private void ResumeShelterRemovalTimers()
        {
            foreach (var kvp in _storedData.Shelters)
            {
                ShelterData shelterData = kvp.Value;
                LegacyShelter shelter = FindEntityById(kvp.Key) as LegacyShelter;

                if (shelter != null)
                {
                    double remainingTime = shelterData.RemovalTimer - Time.realtimeSinceStartup;
                    StartRemovalTimer(shelter, (float)remainingTime, shelterData);
                }
            }
        }

        private void StartRemovalTimer(LegacyShelter shelter, float lifetimeSeconds, ShelterData shelterData)
        {
            ulong shelterId = shelter != null && shelter.net != null ? shelter.net.ID.Value : 0;

            if (lifetimeSeconds <= 0)
            {
                if (shelter != null)
                    shelter.Kill();

                if (shelterData != null)
                {
                    foreach (ulong entityId in shelterData.InteriorEntities)
                    {
                        BaseEntity entity = FindEntityById(entityId);
                        if (entity != null)
                            entity.Kill();
                    }
                }

                if (_storedData.Shelters.ContainsKey(shelterId))
                {
                    _storedData.Shelters.Remove(shelterId);
                    DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
                }

                return;
            }

            if (shelter != null)
            {
                shelterData.RemovalTimer = Time.realtimeSinceStartup + lifetimeSeconds;
                DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);

                timer.Once(lifetimeSeconds, () =>
                {
                    if (shelter != null)
                        shelter.Kill();

                    if (shelterData != null)
                    {
                        foreach (ulong entityId in shelterData.InteriorEntities)
                        {
                            BaseEntity entity = FindEntityById(entityId);
                            if (entity != null)
                                entity.Kill();
                        }
                    }

                    if (_storedData.Shelters.ContainsKey(shelterId))
                    {
                        _storedData.Shelters.Remove(shelterId);
                        DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
                    }
                });
            }
        }

        private void KillAllShelters()
        {
            foreach (var kvp in _storedData.Shelters)
            {
                ShelterData shelterData = kvp.Value;
                LegacyShelter shelter = BaseNetworkable.serverEntities.Find(new NetworkableId(kvp.Key)) as LegacyShelter;
                if (shelter != null)
                {
                    foreach (ulong entityId in shelterData.InteriorEntities)
                    {
                        BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityId)) as BaseEntity;
                        if (entity != null)
                            entity.Kill();
                    }

                    shelter.Kill();
                }
            }

            _storedData.Shelters.Clear();
            DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
        }

        #endregion Shelters Removal

        #region Debug

        private void DrawDebugInfo(Vector3 position, string info, Color color, float radius = 0.5f)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsAdmin)
                    continue;

                DrawUtil.Text(player, 10f, color, position + Vector3.up * 1f, $"<size=30>{info}</size>");
                DrawUtil.Sphere(player, 10f, color, position, radius);
            }
        }

        #endregion Debug

        #region Exposed Hooks

        private static class ExposedHook
        {
            public static bool OnShelterInteriorEntitySpawn(LegacyShelter shelter, string prefabName, Vector3 position, Quaternion rotation)
            {
                object hookResult = Interface.CallHook("OnShelterInteriorEntitySpawn", shelter, prefabName, position, rotation);
                return hookResult is bool && (bool)hookResult == false;
            }
            
            public static void OnShelterInteriorEntitySpawned(LegacyShelter shelter, BaseEntity entity)
            {
                Interface.CallHook("OnShelterInteriorEntitySpawned", shelter, entity);
            }
        }

        #endregion Exposed Hooks

        #region API
        
        private bool API_IsShelterRaidable(LegacyShelter shelter)
        {
            return _storedData.Shelters.ContainsKey(shelter.net.ID.Value);
        }

        #endregion API

        #region Helper Functions

        private static bool CoinFlip()
        {
            return Random.Range(0, 2) == 0;
        }
       
        private BaseEntity FindEntityById(ulong id)
        {
            return BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
        }

        private static void HideIOInputsAndOutputs(IOEntity ioEntity)
        {
            foreach (var input in ioEntity.inputs)
                input.type = IOEntity.IOType.Generic;

            foreach (var output in ioEntity.outputs)
                output.type = IOEntity.IOType.Generic;
        }

        private static BaseEntity FindBaseEntityForPrefab(string prefabName)
        {
            var prefab = GameManager.server.FindPrefab(prefabName);
            if (prefab == null)
                return null;

            return prefab.GetComponent<BaseEntity>();
        }

        private static void Shuffle<T>(List<T> list)
        {
            int remainingItems = list.Count;

            while (remainingItems > 1)
            {
                remainingItems--;
                int randomIndex = _plugin._randomGenerator.Next(remainingItems + 1);

                T itemToSwap = list[randomIndex];
                list[randomIndex] = list[remainingItems];
                list[remainingItems] = itemToSwap;
            }
        }

        private static void RemoveProblematicComponents(BaseEntity entity)
        {
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
        }

        private static bool ChanceSucceeded(int percentage)
        {
            return Random.Range(0, 100) < percentage;
        }

        private static Color ParseColor(string hexColor)
        {
            if (ColorUtility.TryParseHtmlString(hexColor, out Color color))
                return color;

            return Color.white;
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class TerrainUtil
        {
            public static bool OnTopology(Vector3 position, TerrainTopology.Enum topology)
            {
                return (TerrainMeta.TopologyMap.GetTopology(position) & (int)topology) != 0;
            }

            public static bool OnRoadOrRail(Vector3 position)
            {
                var combinedTopology = TerrainTopology.Enum.Road | TerrainTopology.Enum.Roadside |
                                   TerrainTopology.Enum.Rail | TerrainTopology.Enum.Railside;

                return OnTopology(position, combinedTopology);
            }

            public static bool OnTerrain(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Terrain, QueryTriggerInteraction.Ignore);
            }

            public static bool InNoBuildZone(Vector3 position, float radius)
            {
                return Physics.CheckSphere(position, radius, Layers.Mask.Prevent_Building);
            }

            public static bool InWater(Vector3 position)
            {
                return WaterLevel.Test(position, false, false);
            }

            public static bool InsideRock(Vector3 position, float radius)
            {
                List<Collider> colliders = Pool.Get<List<Collider>>();
                Vis.Colliders(position, radius, colliders, Layers.Mask.World, QueryTriggerInteraction.Ignore);

                bool result = false;

                foreach (Collider collider in colliders)
                {
                    if (collider.name.Contains("rock", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("cliff", CompareOptions.OrdinalIgnoreCase)
                        || collider.name.Contains("formation", CompareOptions.OrdinalIgnoreCase))
                    {
                        result = true;
                        break;
                    }
                }

                Pool.FreeUnmanaged(ref colliders);
                return result;
            }

            public static bool InRadTown(Vector3 position, bool shouldDisplayOnMap = false)
            {
                foreach (var monumentInfo in TerrainMeta.Path.Monuments)
                {
                    bool inBounds = monumentInfo.IsInBounds(position);

                    bool hasLandMarker = true;
                    if (shouldDisplayOnMap)
                        hasLandMarker = monumentInfo.shouldDisplayOnMap;

                    if (inBounds && hasLandMarker)
                        return true;
                }

                return OnTopology(position, TerrainTopology.Enum.Monument);
            }

            public static bool HasEntityNearby(Vector3 position, float radius, LayerMask mask, string prefabName = null)
            {
                List<Collider> hitColliders = Pool.Get<List<Collider>>();
                GamePhysics.OverlapSphere(position, radius, hitColliders, mask, QueryTriggerInteraction.Ignore);

                bool hasEntityNearby = false;
                foreach (Collider collider in hitColliders)
                {
                    BaseEntity entity = collider.gameObject.ToBaseEntity();
                    if (entity != null)
                    {
                        if (prefabName == null || entity.PrefabName == prefabName)
                        {
                            hasEntityNearby = true;
                            break;
                        }
                    }
                }

                Pool.FreeUnmanaged(ref hitColliders);
                return hasEntityNearby;
            }

            public static Vector3 GetRandomPositionAround(Vector3 center, float minimumRadius, float maximumRadius, bool adjustToWaterHeight = false)
            {
                Vector3 randomDirection = Random.onUnitSphere;
                float randomDistance = Random.Range(minimumRadius, maximumRadius);
                Vector3 randomPosition = center + randomDirection * randomDistance;

                if (adjustToWaterHeight)
                    randomPosition.y = TerrainMeta.WaterMap.GetHeight(randomPosition);
                else
                    randomPosition.y = TerrainMeta.HeightMap.GetHeight(randomPosition);

                return randomPosition;
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask)
            {
                return Physics.Linecast(startPosition + new Vector3(0.0f, range, 0.0f), startPosition - new Vector3(0.0f, range, 0.0f), out raycastHit, mask);
            }

            public static bool GetGroundInfo(Vector3 startPosition, out RaycastHit raycastHit, float range, LayerMask mask, Transform ignoreTransform = null)
            {
                startPosition.y += 0.25f;
                range += 0.25f;
                raycastHit = default;

                RaycastHit hit;
                if (!GamePhysics.Trace(new Ray(startPosition, Vector3.down), 0f, out hit, range, mask, QueryTriggerInteraction.UseGlobal, null))
                    return false;

                if (ignoreTransform != null && hit.collider != null
                    && (hit.collider.transform == ignoreTransform || hit.collider.transform.IsChildOf(ignoreTransform)))
                {
                    return GetGroundInfo(startPosition - new Vector3(0f, 0.01f, 0f), out raycastHit, range, mask, ignoreTransform);
                }

                raycastHit = hit;
                return true;
            }
        }

        private static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static bool HasPlayerNearby(Vector3 position, float radius)
            {
                return BaseNetworkable.HasCloseConnections(position, radius);
            }

            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }

            public static bool Sleeping(BasePlayer player)
            {
                return player != null && player.IsSleeping();
            }

            public static bool Swimming(BasePlayer player)
            {
                return player != null && player.IsSwimming();
            }

            public static bool Boating(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Boating;
            }

            public static bool Flying(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Flying;
            }

            public static bool Driving(BasePlayer player)
            {
                return player != null && player.isMounted && player.GetMounted().mountTimeStatType == BaseMountable.MountStatType.Driving;
            }

            public static bool InBase(BasePlayer player)
            {
                return player != null && player.IsBuildingAuthed();
            }

            public static bool NearEnemyBase(BasePlayer player)
            {
                return player != null && player.IsNearEnemyBase();
            }

            public static bool Wounded(BasePlayer player)
            {
                return player != null && player.IsWounded();
            }

            public static bool Crawling(BasePlayer player)
            {
                return player != null && player.IsCrawling();
            }

            public static bool OnGround(BasePlayer player)
            {
                return player != null && player.IsOnGround();
            }
        }

        public static class CoroutineUtil
        {
            private static readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

            public static void StartCoroutine(string coroutineName, IEnumerator coroutineFunction)
            {
                StopCoroutine(coroutineName);

                Coroutine coroutine = ServerMgr.Instance.StartCoroutine(coroutineFunction);
                _activeCoroutines[coroutineName] = coroutine;
            }

            public static void StopCoroutine(string coroutineName)
            {
                if (_activeCoroutines.TryGetValue(coroutineName, out Coroutine coroutine))
                {
                    if (coroutine != null)
                        ServerMgr.Instance.StopCoroutine(coroutine);

                    _activeCoroutines.Remove(coroutineName);
                }
            }

            public static void StopAllCoroutines()
            {
                foreach (string coroutineName in _activeCoroutines.Keys.ToArray())
                {
                    StopCoroutine(coroutineName);
                }
            }
        }

        public static class DrawUtil
        {
            public static void Box(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.box", durationSeconds, color, position, radius);
            }

            public static void Sphere(BasePlayer player, float durationSeconds, Color color, Vector3 position, float radius)
            {
                player.SendConsoleCommand("ddraw.sphere", durationSeconds, color, position, radius);
            }

            public static void Line(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition)
            {
                player.SendConsoleCommand("ddraw.line", durationSeconds, color, fromPosition, toPosition);
            }

            public static void Arrow(BasePlayer player, float durationSeconds, Color color, Vector3 fromPosition, Vector3 toPosition, float headSize)
            {
                player.SendConsoleCommand("ddraw.arrow", durationSeconds, color, fromPosition, toPosition, headSize);
            }

            public static void Text(BasePlayer player, float durationSeconds, Color color, Vector3 position, string text)
            {
                player.SendConsoleCommand("ddraw.text", durationSeconds, color, position, text);
            }
        }

        #endregion Helper Classes

        #region Commands

        private static class Cmd
        {
            public const string TEST = "rs.test";
        }

        [ChatCommand(Cmd.TEST)]
        private void cmdTest(BasePlayer player, string cmd, string[] args)
        {
            if (player == null || !player.IsAdmin)
                return;
            
            RaycastHit groundHit;
            if (TerrainUtil.GetGroundInfo(player.transform.position, out groundHit, 10f, LAYER_GROUND))
            {
                Vector3 spawnPosition = groundHit.point;

                Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, groundHit.normal);
                Quaternion randomYRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                Quaternion spawnRotation = surfaceRotation * randomYRotation;

                LegacyShelter shelter = SpawnLegacyShelter(spawnPosition, spawnRotation, player);
                if (shelter != null)
                {
                    SendMessage(player, Lang.TestShelterSpawned);
                }
            }
        } 

        #endregion Commands

        #region Localization

        private class Lang
        {
            public const string RaidableShelterSpawned = "RaidableShelterSpawned";
            public const string TestShelterSpawned = "TestShelterSpawned";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.RaidableShelterSpawned] = "A raidable shelter has spawned nearby!",
                [Lang.TestShelterSpawned] = "A test shelter has been spawned at your location."
            }, this, "en");
        }

        private void SendMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            SendReply(player, message);
        }

        private void SendToast(BasePlayer player, string messageKey, GameTip.Styles style = GameTip.Styles.Blue_Normal, params object[] args)
        {
            string message = lang.GetMessage(messageKey, this, player.UserIDString);
            if (args.Length > 0)
                message = string.Format(message, args);

            player.SendConsoleCommand("gametip.showtoast", (int)style, message);
        }

        #endregion Localization
    }
}