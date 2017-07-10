﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using StardewValley.Objects;
using StardewValley.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using xTile.Dimensions;


namespace Replanter
{
    public class Replanter : Mod
    {
        // The hot key which activates this mod.
        private static Keys actionKey;

        // A dictionary that allows seeds to be looked up by the crop's id.
        private Dictionary<int, int> cropToSeed = null;

        // A dictionary that allows the seed price to be looked up by the seed's id.
        private Dictionary<int, int> seedToPrice = null;

        // A dictionary that allows chests to be looked up per item id, in case you've got somewhere else for that stuff to go.
        private Dictionary<int, Vector2> chestLookup;

        // A dictionary that allows the crop price to be looked up by the crop's id.
        // private Dictionary<int, int> cropToPrice = null;

        // A list of crops that are never to be harvested.
        private HashSet<int> ignoreLookup = null;

        // A list of crops that are always to be sold, regardless of "sellAfterHarvest" setting.  This list trumps never-sell in the case of duplicates.
        private HashSet<int> alwaysSellLookup = null;

        // A list of crops that are never sold, regardless of sellAfterHarvest setting.
        private HashSet<int> neverSellLookup = null;

        // The mod's configuration file.
        public static ReplanterConfig config;

        // Whether to disregard all cost mechanisms, or to enforce them.
        private bool free = false;

        // A discount (integer converted to a percentage) received on seeds.
        private int seedDiscount = 0;

        // Whether to display dialogues and message feedback to the farmer.
        private bool messagesEnabled = true;

        // The cost charged for the action of harvesting a crop, this is calculated per-crop.
        private float costPerHarvestedCrop = 0.5f;

        // Whether or not to immediately sell the crops that are harvested-- they bypass both the inventory and chests, and the money is received immediately.
        private bool sellAfterHarvest = false;

        // Whether or not to water the crops after they are replanted.
        private bool waterCrops = false;

        // A bar separated list of crop Ids that are to be ignored by the harvester.
        private string ignoreList = "";

        // A bar separated list of crops that are to be always sold, regardless if sellAfterHarvest is false.
        private string alwaysSellList = "";

        // A bar separated list of crops that are to never be sold, regardless if sellAfterHarvest is true.
        private string neverSellList = "";

        private bool clearDeadPlants = true;

        private bool smartReplantingEnabled = true;

        // The name of the person or character who "checks" the crops, and will participate in dialogues following the action.
        private string checker = "spouse";

        // Whether to bypass the inventory, and first attempt to deposit the harvest into the chest.  Inventory is then used as fallback.
        private bool bypassInventory = false;

        // The coordinates of a chest where crops are to be put if there is not room in your inventory.
        private Vector2 chestCoords = new Vector2(70f, 14f);

        private String chestDefs = "";

        private Dictionary<int, ChestDef> chests;


        #region Messages and Dialogue

        // Content manager for loading dialogs, etc.
        public LocalizedContentManager content;

        // An indexed list of all messages from the dialog.xna file
        private Dictionary<string, string> allmessages = null;

        // An indexed list of key dialog elements, these need to be indexed in the order in the file ie. cannot be randomized.
        private Dictionary<int, string> dialog = null;

        // An indexed list of greetings.
        private Dictionary<int, string> greetings = null;

        // An indexed list of all dialog entries relating to "unfinished money"
        //private Dictionary<int, string> unfinishedMessages = null;

        // An indexed list of all dialog entries related to "freebies"
        private Dictionary<int, string> freebieMessages = null;

        // An indexed list of all dialog entries related to "inventory full"
        private Dictionary<int, string> inventoryMessages = null;

        // An indexed list of all dialog entries related to "smalltalk".  This list is merged with a list of dialogs that are specific to your "checker"
        private Dictionary<int, string> smalltalk = null;

        // Random number generator, used primarily for selecting dialog messages.
        private Random random = new Random();

        #endregion

        // A flag for when an item could not be deposited into either the inventory or the chest.
        private bool inventoryAndChestFull = false;


        public Replanter() : base()
        {
            
        }

        public override void Entry(params object[] objects)
        {
            PlayerEvents.LoadedGame += onLoaded;
            ControlEvents.KeyReleased += onKeyReleased;
        }


        private void onLoaded(object sender, EventArgs e)
        {
            Replanter.config = (ReplanterConfig)ConfigExtensions.InitializeConfig<ReplanterConfig>(new ReplanterConfig(), this.BaseConfigPath);

            // Loads and validates the configuration settings.
            importConfiguration();

            DialogManager.config = config;

            // Parses the always sell, never sell, and never harvest lists.
            generateLists();

            // Parses the lookup dictionaries for seed price and item info.
            generateDictionaries();

            parseChestLocations();

            // Read in dialogue
            this.content = new LocalizedContentManager(Game1.content.ServiceProvider, this.PathOnDisk);
            readInMessages();
        }


        private void onKeyReleased(object sender, EventArgsKeyPressed e)
        {
            if (Game1.currentLocation == null
                || (Game1.player == null
                || Game1.hasLoadedGame == false)
                || (((Farmer)Game1.player).UsingTool
                || !((Farmer)Game1.player).CanMove
                || (Game1.activeClickableMenu != null
                || Game1.CurrentEvent != null))
                || Game1.gameMode != 3)
                return;

            if (e.KeyPressed == Replanter.actionKey)
            {
                try
                {
                    performAction();
                }
                catch (Exception ex)
                {
                    Log.ERROR((object)("[Replanter] Exception performAction: " + ex.Message));
                    Log.ERROR((object)("[Replanter] Stacktrace: " + ex.ToString()));
                }

            }

        }


        private void importConfiguration()
        {
            Log.enabled = config.enableLogging;

            if (!Enum.TryParse<Keys>(config.keybind, true, out Replanter.actionKey))
            {
                Replanter.actionKey = Keys.J;
                Log.force_INFO((object)"[Replanter] Error parsing key binding. Defaulted to J");
            }

            this.messagesEnabled = config.enableMessages;

            this.free = config.free;

            if (config.seedDiscount > 100)
                this.seedDiscount = 100;
            else if (config.seedDiscount < 0)
                this.seedDiscount = 0;
            else
                this.seedDiscount = config.seedDiscount;

            this.costPerHarvestedCrop = config.costPerCropHarvested;
            this.sellAfterHarvest = config.sellHarvestedCropsImmediately;
            this.checker = config.whoChecks;
            this.waterCrops = config.waterCrops;

            this.bypassInventory = config.bypassInventory;
            this.chestCoords = config.chestCoords;

            this.neverSellList = config.neverSellList;
            this.alwaysSellList = config.alwaysSellList;
            this.ignoreList = config.ignoreList;

            this.chestDefs = config.chestDefs;

            this.clearDeadPlants = config.clearDeadPlants;
            this.smartReplantingEnabled = config.smartReplantingEnabled;
        }


        private void performAction()
        {
            Farm farm = Game1.getFarm();
            Farmer farmer = Game1.player;

            ReplanterStats stats = new ReplanterStats();

            foreach (GameLocation location in Game1.locations)
            {
                bool itemHarvested = true;

                if (!location.isFarm && !location.name.Contains("Greenhouse")) continue;

                foreach (KeyValuePair<Vector2, TerrainFeature> feature in location.terrainFeatures)
                {
                    if (feature.Value == null) continue;

                    if (feature.Value is HoeDirt)
                    {

                        HoeDirt dirt = (HoeDirt)feature.Value;

                        if (dirt.crop != null)
                        {
                            Crop crop = dirt.crop;

                            if (waterCrops && dirt.state != 1)
                            {
                                dirt.state = 1;
                                stats.cropsWatered++;
                            }

                            if (clearDeadPlants && crop.dead)
                            {
                                // TODO: store what kind of crop this was so we can replant.
                                dirt.destroyCrop(feature.Key);
                                stats.plantsCleared++;

                                continue;
                            }

                            if (ignore(crop.indexOfHarvest))
                            {
                                continue;
                            }

                            if (crop.currentPhase >= crop.phaseDays.Count - 1 && (!crop.fullyGrown || crop.dayOfCurrentPhase <= 0))
                            {
                                int seedCost = 0;
                                stats.totalCrops++;

                                StardewValley.Object item = getHarvestedCrop(dirt, crop, (int)feature.Key.X, (int)feature.Key.Y);

                                if (!free)
                                {
                                    seedCost = (int)((float)costOfSeed(crop.indexOfHarvest) * ((100f - (float)this.seedDiscount) / 100f));
                                }
                                if (sellAfterHarvest)
                                {
                                    if ( sellCrops(farmer, item, stats) )
                                    {
                                        if (crop.indexOfHarvest == 431)
                                            handleSunflower(farmer, stats, item.quality);

                                        itemHarvested = true;
                                    }
                                    else
                                    {
                                        itemHarvested = false;
                                    }

                                    
                                }
                                else
                                {
                                    if (addItemToInventory(item, farmer, farm, stats))
                                    {
                                        itemHarvested = true;

                                        if (crop.indexOfHarvest == 431)
                                        {
                                            handleSunflower(farmer, stats, item.quality, (int)feature.Key.X, (int)feature.Key.Y);
                                        }
                                    }
                                    else
                                    {
                                        itemHarvested = false;
                                    }

                                }

                                // Replanting

                                if (itemHarvested)
                                {
                                    stats.cropsHarvested++;

                                    if ( replantCrop(crop, location) )
                                    {
                                        if (crop.regrowAfterHarvest == -1)
                                        {
                                            stats.runningSeedCost += seedCost;
                                        }
                                    }
                                    else
                                    {
                                        if (crop.dead)
                                        {
                                            // Store what kind of crop this is so you can replant.
                                            dirt.destroyCrop(feature.Key);
                                            stats.plantsCleared++;
                                        }
                                    }
                                    

                                    // Add experience
                                    float experience = (float)(16.0 * Math.Log(0.018 * (double)Convert.ToInt32(Game1.objectInformation[crop.indexOfHarvest].Split('/')[1]) + 1.0, Math.E));
                                    Game1.player.gainExperience(0, (int)Math.Round((double)experience));
                                }
   
                            }
                        }
                    }
                    else if (feature.Value is FruitTree)
                    {
                        FruitTree tree = (FruitTree)feature.Value;

                        if (tree.fruitsOnTree > 0)
                        {
                            int countFromThisTree = 0;

                            for (int i = 0; i < tree.fruitsOnTree; i++)
                            {
                                stats.totalCrops++;

                                StardewValley.Object fruit = getHarvestedFruit(tree);

                                if (sellAfterHarvest)
                                {
                                    if (sellCrops(farmer, fruit, stats))
                                    {
                                        itemHarvested = true;
                                        countFromThisTree++;
                                    }  
                                    else
                                        itemHarvested = false;
                                }
                                else
                                {
                                    if (addItemToInventory(fruit, farmer, farm, stats))
                                    {
                                        itemHarvested = true;
                                        countFromThisTree++;
                                    }
                                    else
                                        itemHarvested = false;
                                }

                                if (itemHarvested)
                                {
                                    stats.cropsHarvested++;

                                    float experience = (float)(16.0 * Math.Log(0.018 * (double)Convert.ToInt32(Game1.objectInformation[tree.indexOfFruit].Split('/')[1]) + 1.0, Math.E));
                                    Game1.player.gainExperience(0, (int)Math.Round((double)experience));
                                }
                            }

                            tree.fruitsOnTree -= countFromThisTree;
                        }

                        
                    }
                }
            }

            if (stats.runningSellPrice > 0)
            {
                farmer.Money = farmer.Money + stats.runningSellPrice;
                Log.INFO((object)("[Replanter] " + "Sale price of your crops: " + stats.runningSellPrice));
            }

            if (!free)
            {
                farmer.Money = Math.Max(0, farmer.Money - stats.runningSeedCost);
                Log.INFO((object)("[Replanter] " + "Total cost of new seeds: " + stats.runningSeedCost));
            }

            if (!free)
            {
                stats.farmhandCost = (int)Math.Round((float)stats.cropsHarvested * (float)costPerHarvestedCrop);
                farmer.Money = Math.Max(0, farmer.Money - stats.farmhandCost);
                Log.INFO((object)("[Replanter] " + "Costs paid to farm hand: " + stats.farmhandCost));
            }

            if (this.messagesEnabled)
            {
                showMessage(stats);
            }
        }


        private StardewValley.Object getHarvestedFruit(FruitTree tree)
        {
            int quality = 0;
            if (tree.daysUntilMature <= -112)
                quality = 1;
            if (tree.daysUntilMature <= -224)
                quality = 2;
            if (tree.daysUntilMature <= -336)
                quality = 4;
            //if (tree.struckByLightningCountdown > 0)
            //        quality = 0; 

            StardewValley.Object obj = new StardewValley.Object(tree.indexOfFruit, 1, false, -1, quality);

            return obj;
        }

        private StardewValley.Object getHarvestedCrop(HoeDirt dirt, Crop crop, int tileX, int tileY)
        {
            int stackSize = 1;
            int itemQuality = 0;
            int fertilizerBuff = 0;

            Random random = new Random(tileX * 7 + tileY * 11 + (int)Game1.stats.DaysPlayed + (int)Game1.uniqueIDForThisGame);

            switch (dirt.fertilizer)
            {
                case 368:
                    fertilizerBuff = 1;
                    break;
                case 369:
                    fertilizerBuff = 2;
                    break;
            }

            double qualityModifier1 = 0.2 * (double)(Game1.player.FarmingLevel / 10) + 0.2 * (double)fertilizerBuff * ((double)(Game1.player.FarmingLevel + 2) / 12.0) + 0.01;
            double qualityModifier2 = Math.Min(0.75, qualityModifier1 * 2.0);

            if (random.NextDouble() < qualityModifier1)
                itemQuality = 2;
            else if (random.NextDouble() < qualityModifier2)
                itemQuality = 1;
            if (crop.minHarvest > 1 || crop.maxHarvest > 1)
                stackSize = random.Next(crop.minHarvest, Math.Min(crop.minHarvest + 1, crop.maxHarvest + 1 + Game1.player.FarmingLevel / crop.maxHarvestIncreasePerFarmingLevel));
            if (crop.chanceForExtraCrops > 0.0)
            {
                while (random.NextDouble() < Math.Min(0.9, crop.chanceForExtraCrops))
                    ++stackSize;
            }

            if (random.NextDouble() < (double)Game1.player.LuckLevel / 1500.0 + Game1.dailyLuck / 1200.0 + 9.99999974737875E-05)
            {
                stackSize *= 2;
            }

            if (crop.indexOfHarvest == 421)
            {
                crop.indexOfHarvest = 431;
                stackSize = random.Next(1, 4);
            }

            StardewValley.Object item = new StardewValley.Object(crop.indexOfHarvest, stackSize, false, -1, itemQuality);

            return item;
        }


        private bool replantCrop(Crop c, GameLocation location)
        {
            bool replanted = true;
            String replantLog = "";

            if (smartReplantingEnabled && !location.Name.Contains("Greenhouse"))
            {
                string season = Game1.currentSeason;
                string nextSeason = getNextSeason(season);
                int day = Game1.dayOfMonth;
                int growingDaysTillNextSeason = 28 - day;
                int totalDaysNeeded = c.phaseDays.Sum();

                replantLog += "smartenabled/dtg:" + "?" + "/gdtns:" + growingDaysTillNextSeason + "/ioh:" + c.indexOfHarvest + "/tdn:" + totalDaysNeeded + "/";

                if (c.regrowAfterHarvest == -1)
                {
                    replantLog += "rah:" + c.regrowAfterHarvest + "/cpb:" + c.currentPhase + "/";
                    //c.currentPhase = 0;

                    if (!((totalDaysNeeded - 99999) <= growingDaysTillNextSeason) && !c.seasonsToGrowIn.Contains(nextSeason))
                    {
                        replantLog += "notreplanted/";
                        c.dead = true;
                        replanted = false;
                    }
                    else
                    {
                        replantLog += "replanted/";
                        c.currentPhase = 0;
                        replantLog += "cpa:" + c.currentPhase + "/";
                    }
                }
                else
                {
                    replantLog += "cp:" + c.currentPhase + "/rah:" + c.regrowAfterHarvest + "/docpb:" + c.dayOfCurrentPhase + "/";
                    c.dayOfCurrentPhase = c.regrowAfterHarvest;
                    c.fullyGrown = true;
                    replantLog += "docpa:" + c.dayOfCurrentPhase + "/";
                }
            }
            else
            {
                replantLog += "smartdisabled/";
                if (c.regrowAfterHarvest == -1)
                {
                    replantLog += "rah:" + c.regrowAfterHarvest + "/cpb:" + c.currentPhase + "/";
                    c.currentPhase = 0;
                    replantLog += "cpa:" + c.currentPhase + "/";
                }
                else
                {
                    replantLog += "cp:" + c.currentPhase + "/rah:" + c.regrowAfterHarvest + "/docpb:" + c.dayOfCurrentPhase + "/";
                    c.dayOfCurrentPhase = c.regrowAfterHarvest;
                    c.fullyGrown = true;
                    replantLog += "docpa:" + c.dayOfCurrentPhase + "/";
                }
            }

            replantLog += "replanted?:" + replanted;

            Log.DEBUG(replantLog);

            return replanted;
        }


        private string getNextSeason(string season)
        {
            switch (season)
            {
                case "spring":
                    return "summer";
                case "summer":
                    return "fall";
                case "fall":
                    return "winter";
                case "winter":
                    return "spring";
                default:
                    return "spring";
            }
        }


        private void handleSunflower(Farmer farmer, ReplanterStats stats, int quality, int tileX = 0, int tileY = 0)
        {
            if (sellAfterHarvest)
            {
                StardewValley.Object flower = new StardewValley.Object(421, 1, false, -1, quality);

                if ( ! sellCrops(farmer, flower, stats) )
                {
                    // TODO: what to do if we can't sell the sunflower?
                }

            }
            else
            {
                StardewValley.Object flower = new StardewValley.Object(421, 1, false, -1, quality);

                if (!addItemToInventory(flower, farmer, Game1.getFarm(), stats))
                {
                    Game1.createObjectDebris(421, tileX, tileY, -1, flower.quality, 1f, (GameLocation)null);

                    Log.INFO((object)("[Replanter] Sunflower was harvested, but couldn't add flower to inventory, you'll have to go pick it up."));
                }

            }
        }


        private void showMessage(ReplanterStats stats)
        {
            string message = "";

            if (checker.ToLower() == "spouse")
            {
                if (Game1.player.isMarried())
                {
                    message += DialogManager.performReplacement(dialog[1], stats);
                }
                else
                {
                    message += DialogManager.performReplacement(dialog[2], stats);
                }

                if ( ( (stats.runningSeedCost + stats.farmhandCost) > 0 ) && !free)
                {
                    message += DialogManager.performReplacement(dialog[3], stats);
                }

                HUDMessage msg = new HUDMessage(message);
                Game1.addHUDMessage(msg);
            }
            else
            {
                NPC character = Game1.getCharacterFromName(checker);
                if (character != null)
                {
                    message += DialogManager.performReplacement(getRandomMessage(greetings), stats);

                    if (stats.cropsHarvested > 0)
                    {
                        message += DialogManager.performReplacement(dialog[4], stats);
                    }  
                    else
                    {
                        message += DialogManager.performReplacement(dialog[7], stats);
                    }   

                    if ( (stats.cropsHarvested != stats.totalCrops) && !sellAfterHarvest)
                    {
                        message += DialogManager.performReplacement(dialog[8], stats);
                        message += DialogManager.performReplacement(getRandomMessage(inventoryMessages), stats);
                    }

                    if (!free && stats.cropsHarvested > 0)
                    {
                        message += DialogManager.performReplacement(dialog[5], stats);

                        if (stats.runningSeedCost > 0)
                            message += DialogManager.performReplacement(dialog[9], stats);
                        else
                            message += ".";
                    }

                    if (sellAfterHarvest && stats.cropsHarvested > 0)
                    {
                        if (character.name == "Pierre")
                        {
                            message += DialogManager.performReplacement(dialog[10], stats);
                        }
                        else
                        {
                            message += DialogManager.performReplacement(dialog[11], stats);
                        } 
                    }

                    if (stats.cropsWatered > 0)
                    {
                        message += DialogManager.performReplacement(dialog[12], stats);
                    }

                    message += DialogManager.performReplacement(getRandomMessage(smalltalk), stats);
                    message += "#$e#";

                    character.CurrentDialogue.Push(new Dialogue(message, character));
                    Game1.drawDialogue(character);
                }
                else
                {
                    message += DialogManager.performReplacement(dialog[13], stats);
                    HUDMessage msg = new HUDMessage(message);
                    Game1.addHUDMessage(msg);
                }
            }

        }


        /**
         * Parses the neverSell, alwaysSell, neverHarvest crops.
         */
        private void generateLists()
        {
            ignoreLookup = new HashSet<int>();
            if (ignoreList.Length > 0)
            {
                string[] ignoredItems = ignoreList.Split('|');
                foreach (string ignored in ignoredItems)
                {
                    ignoreLookup.Add(Convert.ToInt32(ignored));
                }
            }

            alwaysSellLookup = new HashSet<int>();
            if (alwaysSellList.Length > 0)
            {
                string[] alwaysSellItems = alwaysSellList.Split('|');
                foreach (string always in alwaysSellItems)
                {
                    alwaysSellLookup.Add(Convert.ToInt32(always));
                }
            }

            neverSellLookup = new HashSet<int>();
            if (neverSellList.Length > 0)
            {
                string[] neverSellItems = neverSellList.Split('|');
                foreach (string always in neverSellItems)
                {
                    neverSellLookup.Add(Convert.ToInt32(always));
                }
            }
        }


        private void parseChestLocations()
        {
            chests = new Dictionary<int, ChestDef>();

            string[] chestDefinitions = this.chestDefs.Split('|');

            foreach (string def in chestDefinitions)
            {
                string[] chestInfo = def.Split(',');
                if (chestInfo.Length == 3)
                {
                    // A Farm chest
                    ChestDef cd = new ChestDef(Convert.ToInt32(chestInfo[1]), Convert.ToInt32(chestInfo[2]));
                    chests.Add(Convert.ToInt32(chestInfo[0]), cd);
                }
                else if (chestInfo.Length == 4)
                {
                    // A farm house chest
                    ChestDef cd = new ChestDef(Convert.ToInt32(chestInfo[1]), Convert.ToInt32(chestInfo[2]));
                    cd.location = "house";
                    chests.Add(Convert.ToInt32(chestInfo[0]), cd);
                }
            }
        }


        /**
         * Generates the lookup dictionaries needed to get crop and seed prices.
         */
        private void generateDictionaries()
        {
            Dictionary<int, string> dictionary = Game1.content.Load<Dictionary<int, string>>("Data\\Crops");
            Dictionary<int, string> objects = Game1.content.Load<Dictionary<int, string>> ("Data\\ObjectInformation");

            cropToSeed = new Dictionary<int, int>();
            //cropToPrice = new Dictionary<int, int>();
            seedToPrice = new Dictionary<int, int>();

            foreach (KeyValuePair<int, string> crop in (Dictionary<int, string>)dictionary)
            {
                string[] cropString = crop.Value.Split('/');

                int seedId = crop.Key;
                int harvestId = Convert.ToInt32(cropString[3]);

                if (seedId != 770)
                {
                    cropToSeed.Add(harvestId, seedId);
                }

                // Both of these are indexed by the harvest ID.
                string[] seedData = objects[seedId]?.Split('/');

                if (seedData != null)
                {
                    int seedCost = Convert.ToInt32(seedData[1]) * 2;

                    if (harvestId == 421)
                    {
                        seedToPrice.Add(431, seedCost);
                    }
                    else
                    {
                        seedToPrice.Add(harvestId, seedCost);
                    }
                    
                    Log.INFO((object)("[Replanter] Adding ID to seed price index: " + harvestId + " / " + seedCost));
                }
            }


        }


        /**
         * Gets the cost of the seed based off its harvest ID, (not the item id of the seed)
         */
        private int costOfSeed(int id)
        {
            if (! seedToPrice.ContainsKey(id) )
            {
                Log.ERROR((object)("[Replanter] Couldn't find seed for harvest ID: " + id));

                return 20;
            }
            else
                return seedToPrice[id];
        }


        /**
         * Sells the crops, and adds them to the inventory if they are on the never-sell list.
         */
        private bool sellCrops(Farmer farmer, StardewValley.Object obj, ReplanterStats stats)
        {
            if (neverSell(obj.parentSheetIndex))
            {
                return ( addItemToInventory(obj, farmer, Game1.getFarm(), stats) );
            }
                
            stats.runningSellPrice += obj.sellToStorePrice();
            return true;

        }


        /**
         * Determines of a crop is on the always-sell list.
         */
        private bool alwaysSell(int cropId)
        {
            return alwaysSellLookup.Contains(cropId);
        }


        /**
         * Determines if a crop is on the never-sell list.  This does an alwaysSell lookup, because the always sell list trumps the never sell list,
         * in a case where a crop is included in both.
         */
        private bool neverSell(int cropId)
        {
            if (alwaysSellLookup.Contains(cropId))
                return false;
            else
                return neverSellLookup.Contains(cropId);   
        }


        /**
         * Determines if a crop in on the ignore (do-not-harvest) list.
         */
        private bool ignore(int cropId)
        {
            return ignoreLookup.Contains(cropId);
        }


        /**
         * Attempts to add the crop to the farmer's inventory.  If the crop is on the always sell list, it is sold instead.
         */
        private bool addItemToInventory(StardewValley.Object obj, Farmer farmer, Farm farm, ReplanterStats stats)
        {
            if (alwaysSell(obj.parentSheetIndex))
            {
                return sellCrops(farmer, obj, stats);
            }

            bool wasAdded = false;

            if (farmer.couldInventoryAcceptThisItem(obj) && !bypassInventory)
            {
                farmer.addItemToInventory(obj);
                wasAdded = true;

                Log.INFO((object)("[Replanter] Was able to add item to inventory."));
            }
            else
            {
                StardewValley.Object chest = null;

                ChestDef preferred = null;
                chests.TryGetValue(obj.ParentSheetIndex, out preferred);

                if (preferred != null)
                {
                    if (preferred.location.Equals("house"))
                    {
                        FarmHouse house = (FarmHouse)Game1.getLocationFromName("FarmHouse");
                        house.objects.TryGetValue(preferred.vector, out chest);
                    }
                    else
                    {
                        farm.objects.TryGetValue(preferred.vector, out chest);
                    } 

                    if (chest == null || !(chest is Chest))
                    {
                        // Try getting the default chest.
                        farm.objects.TryGetValue(chestCoords, out chest);
                    }
                }
                else
                {
                    farm.objects.TryGetValue(chestCoords, out chest);
                }

                if (chest != null && chest is Chest)
                {
                    Item i = ((Chest)chest).addItem(obj);
                    if (i == null)
                    {
                        wasAdded = true;
                    }
                    else
                    {
                        // If this condition was reached because bypassInventory was set, then try the inventory.
                        if (bypassInventory && farmer.couldInventoryAcceptThisItem(obj))
                        {
                            farmer.addItemToInventory(obj);
                            wasAdded = true;
                        }
                        else
                        {
                            inventoryAndChestFull = true;

                            Log.INFO((object)("[Replanter] Was NOT able to add items to chest."));
                        }
                    }

                }
                else
                {
                    Log.INFO((object)("[Replanter] Did not find a chest at " + (int)chestCoords.X + "," + (int)chestCoords.Y));

                    // If bypassInventory is set to true, but there's no chest: try adding to the farmer's inventory.
                    if (bypassInventory)
                    {
                        Log.INFO((object)("[Replanter] No chest at " + (int)chestCoords.X + "," + (int)chestCoords.Y + ", you should place a chest there, or set bypassInventory to 'false'."));

                        if (farmer.couldInventoryAcceptThisItem(obj))
                        {
                            farmer.addItemToInventory(obj);
                            wasAdded = true;
                        }
                        else
                        {
                            inventoryAndChestFull = true;

                            Log.INFO((object)("[Replanter] Was NOT able to add item to inventory or a chest.  (No chest found, bypassInventory set to 'true')"));
                        }
                    }
                    else
                    {
                        inventoryAndChestFull = true;

                        Log.INFO((object)("[Replanter] Was NOT able to add item to inventory or a chest.  (No chest found, bypassInventory set to 'false')"));
                    }
                }
            }

            return wasAdded;
        }


        /**
         * Gets a random message from a specific list.
         */
        public string getRandomMessage(Dictionary<int, string> messageStore)
        {
            Log.INFO((object)("returning random message from : " + messageStore.Count));

            int rand = random.Next(1, messageStore.Count + 1);

            string value = "...$h#$e#";

            messageStore.TryGetValue(rand, out value);

            return value;
        }


        /**
         * Loads the dialog.xnb file and sets up each of the dialog lookup files.
         */
        public void readInMessages()
        {
            //Dictionary<int, string> objects = Game1.content.Load<Dictionary<int, string>>("Data\\ObjectInformation");
            try
            {
                allmessages = content.Load<Dictionary<string, string>>("dialog");

                dialog = DialogManager.getDialog("Xdialog", allmessages);
                greetings = DialogManager.getDialog("greeting", allmessages);
                //unfinishedMessages = DialogManager.getDialog("unfinishedmoney", allmessages);
                freebieMessages = DialogManager.getDialog("freebies", allmessages);
                inventoryMessages = DialogManager.getDialog("unfinishedinventory", allmessages);
                smalltalk = DialogManager.getDialog("smalltalk", allmessages);

                Dictionary<int, string> characterDialog = DialogManager.getDialog(this.checker, allmessages);

                if (characterDialog.Count > 0)
                {
                    int index = smalltalk.Count + 1;
                    foreach (KeyValuePair<int, string> d in characterDialog)
                    {
                        smalltalk.Add(index, d.Value);
                        index++;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.force_ERROR((object)("[Replanter] Exception loading content:" + ex.ToString()));
            }
        }

    }
}