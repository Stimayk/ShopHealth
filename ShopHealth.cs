using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopHealth
{
    public class ShopHealth : BasePlugin
    {
        public override string ModuleName => "[SHOP] Health";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "Health";
        public static JObject? JsonHealth { get; private set; }
        private readonly PlayerHealth[] playerHealth = new PlayerHealth[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/Health.json");
            if (File.Exists(configPath))
            {
                JsonHealth = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonHealth == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Доп. здоровье");

            var sortedItems = JsonHealth
                .Properties()
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .OrderBy(p => (int)p.Value["health"]!)
                .ToList();

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(item.Key, (string)item.Value["name"]!, CategoryName, (int)item.Value["price"]!, (int)item.Value["sellprice"]!, (int)item.Value["duration"]!);
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot => playerHealth[playerSlot] = null!);
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetNumberOfHealth(uniqueName, out int Health))
            {
                playerHealth[player.Slot] = new PlayerHealth(Health, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'health' in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetNumberOfHealth(uniqueName, out int Health))
            {
                playerHealth[player.Slot] = new PlayerHealth(Health, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerHealth[player.Slot] = null!;
            return HookResult.Continue;
        }

        [GameEventHandler]
        public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            var player = @event.Userid;
            if (player != null && !player.IsBot && playerHealth[player.Slot] != null)
            {
                GiveHealth(player);
            }
            return HookResult.Continue;
        }

        private void GiveHealth(CCSPlayerController player)
        {
            var playerPawn = player.PlayerPawn.Value;

            var healthValue = playerHealth[player.Slot].Health;

            if (healthValue <= 0 || playerPawn == null) return;

            playerPawn.Health = healthValue;
            playerPawn.MaxHealth = healthValue;
            Server.NextFrame(() => Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth"));
        }

        private static bool TryGetNumberOfHealth(string uniqueName, out int Health)
        {
            Health = 0;
            if (JsonHealth != null && JsonHealth.TryGetValue(uniqueName, out var obj) && obj is JObject jsonItem && jsonItem["health"] != null && jsonItem["health"]!.Type != JTokenType.Null)
            {
                Health = (int)jsonItem["health"]!;
                return true;
            }
            return false;
        }

        public record PlayerHealth(int Health, int ItemID);
    }
}