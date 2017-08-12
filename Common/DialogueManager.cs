﻿using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;

namespace StardewLib
{
    internal class DialogueManager
    {
        /*********
        ** Properties
        *********/
        private readonly LocalizedContentManager Content;
        private readonly Dictionary<string, Dictionary<int, string>> DialogueLookups = new Dictionary<string, Dictionary<int, string>>();
        private readonly IConfig Config;
        private readonly Random Random = new Random();
        private readonly IMonitor Monitor;
        private Dictionary<string, string> AllMessages = new Dictionary<string, string>();


        /*********
        ** Public methods
        *********/
        public DialogueManager(IConfig config, IServiceProvider provider, string path, IMonitor monitor)
        {
            this.Config = config;
            this.Content = new LocalizedContentManager(provider, path);
            this.Monitor = monitor;
        }

        /**
         * Performs a string replacement of certain variables inside text strings, allowing
         * the dialog to use elements of the actual situation.
         */
        public string PerformReplacement(string message, IStats stats, IConfig config)
        {
            string retVal = message;

            IDictionary<string, object> fields = stats.GetFields();


            foreach (var field in fields)
            {
                retVal = retVal.Replace(("%%" + field.Key + "%%"), field.Value.ToString());
            }

            retVal = retVal.Replace("%%checker%%", config.WhoChecks);
            if (Game1.player.isMarried())
                retVal = retVal.Replace("%%spouse%%", Game1.player.getSpouse().getName());
            else
                retVal = retVal.Replace("%%spouse%%", config.WhoChecks);

            return retVal;
        }

        public string GetRandomMessage(string messageStoreName)
        {
            string value = "";

            Dictionary<int, string> messagePool = null;
            this.DialogueLookups.TryGetValue(messageStoreName, out messagePool);

            if (messagePool == null)
            {
                messagePool = readDialogue(messageStoreName);
            }
            else if (messagePool.Count == 0)
            {
                return "...$h#$e#";
            }

            int rand = this.Random.Next(1, messagePool.Count + 1);
            messagePool.TryGetValue(rand, out value);

            if (value == null)
            {
                return "...$h#$e#";
            }
            else
            {
                return value;
            }
        }

        public string GetMessageAt(int index, string messageStoreName)
        {
            Dictionary<int, string> messagePool = null;
            this.DialogueLookups.TryGetValue(messageStoreName, out messagePool);

            if (messagePool == null)
            {
                messagePool = readDialogue(messageStoreName);
            }
            else if (messagePool.Count == 0)
            {
                return "...$h#$e#";
            }
            else if (messagePool.Count < index)
            {
                return "...$h#$e#";
            }

            this.Monitor.Log($"Returning message {index}: {messagePool[index]}", LogLevel.Trace);
            return messagePool[index];
            //return messagePool.ElementAt(index).Value;
        }


        /**
         * Loads the dialog.xnb file and sets up each of the dialog lookup files.
         */
        public void ReadInMessages()
        {
            //Dictionary<int, string> objects = Game1.content.Load<Dictionary<int, string>>("Data\\ObjectInformation");
            try
            {
                this.AllMessages = this.Content.Load<Dictionary<string, string>>("dialog");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"[jwdred-StardewLib] Exception loading content:{ex}", LogLevel.Error);
            }
        }

        /**
         * Gets a set of dialogue strings that are identified in the source document by an index
         * in the format indexGroup_number.  The numbers should be unique within each index group.
         */
        public Dictionary<int, string> GetDialog(string identifier, Dictionary<string, string> source)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();

            foreach (KeyValuePair<string, string> msgPair in source)
            {
                if (msgPair.Key.Contains("_"))
                {
                    string[] nameid = msgPair.Key.Split('_');
                    if (nameid.Length == 2)
                    {
                        if (nameid[0] == identifier)
                        {
                            result.Add(Convert.ToInt32(nameid[1]), msgPair.Value);
                        }
                    }
                    else
                    {
                        this.Monitor.Log("Malformed dialog string encountered. Ensure key is in the form of indexGroup_number:, where 'number' is unique within its indexGroup.", LogLevel.Error);
                    }
                }
                else
                {
                    this.Monitor.Log("Malformed dialog string encountered. Ensure key is in the form of indexGroup_number:, where 'number' is unique within its indexGroup.", LogLevel.Error);
                }
            }

            return result;
        }

        /*********
        ** Private methods
        *********/
        private Dictionary<int, string> readDialogue(string identifier)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();

            foreach (KeyValuePair<string, string> msgPair in this.AllMessages)
            {
                if (msgPair.Key.Contains("_"))
                {
                    string[] nameid = msgPair.Key.Split('_');
                    if (nameid.Length == 2)
                    {
                        if (nameid[0] == identifier)
                        {
                            //Log.INFO("Adding to " + identifier + ": " + nameid[1] + ">" + msgPair.Value);
                            result.Add(Convert.ToInt32(nameid[1]), msgPair.Value);
                        }
                    }
                    else
                    {
                        this.Monitor.Log("Malformed dialog string encountered. Ensure key is in the form of indexGroup_number:, where 'number' is unique within its indexGroup.", LogLevel.Error);
                    }
                }
                else
                {
                    this.Monitor.Log("Malformed dialog string encountered. Ensure key is in the form of indexGroup_number:, where 'number' is unique within its indexGroup.", LogLevel.Error);
                }
            }

            if (identifier.Equals("smalltalk"))
            {
                Dictionary<int, string> characterDialog = this.readDialogue(this.Config.WhoChecks);

                if (characterDialog.Count > 0)
                {
                    int index = result.Count + 1;
                    foreach (KeyValuePair<int, string> d in characterDialog)
                    {
                        result.Add(index, d.Value);
                        index++;
                    }
                }
            }

            this.DialogueLookups.Add(identifier, result);

            return result;
        }
    }
}
