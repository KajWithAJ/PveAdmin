using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;
using System.Text.RegularExpressions;


namespace Oxide.Plugins
{
    [Info("PveAdmin", "KajWithAJ", "0.0.1")]
    [Description("Set of tools to manage a PVE server.")]

    class PveAdmin : RustPlugin
    {
        //[PluginReference] private Plugin DiscordMessages;

        private const string webhookURL = "https://discord.com/api/webhooks/WEBHOOK_TOKEN";

        private bool isInit = false;
        Dictionary<ulong, StorageType> itemTracker = new Dictionary<ulong, StorageType>();

        private const string PermissionExcludeLooters = "pveadmin.exclude.looters";
        private const string PermissionExcludeLadders = "pveadmin.exclude.ladders";

        void OnServerInitialized() {
            isInit = true;
            permission.RegisterPermission(PermissionExcludeLooters, this);
            permission.RegisterPermission(PermissionExcludeLadders, this);
        }

        void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (isInit)
            {
                if (item == null) return;
                if (item.uid.Value == 0) return;
                if (container.playerOwner != null)
                {
                    if (itemTracker.ContainsKey(item.uid.Value))
                    {
                        var player = container.playerOwner;
                        var data = itemTracker[item.uid.Value];
                        if (string.IsNullOrEmpty(data.type) || data.type == "BasePlayer") return;
                        if (player.userID != data.ownerID && !IsInExcludeList(data.ownerID)) {

                            if (!permission.UserHasPermission(player.userID.ToString(), PermissionExcludeLooters)) {
                                if (player.Team == null || !player.Team.members.Contains(data.ownerID)) {
                                    if (!ArePlayersFriendlyToEachOther(player.userID, data.ownerID)) {
                                        //Name of looter: player.displayName
                                        //UserID of looter: player.userID
                                        //UserID of container owner: data.ownerID
                                        //Location of container: data.location

                                        var owner = GetDisplayName(data.ownerID.ToString());

                                        var message = $"{player.displayName} ({player.userID}) looted {data.itemAmount}x {data.itemName} from {data.type} of player {owner} ({data.ownerID}) at location {data.location}";

                                        Puts(message);
                                        LogToFile("looters", message, this);
                                        SendDiscordLootMessage(player.displayName, owner, data.itemName, data.itemAmount, data.type, data.location, data.gridLocation);
                                    }
                                }
                            }
                        }
                        itemTracker.Remove(item.uid.Value);
                    }
                }
                else if (container.entityOwner != null)
                {
                    if (itemTracker.ContainsKey(item.uid.Value))
                    {
                        var data = itemTracker[item.uid.Value];
                        string type = "";
                        if (container.entityOwner is StorageContainer)
                            type = "StorageContainer";
                        if (container.entityOwner.GetComponentInParent<BaseOven>())
                            type = "BaseOven";
                        if (container.entityOwner is StashContainer)
                            type = "StashContainer";
                        if (container.entityOwner is Recycler)
                            type = "Recycler";
                        if (container.entityOwner is ResearchTable)
                            type = "ResearchTable";
                        if (container.entityOwner is RepairBench)
                            type = "RepairBench";
                        if (container.entityOwner is VendingMachine) {
                            type = "VendingMachine";
                            if (((VendingMachine) container.entityOwner).transactionActive) return;
                        }
                        if (string.IsNullOrEmpty(type) || type == "BasePlayer") return;

                        if (data.type == "BasePlayer") {
                            // Depositer: data.ownerID
                            // Container owner: container.entityOwner.OwnerID

                            if (container.entityOwner.OwnerID != data.ownerID && container.entityOwner.OwnerID != 0 && !IsInExcludeList(container.entityOwner.OwnerID)) {
                                if (!permission.UserHasPermission(data.ownerID.ToString(), PermissionExcludeLooters)) {
                                    if (!ArePlayersFriendlyToEachOther(container.entityOwner.OwnerID, data.ownerID)) {
                                        BasePlayer depositor = BasePlayer.FindByID(data.ownerID);
                                        if (depositor == null || depositor.Team == null || !depositor.Team.members.Contains(container.entityOwner.OwnerID)) {
                                            var owner = GetDisplayName(container.entityOwner.OwnerID.ToString());
                                            var location = container.entityOwner.transform.position;
                                            var gridLocation = PhoneController.PositionToGridCoord(location);
                                            var message = $"{data.entityName} ({data.ownerID}) deposited {data.itemAmount}x {data.itemName} to {type} of player {GetDisplayName(container.entityOwner.OwnerID.ToString())} ({container.entityOwner.OwnerID}) at location {location.ToString("F1")}";
                                            Puts(message);
                                            LogToFile("looters", message, this);
                                            SendDiscordDepositMessage(data.entityName, owner, data.itemName, data.itemAmount, type, location.ToString("F1"), gridLocation);
                                        }
                                    }
                                }
                            }
                        }

                        itemTracker.Remove(item.uid.Value);
                    }
                }
            }
        }

        void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (isInit)
            {
                if (item == null || (item.uid.Value == 0)) return;
                if (container.entityOwner != null)
                {
                    var entity = container.entityOwner;

                    if (entity.OwnerID == 0) return;
                    
                    var storageData = new StorageType
                    {
                        entityName = entity.ShortPrefabName,
                        entityID = entity.net.ID.Value.ToString(),
                        itemAmount = item.amount,
                        itemName = item.info.displayName.english,
                        location = entity.transform.position.ToString("F1"),
                        gridLocation = PhoneController.PositionToGridCoord(entity.transform.position),
                        ownerID = entity.OwnerID
                    };
                    

                    if (entity is StorageContainer)
                        storageData.type = "StorageContainer";
                    if (entity.GetComponentInParent<BaseOven>())
                        storageData.type = "BaseOven";
                    if (entity is StashContainer)
                        storageData.type = "StashContainer";
                    if (entity is Recycler)
                        storageData.type = "Recycler";
                    if (entity is ResearchTable)
                        storageData.type = "ResearchTable";
                    if (entity is RepairBench)
                        storageData.type = "RepairBench";
                    if (entity is VendingMachine) {
                        storageData.type = "VendingMachine";
                        if (((VendingMachine) entity).transactionActive) return;
                    }
                        

                    if (string.IsNullOrEmpty(storageData.type)) return;

                    if (!itemTracker.ContainsKey(item.uid.Value))
                    {
                        itemTracker.Add(item.uid.Value, storageData);

                        timer.Once(5, () =>
                        {
                            if (itemTracker.ContainsKey(item.uid.Value))
                                itemTracker.Remove(item.uid.Value);
                        });
                    }
                }
                else if (container.playerOwner != null)
                {
                    var entity = container.playerOwner;

                    var storageData = new StorageType
                    {
                        entityName = entity.displayName,
                        entityID = entity.net.ID.Value.ToString(),
                        itemAmount = item.amount,
                        itemName = item.info.displayName.english,
                        type = "BasePlayer",
                        location = entity.transform.position.ToString("0"),
                        gridLocation = PhoneController.PositionToGridCoord(entity.transform.position),
                        ownerID = entity.userID
                    };
                    if (!itemTracker.ContainsKey(item.uid.Value))
                    {
                        itemTracker.Add(item.uid.Value, storageData);

                        timer.Once(5, () =>
                        {
                            if (itemTracker.ContainsKey(item.uid.Value))
                                itemTracker.Remove(item.uid.Value);
                        });
                    }
                }
            }
        }

        object CanBuild(Planner planner, Construction prefab, Construction.Target target) {
            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return null;
            if (prefab.fullName.Contains("ladder.wooden.wall")) {
                var location = target.entity.transform.position;
                var gridLocation = PhoneController.PositionToGridCoord(location);

                var ownerID = target.entity.OwnerID;

                if (player.userID != ownerID && ownerID != 0) {
                    if (!permission.UserHasPermission(player.userID.ToString(), PermissionExcludeLadders)) {
                        if (player.Team == null || !player.Team.members.Contains(ownerID)) {
                            var message = $"{player.displayName} ({player.userID}) placed a ladder at base of player {GetDisplayName(ownerID.ToString())} ({ownerID}) at location {gridLocation} {location.ToString("F1")}";
                            Puts(message);
                            LogToFile("ladders", message, this);
                        }
                    }
                }
            }
            return null;
        }

        private bool ArePlayersFriendlyToEachOther(ulong playerId, ulong otherPlayerId) {
            var player1Str = playerId.ToString();
            var player2Str = otherPlayerId.ToString();

            var result = _config.FriendlyPlayers.FirstOrDefault(e => e.Contains(player1Str) && e.Contains(player2Str));
            return !string.IsNullOrEmpty(result);
        }

        private bool ShouldSendToDiscord(string containerType) {
            return containerType != "Recycler";
        }

        private void SendDiscordDepositMessage(string looter, string owner, string item, int itemAmount, string type, string location, string gridLocation) {
            if (ShouldSendToDiscord(type)) {
                var embed = new Embed()
                    .AddField("Player", looter, true)
                    .AddField("Deposited", $"{itemAmount}x {item}", true)
                    .AddField("To", owner, true)
                    .AddField("Container type", type, true)
                    .AddField("Location", location, true)
                    .AddField("Grid", gridLocation, true)
                    .SetColor("#00ff70");

                var headers = new Dictionary<string, string>() {{"Content-Type", "application/json"}};
                const float timeout = 500f;

                webrequest.Enqueue(webhookURL, new DiscordMessage("", embed).ToJson(),  GetCallback, this,
                    RequestMethod.POST, headers, timeout);
            }
        }

        private void SendDiscordLootMessage(string looter, string owner, string item, int itemAmount, string type, string location, string gridLocation) {
            if (ShouldSendToDiscord(type)) {
                var embed = new Embed()
                    .AddField("Player", looter, true)
                    .AddField("Looted", $"{itemAmount}x {item}", true)
                    .AddField("From", owner, true)
                    .AddField("Container type", type, true)
                    .AddField("Location", location, true)
                    .AddField("Grid", gridLocation, true)
                    .SetColor("#b70c16");

                var headers = new Dictionary<string, string>() {{"Content-Type", "application/json"}};
                const float timeout = 500f;

                webrequest.Enqueue(webhookURL, new DiscordMessage("", embed).ToJson(),  GetCallback, this,
                    RequestMethod.POST, headers, timeout);   
            }  
        }

        private void GetCallback(int code, string response) {
            if (response != null && code == 204) return;
            
            Puts($"Error: {code} - Couldn't get an answer from server.");
        }

        private string GetDisplayName(string targetId) => covalence.Players.FindPlayer(targetId)?.Name ?? targetId;

        private bool IsInExcludeList(ulong OwnerID) {
            return _config.ExcludeList.Contains(OwnerID.ToString());
        }

        class StorageType
        {
            public string entityName;
            public string entityID;
            public string itemName;
            public int itemAmount;
            public string type;
            public string location;
            public string gridLocation;
            public ulong ownerID;
        }


        #region Discord Stuff
        private class DiscordMessage
        {
            public DiscordMessage(string content, params Embed[] embeds)
            {
                Content = content;
                Embeds  = embeds.ToList();
            }

            [JsonProperty("content")] public string Content { get; set; }
            [JsonProperty("embeds")] public List<Embed> Embeds { get; set; }
            

            public string ToJson() => JsonConvert.SerializeObject(this);
        }

        private class Embed
        {
            [JsonProperty("fields")] public List<Field> Fields { get; set; } = new List<Field>();
            [JsonProperty("color")] public int Color { get; set; }

            public Embed AddField(string name, string value, bool inline)
            {
                Fields.Add(new Field(name, Regex.Replace(value, "<.*?>", string.Empty), inline));

                return this;
            }
            
            public Embed SetColor(string color)
            {
                var replace = color.Replace("#", "");
                var decValue = int.Parse(replace, System.Globalization.NumberStyles.HexNumber);
                Color = decValue;
                return this;
            }
        }

        private class Field
        {
            public Field(string name, string value, bool inline)
            {
                Name = name;
                Value = value;
                Inline = inline;
            }

            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("value")] public string Value { get; set; }
            [JsonProperty("inline")] public bool Inline { get; set; }
        }
        #endregion


        #region Config
        private static ConfigData _config;

        private class ConfigData {
            [JsonProperty(PropertyName = "OwnerID's to ignore")]
            public string[] ExcludeList { get; set; }

            [JsonProperty(PropertyName = "Friendly players")]
            public string[] FriendlyPlayers { get; set; }
        }

        protected override void LoadConfig() {
            base.LoadConfig();
            try {
                _config = Config.ReadObject<ConfigData>();
                SaveConfig();
            } catch {
                PrintError("Error reading config, please check!");
            }
        }

        protected override void LoadDefaultConfig() {
            _config = GetBaseConfig();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private ConfigData GetBaseConfig() {
            return new ConfigData {
                ExcludeList = new string[] {},
                FriendlyPlayers = new string[] {}
            };
        }
        #endregion
    }
}
