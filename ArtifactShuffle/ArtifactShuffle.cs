using BepInEx;
using BepInEx.Configuration;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using RiskOfOptions;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Inventory = RoR2.Inventory;
using EntityStates.BrotherMonster;
using UnityEngine.Networking;
using static RoR2.SpawnCard;

[assembly: HG.Reflection.SearchableAttribute.OptIn]
namespace ArtifactShuffle {
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class ArtifactShuffle : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "TaranDev";
        public const string PluginName = "ArtifactShuffle";
        public const string PluginVersion = "1.0.0";

        public static ConfigFile config;

        public static ConfigEntry<float> secondsBetweenArtifactChange;

        public static ConfigEntry<float> minNumberOfArtifacts;

        public static ConfigEntry<float> maxNumberOfArtifacts;

        public static ConfigEntry<bool> sacrificeChangedEnabled;

        public static List<ArtifactConfig> artifactEnabledConfigEntries;

        System.Random rnd;
        static int handledMinute = -1;

        // Interactables that sacrifice should remove
        static List<SpawnCard.SpawnResult> sacrificeInteractables;
        static List<Dictionary<string, SpawnCard.SpawnResult>> devotionInteractables;
        static string droneDevotionKey = "drone";
        static string eggDevotionKey = "egg";

        static SpawnCard lumerianEgg;

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake()
        {
            Log.Init(Logger);
            config = Config;
            //configs();
            sacrificeInteractables = new List<SpawnCard.SpawnResult>();
            devotionInteractables = new List<Dictionary<string, SpawnCard.SpawnResult>>();
            rnd = new System.Random();
        }

        // Hooks
        private void OnEnable()
        {
            RunArtifactManager.onArtifactEnabledGlobal += OnArtifactEnabled;
            RunArtifactManager.onArtifactDisabledGlobal += OnArtifactDisabled;
            On.RoR2.InteractableSpawnCard.Spawn += InteractableSpawn;
            On.RoR2.SceneDirector.PopulateScene += OnPopulateScene;
            On.RoR2.CharacterMaster.OnBodyStart += OnBodyStart;
            On.RoR2.DevotionInventoryController.OnDevotionArtifactEnabled += OnDevotionArtifactEnabled;
            On.RoR2.DevotionInventoryController.OnDevotionArtifactDisabled += OnDevotionArtifactDisabled;

        }

        private void OnDisable()
        {
            RunArtifactManager.onArtifactEnabledGlobal -= OnArtifactEnabled;
            RunArtifactManager.onArtifactDisabledGlobal -= OnArtifactDisabled;
            On.RoR2.InteractableSpawnCard.Spawn -= InteractableSpawn;
            On.RoR2.SceneDirector.PopulateScene -= OnPopulateScene;
            On.RoR2.CharacterMaster.OnBodyStart -= OnBodyStart;
            On.RoR2.DevotionInventoryController.OnDevotionArtifactEnabled -= OnDevotionArtifactEnabled;
            On.RoR2.DevotionInventoryController.OnDevotionArtifactDisabled -= OnDevotionArtifactDisabled;
        }

        void OnDevotionArtifactEnabled(On.RoR2.DevotionInventoryController.orig_OnDevotionArtifactEnabled orig, RunArtifactManager runArtifactManager, ArtifactDef artifactDef) {
            if (DevotionInventoryController.lowLevelEliteBuffs.Count == 0) {
                DevotionInventoryController.lowLevelEliteBuffs.Add(RoR2Content.Equipment.AffixRed.equipmentIndex);
                DevotionInventoryController.lowLevelEliteBuffs.Add(RoR2Content.Equipment.AffixWhite.equipmentIndex);
                DevotionInventoryController.lowLevelEliteBuffs.Add(RoR2Content.Equipment.AffixBlue.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixRed.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixWhite.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixBlue.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixPoison.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixLunar.equipmentIndex);
                DevotionInventoryController.highLevelEliteBuffs.Add(RoR2Content.Equipment.AffixHaunted.equipmentIndex);
                if (DLC1Content.Elites.Earth.IsAvailable()) {
                    DevotionInventoryController.lowLevelEliteBuffs.Add(DLC1Content.Elites.Earth.eliteEquipmentDef.equipmentIndex);
                    DevotionInventoryController.highLevelEliteBuffs.Add(DLC1Content.Elites.Earth.eliteEquipmentDef.equipmentIndex);
                }
                Run.onRunDestroyGlobal += DevotionInventoryController.OnRunDestroy;
                DevotionInventoryController.s_effectPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/ItemTakenOrbEffect");
                BossGroup.onBossGroupDefeatedServer += DevotionInventoryController.OnBossGroupDefeatedServer;
            }
        }

        void OnDevotionArtifactDisabled(On.RoR2.DevotionInventoryController.orig_OnDevotionArtifactDisabled orig, RunArtifactManager runArtifactManager, ArtifactDef artifactDef) {
            return;
        }

        public struct ArtifactConfig {
            public readonly ConfigEntry<bool> configEntry;
            public readonly ArtifactDef artifactDef;
            public ArtifactConfig(ConfigEntry<bool> configEntry, ArtifactDef artifactDef) {
                this.configEntry = configEntry;
                this.artifactDef = artifactDef;
            }
        }

        // Initialising configs
        [SystemInitializer(typeof(ArtifactCatalog))]
        private static void configs() {

            artifactEnabledConfigEntries = new List<ArtifactConfig>();

            List<ArtifactDef> validArtifactDefs = ArtifactCatalog.artifactDefs.ToList();

            secondsBetweenArtifactChange = config.Bind("General", "Number of seconds between artifact change.", 120f, "Default is 120 (2 minutes).");
            ModSettingsManager.AddOption(new StepSliderOption(secondsBetweenArtifactChange,
                new StepSliderConfig
                {
                    min = 1f,
                    max = 600f,
                    increment = 1f
                }));

            secondsBetweenArtifactChange.SettingChanged += (o, args) => {
                if (!Run.instance) {
                    return;
                }

                handledMinute = (int)Math.Floor(RoR2.Run.instance.fixedTime / secondsBetweenArtifactChange.Value);
            };

            minNumberOfArtifacts = config.Bind("General", "Minimum number of artifacts randomly chosen.", 1f, "Default is 1.");
            ModSettingsManager.AddOption(new StepSliderOption(minNumberOfArtifacts,
                new StepSliderConfig
                {
                    min = 0f,
                    max = 16f,
                    increment = 1f
                }));

            maxNumberOfArtifacts = config.Bind("General", "Maximum number of artifacts randomly chosen.", 5f, "Default is 5.");
            ModSettingsManager.AddOption(new StepSliderOption(maxNumberOfArtifacts,
                new StepSliderConfig
                {
                    min = 0f,
                    max = (float) validArtifactDefs.Count,
                    increment = 1f
                }));

            sacrificeChangedEnabled = config.Bind("General", "Lock chests while sacrifice is active", true, "If chests should become locked while the artifact of sacrifice is active.\nDefault is true.");
            sacrificeChangedEnabled.SettingChanged += (o, args) => {
                if (!Run.instance) {
                    return;
                }

                if (TeleporterInteraction.instance && !sacrificeChangedEnabled.Value) {
                    // Unlock locked chests
                    unlockAllSacrificeInstances();
                } else if (TeleporterInteraction.instance && sacrificeChangedEnabled.Value && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.sacrificeArtifactDef)) {
                    // Lock unlocked chests
                    lockAllSacrificeInstances();
                }
            };
            ModSettingsManager.AddOption(new CheckBoxOption(sacrificeChangedEnabled));

            foreach (ArtifactDef artifactDef in validArtifactDefs) {
                Log.Info("Initialising artifact config: " + artifactDef.nameToken);
                String artifactName = Language.GetString(artifactDef.nameToken);
                ConfigEntry<bool> artifactEnabled = config.Bind("Enabled Artifacts", artifactName + " enabled", true, "If the " + artifactName + " should be in the pool of randomised artifacts.\nDefault is true.");
                artifactEnabled.SettingChanged += (o, args) => {
                    if (RunArtifactManager.instance) {
                        RunArtifactManager.instance.SetArtifactEnabled(artifactDef, false);
                    }
                };
                ModSettingsManager.AddOption(new CheckBoxOption(artifactEnabled));
                artifactEnabledConfigEntries.Add(new ArtifactConfig(artifactEnabled, artifactDef));
            }
        }

        // Initialising the interactables list
        static void InteractableSpawn(On.RoR2.InteractableSpawnCard.orig_Spawn orig, InteractableSpawnCard self, Vector3 position, Quaternion rotation, DirectorSpawnRequest directorSpawnRequest, ref SpawnCard.SpawnResult result) {
            orig(self, position, rotation, directorSpawnRequest, ref result);

            // If interactable shouldn't be spawned when sacrifice is active, add it to the list
            if (result.success && result.spawnedInstance) {
                if (self.skipSpawnWhenSacrificeArtifactEnabled) {
                    sacrificeInteractables.Add(result);
                } else if (self.skipSpawnWhenDevotionArtifactEnabled) {
                    Dictionary<string, SpawnResult> newDict = new Dictionary<string, SpawnResult>();

                    Log.Info("skipped: " + result.spawnedInstance);

                    newDict.Add(droneDevotionKey, result);

                    SpawnCard.SpawnResult lumerianSpawnCardResult = result;
                    directorSpawnRequest.spawnCard = lumerianEgg;

                    GameObject gameObject = Instantiate(lumerianEgg.prefab, position, rotation);
                    NetworkServer.Spawn(gameObject);
                    lumerianSpawnCardResult.spawnedInstance = gameObject;
                    lumerianSpawnCardResult.spawnRequest = directorSpawnRequest;
                    lumerianSpawnCardResult.success = true;

                    newDict.Add(eggDevotionKey, lumerianSpawnCardResult);

                    devotionInteractables.Add(newDict);

                    bool devotionEnabled = RunArtifactManager.instance.IsArtifactEnabled(CU8Content.Artifacts.Devotion);
                    if(devotionEnabled) {
                        // Hide drone
                        result.spawnedInstance.SetActive(false);
                    } else {
                        // Hide egg
                        lumerianSpawnCardResult.spawnedInstance.SetActive(false);
                    }
                }
            }
        }

        static void OnBodyStart(On.RoR2.CharacterMaster.orig_OnBodyStart orig, CharacterMaster self, CharacterBody body) {
            // Getting bodies health
            float health = body.healthComponent.Networkhealth;
            orig(self, body);
            // If health is not 0 or the default 100f, it was already set by metamorphosis override, so reset it
            if (health != 0 && health != 100f && body.isPlayerControlled) {
                body.healthComponent.Networkhealth = health;
            }
        }

        // After enter new stage
        private void OnPopulateScene(On.RoR2.SceneDirector.orig_PopulateScene orig, SceneDirector self) {
            lumerianEgg = self.lumerianEgg.spawnCard;
            // Clearing the list of sacrifice interactables
            sacrificeInteractables.Clear();
            devotionInteractables.Clear();

            // If sacrifice is active, disable it temporarily to get interactables that should spawn
            bool sacrificeEnabled = RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.sacrificeArtifactDef);
            bool devotionEnabled = RunArtifactManager.instance.IsArtifactEnabled(CU8Content.Artifacts.Devotion);

            // Terrible way of initialising devotion stuff
            RunArtifactManager.instance.SetArtifactEnabled(CU8Content.Artifacts.Devotion, true);
            RunArtifactManager.instance.SetArtifactEnabled(CU8Content.Artifacts.Devotion, false);

            if (sacrificeEnabled) {
                RunArtifactManager.instance.SetArtifactEnabled(RoR2Content.Artifacts.sacrificeArtifactDef, false);
            }

            orig(self);

            if(sacrificeEnabled) {
                RunArtifactManager.instance.SetArtifactEnabled(RoR2Content.Artifacts.sacrificeArtifactDef, true);
            }

            if (devotionEnabled) {
                RunArtifactManager.instance.SetArtifactEnabled(CU8Content.Artifacts.Devotion, true);
            }

        }

        // Specific artifact behaviour
        static void OnArtifactEnabled(RunArtifactManager runArtifactManager, ArtifactDef artifactDef) {
            if (Stage.instance) {
                if (artifactDef == RoR2Content.Artifacts.sacrificeArtifactDef) {
                    OnSacrificeEnabled();
                } else if (artifactDef == RoR2Content.Artifacts.randomSurvivorOnRespawnArtifactDef) {
                    OnMetamorphosisEnabled();
                } else if (artifactDef == RoR2Content.Artifacts.enigmaArtifactDef) {
                    OnEnigmaEnabled();
                } else if (artifactDef == RoR2Content.Artifacts.glassArtifactDef) {
                    OnGlassChanged();
                } else if (artifactDef == RoR2Content.Artifacts.singleMonsterTypeArtifactDef) {
                    OnKinChanged();
                } else if (artifactDef == RoR2Content.Artifacts.monsterTeamGainsItemsArtifactDef) {
                    OnEvolutionEnabled();
                } else if (artifactDef == CU8Content.Artifacts.Devotion) {
                    OnDevotionEnabled();
                }
            }
        }

        static void OnArtifactDisabled(RunArtifactManager runArtifactManager, ArtifactDef artifactDef) {
            if (Stage.instance) {
                if (artifactDef == RoR2Content.Artifacts.sacrificeArtifactDef) {
                    OnSacrificeDisabled();
                } else if (artifactDef == RoR2Content.Artifacts.glassArtifactDef) {
                    OnGlassChanged();
                } else if (artifactDef == RoR2Content.Artifacts.singleMonsterTypeArtifactDef) {
                    OnKinChanged();
                } else if (artifactDef == RoR2Content.Artifacts.monsterTeamGainsItemsArtifactDef) {
                    OnEvolutionDisabled();
                } else if (artifactDef == CU8Content.Artifacts.Devotion) {
                    OnDevotionDisabled();
                }
            }
        }

        static void OnDevotionEnabled() {
            if (!Run.instance) {
                return;
            }

            List<int> indexesToDelete = new List<int>();

            for (int i = 0; i < devotionInteractables.Count; i++) {
                GameObject drone = devotionInteractables[i][droneDevotionKey].spawnedInstance;
                GameObject egg = devotionInteractables[i][eggDevotionKey].spawnedInstance;
                
                if (drone == null) {
                    // If drone is used, delete and forget egg
                    indexesToDelete.Add(i);
                    Destroy(egg);
                    continue;
                } else if (egg == null) {
                    // If egg is used, delete and forget drone
                    indexesToDelete.Add(i);
                    Destroy(drone);
                    continue;
                } else {
                    drone.SetActive(false);
                    egg.SetActive(true);
                }
            }

            for (int i = indexesToDelete.Count - 1; i >= 0; i --) {
                devotionInteractables.RemoveAt(indexesToDelete[i]);
            }
        }

        static void OnDevotionDisabled() {
            List<int> indexesToDelete = new List<int>();

            for (int i = 0; i < devotionInteractables.Count; i++) {
                GameObject drone = devotionInteractables[i][droneDevotionKey].spawnedInstance;
                GameObject egg = devotionInteractables[i][eggDevotionKey].spawnedInstance;

                if (drone == null) {
                    // If drone is used, delete and forget egg
                    indexesToDelete.Add(i);
                    Destroy(egg);
                    continue;
                } else if (egg == null) {
                    // If egg is used, delete and forget drone
                    indexesToDelete.Add(i);
                    Destroy(drone);
                    continue;
                } else {
                    drone.SetActive(true);
                    egg.SetActive(false);
                }
            }

            for (int i = indexesToDelete.Count - 1; i >= 0; i--) {
                devotionInteractables.RemoveAt(indexesToDelete[i]);
            }
        }

        static void OnSacrificeEnabled() {
            if (!Run.instance) {
                return;
            }

            if(sacrificeChangedEnabled.Value) {
                lockAllSacrificeInstances();
            }
        }

        static void lockAllSacrificeInstances() {
            // If TP is charged
            if (TeleporterInteraction.instance.isCharged || TeleporterInteraction.instance.isInFinalSequence) {
                return;
            }

            // For all interactables that shouldn't exist when sacrifice is enabled
            for (int i = 0; i < sacrificeInteractables.Count; i++) {
                try {
                    SpawnCard.SpawnResult interactable = sacrificeInteractables[i];
                    GameObject obj = interactable.spawnedInstance;

                    if (!obj) {
                        continue;
                    }

                    List<PurchaseInteraction> purchases = new List<PurchaseInteraction>();

                    PurchaseInteraction purchase = obj.GetComponent<PurchaseInteraction>();

                    if (purchase) {
                        purchases.Add(purchase);
                    } else {
                        // If interactable doesn't have default purchase interaction, could be a multishop
                        MultiShopController multishopObject = obj.GetComponent<MultiShopController>();
                        if (multishopObject) {
                            foreach (GameObject terminal in multishopObject._terminalGameObjects) {
                                // Each multipshop terminal has it's own purchase interaction
                                PurchaseInteraction terminalPurchase = terminal.GetComponent<PurchaseInteraction>();
                                if (terminalPurchase) {
                                    purchases.Add(terminalPurchase);
                                }
                            }
                        } else {
                            continue;
                        }
                    }

                    // Lock purchase interactions
                    foreach (PurchaseInteraction p in purchases) {
                        if (p.available) {
                            Log.Info("Locking: " + p.name);
                            TeleporterInteraction.instance.outsideInteractableLocker.LockPurchasable(p);
                        }
                    }
                } catch {

                }
            }
        }

        static void OnSacrificeDisabled() {

            // If there is no run
            if (!Run.instance || !TeleporterInteraction.instance) {
                return;
            }

            if (sacrificeChangedEnabled.Value) {
                unlockAllSacrificeInstances();
            }
        }

        static void unlockAllSacrificeInstances() {
            // If TP is active
            if (TeleporterInteraction.instance.isIdleToCharging || TeleporterInteraction.instance.isCharging) {
                return;
            }

            // For all interactables that shouldn't exist when sacrifice is enabled
            for (int i = 0; i < sacrificeInteractables.Count; i++) {
                try {
                    SpawnCard.SpawnResult interactable = sacrificeInteractables[i];
                    GameObject obj = interactable.spawnedInstance;

                    if (!obj) {
                        continue;
                    }

                    List<PurchaseInteraction> purchases = new List<PurchaseInteraction>();

                    PurchaseInteraction purchase = obj.GetComponent<PurchaseInteraction>();

                    if (purchase) {
                        purchases.Add(purchase);
                    } else {
                        // If interactable doesn't have default purchase interaction, could be a multishop
                        MultiShopController multishopObject = obj.GetComponent<MultiShopController>();
                        if (multishopObject) {
                            foreach (GameObject terminal in multishopObject._terminalGameObjects) {
                                // Each multipshop terminal has it's own purchase interaction
                                PurchaseInteraction terminalPurchase = terminal.GetComponent<PurchaseInteraction>();
                                if (terminalPurchase) {
                                    purchases.Add(terminalPurchase);
                                }
                            }
                        } else {
                            continue;
                        }
                    }

                    // Unlock purchase interactions
                    foreach (PurchaseInteraction p in purchases) {
                        if (p.available) {
                            Log.Info("Unlocking: " + p.name);
                            TeleporterInteraction.instance.outsideInteractableLocker.UnlockPurchasable(p);
                        }
                    }
                } catch {

                }
            }
        }

        static void OnMetamorphosisEnabled() {
            if (!Run.instance) {
                return;
            }

            // Get all players
            IEnumerable<CharacterMaster> players = PlayerCharacterMasterController.instances.Where(p => p)
                                                            .Select(p => p.master)
                                                            .Where(m => m && !m.IsDeadAndOutOfLivesServer());

            // Respawn players as random character and keep the same health percentage
            foreach (CharacterMaster player in players) {
                float oldHealth = player.GetBody().healthComponent.combinedHealthFraction;
                oldHealth = Math.Min(oldHealth, 1f);
                BuffIndex curseBuffIndex = BuffCatalog.FindBuffIndex("bdPermanentCurse");
                float oldNumCurse = player.GetBody().GetBuffCount(curseBuffIndex);
                CharacterBody newPlayer = player.Respawn(player.GetBody().transform.position, player.GetBody().transform.rotation);
                newPlayer.RecalculateStats();
                newPlayer.MarkAllStatsDirty();
                newPlayer.AddBuff(curseBuffIndex);
                newPlayer.SetBuffCount(curseBuffIndex, (int)oldNumCurse);
                newPlayer.healthComponent.Networkhealth = newPlayer.healthComponent.fullHealth * oldHealth;
                newPlayer.RecalculateStats();
                newPlayer.MarkAllStatsDirty();
            }
        }

        static void OnEnigmaEnabled() {
            if (!Run.instance) {
                return;
            }

            // Get all players
            IEnumerable<CharacterMaster> players = PlayerCharacterMasterController.instances.Where(p => p)
                                                            .Select(p => p.master)
                                                            .Where(m => m && !m.IsDeadAndOutOfLivesServer());

            // Handle enigma stuff
            foreach (CharacterMaster player in players) {
                if(player.GetBody() && player.GetBody().inventory) {
                    RoR2.Artifacts.EnigmaArtifactManager.OnPlayerCharacterBodyStartServer(player.GetBody());
                }
            }
        }

        static void OnKinChanged() {
            if (!Run.instance) {
                return;
            }

            // Refreshing UI cos kin doesn't ??
            if (!RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.singleMonsterTypeArtifactDef)) {
                Stage.instance.singleMonsterTypeBodyIndex = BodyIndex.None;
            }

            RoR2.UI.EnemyInfoPanel.MarkDirty();
        }

        static void OnGlassChanged() {
            if (!Run.instance) {
                return;
            }

            // Get all allies
            IEnumerable<CharacterBody> allies = CharacterBody.readOnlyInstancesList.Where(c =>
            {
                if (!c || !c.teamComponent)
                    return false;

                switch (c.teamComponent.teamIndex) {
                    case TeamIndex.Player:
                        return true;
                    default:
                        return false;
                }
            });

            // Refresh health bar and set to full health
            foreach (CharacterBody ally in allies) {
                float oldHealth = ally.healthComponent.combinedHealthFraction;
                ally.RecalculateStats();
                ally.MarkAllStatsDirty();
                ally.healthComponent.Networkhealth = ally.healthComponent.fullHealth * oldHealth;
                ally.RecalculateStats();
                ally.MarkAllStatsDirty();
            }
        }

        static void OnEvolutionEnabled() {
            if (!Run.instance) {
                return;
            }

            // Get all enemies
            IEnumerable<CharacterBody> enemies = CharacterBody.readOnlyInstancesList.Where(c =>
            {
                if (!c || !c.teamComponent || c.isPlayerControlled)
                    return false;

                switch (c.teamComponent.teamIndex) {
                    case TeamIndex.Monster:
                    case TeamIndex.Lunar:
                    case TeamIndex.Void:
                        return true;
                    default:
                        return false;
                }
            });

            // Giving the items from evolution to enemies
            foreach (CharacterBody enemy in enemies) {
                enemy.inventory.AddItemsFrom(RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory);
            }

            
        }

        static void OnEvolutionDisabled() {
            if (!Run.instance) {
                return;
            }

            // Get all enemies
            IEnumerable<CharacterBody> enemies = CharacterBody.readOnlyInstancesList.Where(c =>
            {
                if (!c || !c.teamComponent || c.isPlayerControlled)
                    return false;

                switch (c.teamComponent.teamIndex) {
                    case TeamIndex.Monster:
                    case TeamIndex.Lunar:
                    case TeamIndex.Void:
                        return true;
                    default:
                        return false;
                }
            });

            // Taking the items from evolution away from enemies
            foreach (CharacterBody enemy in enemies) {
                Inventory inv = enemy.inventory;
                int[] monsterItemStacks = RoR2.Artifacts.MonsterTeamGainsItemsArtifactManager.monsterTeamInventory.itemStacks;

                for (ItemIndex itemIndex = (ItemIndex)0; (int)itemIndex < inv.itemStacks.Length; itemIndex++) {
                    int num = monsterItemStacks[(int)itemIndex];
                    if (num > 0 && Inventory.defaultItemCopyFilterDelegate(itemIndex)) {
                        int reference = inv.itemStacks[(int)itemIndex];
                        inv.RemoveItem(itemIndex, num);
                    }
                }
                inv.SetDirtyBit(1u);
                inv.SetDirtyBit(8u);
                inv.HandleInventoryChanged();
            }
        }

        private void Update()
        {

            if (!Run.instance) {
                if(handledMinute != -1) {
                    handledMinute = -1;
                }
                return;
            }

            int minute = (int) Math.Floor(RoR2.Run.instance.fixedTime / secondsBetweenArtifactChange.Value);
            // Refreshing UI
            RoR2.UI.EnemyInfoPanel.MarkDirty();
            if (minute > handledMinute) {

                List<ArtifactDef> validArtifactDefs = ArtifactCatalog.artifactDefs.ToList();

                // Remove artifacts with config set to disabled
                foreach (ArtifactConfig config in artifactEnabledConfigEntries) {
                    if(!config.configEntry.Value) {
                        validArtifactDefs.Remove(config.artifactDef);
                    }
                }
                int numValidArtifacts = validArtifactDefs.Count;

                handledMinute = minute;
                int min = (int) minNumberOfArtifacts.Value;
                int max = (int) maxNumberOfArtifacts.Value;

                if (max > numValidArtifacts) {
                    max = numValidArtifacts;
                }

                if(min > max) {
                    min = max;
                }
                int numArtifactsToEnable = rnd.Next(min, max);

                List<ArtifactDef> artifactsToEnable = new List<ArtifactDef>();

                // Get random artifacts to enable
                for (int i = 0; i < numArtifactsToEnable; i++) {
                    ArtifactDef artifactToEnable = validArtifactDefs[rnd.Next(0, validArtifactDefs.Count)];
                    artifactsToEnable.Add(artifactToEnable);
                    validArtifactDefs.Remove(artifactToEnable);
                }

                // Disable all artifacts
                foreach (ArtifactDef artifactDef in validArtifactDefs) {
                    if(artifactDef == RoR2Content.Artifacts.sacrificeArtifactDef && artifactsToEnable.Contains(RoR2Content.Artifacts.sacrificeArtifactDef)) {
                        // Keep sacrifice on, dont refresh it
                        artifactsToEnable.Remove(RoR2Content.Artifacts.sacrificeArtifactDef);
                    } else {
                        RunArtifactManager.instance.SetArtifactEnabled(artifactDef, false);
                    } 
                }

                // Enable random artifacts
                for (int i = 0; i < artifactsToEnable.Count; i++) {
                    RunArtifactManager.instance.SetArtifactEnabled(artifactsToEnable[i], true);
                }
            }
        }
    }
}
