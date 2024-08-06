/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Rust;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Raidable Shelters", "VisEntities", "1.2.0")]
    [Description("Spawns shelters filled with loot for players to raid.")]
    public class RaidableShelters : RustPlugin
    {
        #region Fields

        private static RaidableShelters _plugin;
        private static Configuration _config;
        private StoredData _storedData;

        private System.Random _randomGenerator = new System.Random();
        private Timer _sheltersRespawnTimer;
        
        private const int LAYER_TERRAIN = Layers.Mask.Terrain;
        private const int LAYER_PLAYER = Layers.Mask.Player_Server;
        private const int LAYER_ENTITIES = Layers.Mask.Deployed | Layers.Mask.Construction;
        
        private const string PREFAB_LEGACY_SHELTER = "assets/prefabs/building/legacy.shelter.wood/legacy.shelter.wood.deployed.prefab";
        private const float SPAWNABLE_AREA_RADIUS_INSIDE_SHELTER = 1.7f;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

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

            [JsonProperty("Shelter Lifetime Seconds")]
            public float ShelterLifetimeSeconds { get; set; }

            [JsonProperty("Number Of Attempts For Determining Entity Position Inside Shelter")]
            public int NumberOfAttemptsForDeterminingEntityPositionInsideShelter { get; set; }

            [JsonProperty("Number Of Attempts For Determining Entity Rotation Inside Shelter")]
            public int NumberOfAttemptsForDeterminingEntityRotationInsideShelter { get; set; }

            [JsonProperty("Door")]
            public DoorConfig Door { get; set; }

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

            [JsonProperty("Minimum Number To Spawn")]
            public int MinimumNumberToSpawn { get; set; }

            [JsonProperty("Maximum Number To Spawn")]
            public int MaximumNumberToSpawn { get; set; }

            [JsonProperty("Percentage To Fill Container With Items If Present")]
            public int PercentageToFillContainerWithItemsIfPresent { get; set; }
        }

        public class ItemInfo
        {
            [JsonProperty("Shortname")]
            public string Shortname { get; set; }

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

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                NumberOfAttemptsToFindShelterPositionNearPlayers = 5,
                MinimumSearchRadiusForShelterPositionAroundPlayer = 20f,
                MaximumSearchRadiusForShelterPositionAroundPlayer = 50f,
                NearbyEntitiesAvoidanceRadius = 6f,
                RocksAvoidanceRadius = 5f,
                DistanceFromNoBuildZones = 10f,
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
                        MinimumNumberToSpawn = 1,
                        MaximumNumberToSpawn = 1,
                        PercentageToFillContainerWithItemsIfPresent = 0,
                    },
                },
                ItemsToSpawnInsideEntityContainers = new List<ItemInfo>()
                {
                    new ItemInfo
                    {
                        Shortname = "fat.animal",
                        SkinId = 0,
                        MinimumAmount = 10,
                        MaximumAmount = 25,
                    },
                    new ItemInfo
                    {
                        Shortname = "cloth",
                        SkinId = 0,
                        MinimumAmount = 20,
                        MaximumAmount = 30,
                    },
                    new ItemInfo
                    {
                        Shortname = "wood",
                        SkinId = 0,
                        MinimumAmount = 200,
                        MaximumAmount = 400,
                    },
                    new ItemInfo
                    {
                        Shortname = "syringe.medical",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        Shortname = "rope",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 3,
                    },
                    new ItemInfo
                    {
                        Shortname = "cctv.camera",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "roadsigns",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        Shortname = "stones",
                        SkinId = 0,
                        MinimumAmount = 150,
                        MaximumAmount = 350,
                    },
                    new ItemInfo
                    {
                        Shortname = "metal.fragments",
                        SkinId = 0,
                        MinimumAmount = 30,
                        MaximumAmount = 90,
                    },
                    new ItemInfo
                    {
                        Shortname = "ammo.grenadelauncher.he",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 2,
                    },
                    new ItemInfo
                    {
                        Shortname = "coffeecan.helmet",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "scrap",
                        SkinId = 0,
                        MinimumAmount = 10,
                        MaximumAmount = 25,
                    },
                    new ItemInfo
                    {
                        Shortname = "icepick.salvaged",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "ptz.cctv.camera",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "corn",
                        SkinId = 0,
                        MinimumAmount = 3,
                        MaximumAmount = 5,
                    },
                    new ItemInfo
                    {
                        Shortname = "ammo.rocket.mlrs",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "wall.frame.garagedoor",
                        SkinId = 0,
                        MinimumAmount = 1,
                        MaximumAmount = 1,
                    },
                    new ItemInfo
                    {
                        Shortname = "pistol.revolver",
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
                            NotifyOfShelterSpawn(shelter, player);
                    }
                }

                yield return CoroutineEx.waitForSeconds(_config.DelayBetweenEachShelterSpawnSeconds);
            }
        }

        private bool TryFindSuitableShelterSpawnPoint(Vector3 center, float minSearchRadius, float maxSearchRadius, int maxAttempts, out Vector3 suitablePosition, out Quaternion suitableRotation)
        {
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vector3 position = TerrainUtil.GetRandomPositionAround(center, minSearchRadius, maxSearchRadius);

                if (!TerrainUtil.InsideRock(position, _config.RocksAvoidanceRadius) && !TerrainUtil.InRadTown(position)
                    && !TerrainUtil.HasEntityNearby(position, _config.NearbyEntitiesAvoidanceRadius, LAYER_ENTITIES)
                    && !PlayerUtil.HasPlayerNearby(position, _config.NearbyEntitiesAvoidanceRadius)
                    && !TerrainUtil.InWater(position) && !TerrainUtil.OnRoadOrRail(position)
                    && TerrainUtil.OnTerrain(position, 4f) && !TerrainUtil.InNoBuildZone(position, _config.DistanceFromNoBuildZones))
                {
                    RaycastHit groundHit;
                    if (TerrainUtil.GetGroundInfo(position, out groundHit, 5f, LAYER_TERRAIN))
                    {
                        suitablePosition = groundHit.point;

                        Quaternion surfaceRotation = Quaternion.FromToRotation(Vector3.up, groundHit.normal);
                        Quaternion randomYRotation = Quaternion.Euler(0, Random.Range(0, 360), 0);
                        suitableRotation = surfaceRotation * randomYRotation;

                        return true;
                    }
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

            // Set the lock owner id to 0 to prevent the player from opening the shelter door.
            LegacyShelterDoor shelterDoor = shelter.GetChildDoor();
            if (shelterDoor != null)
            {
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
                List<BasePlayer> nearbyPlayers = Pool.GetList<BasePlayer>();
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

                Pool.FreeList(ref nearbyPlayers);
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
                    Vector3 randomPosition;
                    int maxPositionAttempts = _config.NumberOfAttemptsForDeterminingEntityPositionInsideShelter;
                    int maxRotationAttempts = _config.NumberOfAttemptsForDeterminingEntityRotationInsideShelter;

                    for (int posAttempt = 0; posAttempt < maxPositionAttempts; posAttempt++)
                    {
                        randomPosition = TerrainUtil.GetRandomPositionAround(shelterCenter, minimumRadius: 0f, maximumRadius: spawnRadius);

                        RaycastHit groundHit;
                        if (!TerrainUtil.GetGroundInfo(randomPosition, out groundHit, 2f, LAYER_TERRAIN))
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
                var itemDefinition = ItemManager.FindItemDefinition(itemInfo.Shortname);
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
            if (lifetimeSeconds <= 0)
            {
                ulong shelterId = shelter.net.ID.Value;

                if (shelter != null)
                    shelter.Kill();

                foreach (ulong entityId in shelterData.InteriorEntities)
                {
                    BaseEntity entity = FindEntityById(entityId);
                    if (entity != null)
                        entity.Kill();
                }

                _storedData.Shelters.Remove(shelterId);
                DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
                return;
            }

            shelterData.RemovalTimer = Time.realtimeSinceStartup + lifetimeSeconds;
            DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);

            timer.Once(lifetimeSeconds, () =>
            {
                ulong shelterId = shelter.net.ID.Value;

                if (shelter != null)
                {
                    foreach (ulong entityId in shelterData.InteriorEntities)
                    {
                        BaseEntity entity = FindEntityById(entityId);
                        if (entity != null)
                            entity.Kill();
                    }

                    shelter.Kill();
                }

                if (_storedData.Shelters.ContainsKey(shelterId))
                {
                    _storedData.Shelters.Remove(shelterId);
                    DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
                }
            });
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

        private BaseEntity FindEntityById(ulong id)
        {
            return BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
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
                return Physics.CheckSphere(position, radius, LAYER_TERRAIN, QueryTriggerInteraction.Ignore);
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
                List<Collider> colliders = Pool.GetList<Collider>();
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

                Pool.FreeList(ref colliders);
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
                List<Collider> hitColliders = Pool.GetList<Collider>();
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

                Pool.FreeList(ref hitColliders);
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

        #endregion Helper Classes

        #region Localization

        private class Lang
        {
            public const string RaidableShelterSpawned = "RaidableShelterSpawned";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.RaidableShelterSpawned] = "A raidable shelter has spawned nearby!",
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