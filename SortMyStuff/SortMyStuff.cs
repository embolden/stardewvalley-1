﻿using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using StardewLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Objects;
using Object = StardewValley.Object;


namespace SortMyStuff
{
    public class SortMyStuff : Mod
    {
        /*********
        ** Properties
        *********/
        private SortMyStuffConfig config;
        private Keys actionKey;
        private ChestManager ChestManager;


        /*********
        ** Public methods
        *********/
        public override void Entry(params object[] objects)
        {
            PlayerEvents.LoadedGame += onLoaded;
            ControlEvents.KeyReleased += onKeyReleased;
        }


        /*********
        ** Private methods
        *********/
        private void onLoaded(object sender, EventArgs e)
        {
            try
            {
                this.config = this.Helper.ReadConfig<SortMyStuffConfig>();
                this.ChestManager = new ChestManager(this.Monitor);

                if (!Enum.TryParse(config.keybind, true, out this.actionKey))
                {
                    this.actionKey = Keys.G;
                    this.Monitor.Log("Error parsing key binding. Defaulted to G");
                }

                ChestManager.ParseChests(config.chests);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(ex.ToString(), LogLevel.Error);
            }
        }

        private void onKeyReleased(object sender, EventArgsKeyPressed e)
        {
            if (Game1.currentLocation == null
                || (Game1.player == null
                || Game1.hasLoadedGame == false)
                || ((Game1.player).UsingTool
                || !(Game1.player).CanMove
                || (Game1.activeClickableMenu != null
                || Game1.CurrentEvent != null))
                || Game1.gameMode != 3)
                return;

            if (e.KeyPressed == actionKey)
            {
                this.Monitor.Log("Logging key stroke G!!!", LogLevel.Trace);
                List<ItemContainer> ic = new List<ItemContainer>();
                try
                {
                    foreach (Item item in Game1.player.Items)
                    {
                        if (item != null)
                        {
                            this.Monitor.Log($"{item.Name}/{item.parentSheetIndex}", LogLevel.Trace);
                            Object c = ChestManager.GetChest(item.parentSheetIndex);
                            if (c != null && c is Chest)
                            {
                                ic.Add(new ItemContainer((Chest)c, item));
                            }
                            else
                            {
                                c = (Chest)ChestManager.GetChest(item.category);
                                if (c != null && c is Chest)
                                {
                                    ic.Add(new ItemContainer((Chest)c, item));
                                }
                            }
                        }
                    }

                    foreach (ItemContainer i in ic)
                    {
                        Item o = i.chest.addItem(i.item);
                        if (o == null)
                            Game1.player.removeItemFromInventory(i.item);
                        //else
                        //    Game1.player.addItemToInventory(i.item);
                    }


                    SortedDictionary<int, ChestDef> bestGuessChests = ChestManager.parseAllChests();
                    string message = "";
                    foreach (KeyValuePair<int, ChestDef> chestDefs in bestGuessChests)
                    {
                        message += chestDefs.Key + ": " + chestDefs.Value + "\n";
                    }
                    this.Monitor.Log(message);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log(ex.ToString(), LogLevel.Error);
                }
            }
        }
    }
}
