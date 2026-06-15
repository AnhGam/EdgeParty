using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave;
using Unity.Services.CloudSave.Models;
using Unity.Services.Core;
using UnityEngine;
using EdgeParty.UI;

namespace EdgeParty.Auth
{
    /// <summary>
    /// Manages all persistent player data through UGS Cloud Save.
    /// All data is stored server-side, linked to the authenticated UGS player ID.
    ///
    /// Cloud Save Keys:
    ///   "Coins"                 -> int
    ///   "OwnedItems"            -> JSON array of string item IDs
    ///   "EquippedCustomization" -> JSON object with int fields
    /// </summary>
    public class CloudSaveManager : MonoBehaviour
    {
        public static CloudSaveManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<CloudSaveManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("CloudSaveManager");
                        _instance = go.AddComponent<CloudSaveManager>();
                    }
                }
                return _instance;
            }
        }
        private static CloudSaveManager _instance;

        private const string KEY_COINS       = "Coins";
        private const string KEY_OWNED_ITEMS = "OwnedItems";
        private const string KEY_EQUIPPED    = "EquippedCustomization";

        public int          CachedCoins      { get; private set; } = 1240;
        public List<string> CachedOwnedItems { get; private set; } = new List<string>();
        public EquippedOutfit CachedEquipped { get; private set; } = new EquippedOutfit();

        public bool IsLoaded { get; private set; } = false;

        public event Action OnDataLoaded;
        public event Action OnDataSaved;

        [Serializable]
        public class EquippedOutfit
        {
            public int hat      = -1;
            public int glasses  = -1;
            public int necklace = -1;
            public int emotion  = 0;
            public int color    = 0;
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Loads all player data from the cloud in one batch call.
        /// Called once right after sign-in.
        /// </summary>
        public async Task LoadAllAsync()
        {
            try
            {
                await EnsureServicesReady();

                var keys = new HashSet<string> { KEY_COINS, KEY_OWNED_ITEMS, KEY_EQUIPPED };
                var results = await CloudSaveService.Instance.Data.Player.LoadAsync(keys);

                if (results.TryGetValue(KEY_COINS, out var coinsItem))
                    CachedCoins = coinsItem.Value.GetAs<int>();
                else
                    CachedCoins = 1240;

                if (results.TryGetValue(KEY_OWNED_ITEMS, out var ownedItem))
                {
                    var list = ownedItem.Value.GetAs<List<string>>();
                    CachedOwnedItems = list ?? new List<string>();
                }
                else
                    CachedOwnedItems = new List<string>();

                if (results.TryGetValue(KEY_EQUIPPED, out var equippedItem))
                {
                    var outfit = equippedItem.Value.GetAs<EquippedOutfit>();
                    CachedEquipped = outfit ?? new EquippedOutfit();
                }
                else
                    CachedEquipped = new EquippedOutfit();

                IsLoaded = true;
                Debug.Log($"[CloudSaveManager] Loaded – Coins: {CachedCoins}, " +
                          $"Owned: {CachedOwnedItems.Count} items, " +
                          $"Hat: {CachedEquipped.hat}");
                OnDataLoaded?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CloudSaveManager] Load failed: {ex.Message}. Using defaults.");
                IsLoaded = true; // Allow game to run with defaults
                OnDataLoaded?.Invoke();
            }
        }

        /// <summary>
        /// Saves coins + owned items together (called after a shop purchase).
        /// </summary>
        public async Task SaveCoinsAndItemsAsync(int coins, List<string> ownedItems)
        {
            CachedCoins      = coins;
            CachedOwnedItems = ownedItems ?? new List<string>();
            try
            {
                await EnsureServicesReady();
                var data = new Dictionary<string, object>
                {
                    { KEY_COINS,       CachedCoins },
                    { KEY_OWNED_ITEMS, CachedOwnedItems }
                };
                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                Debug.Log($"[CloudSaveManager] Saved coins={CachedCoins}, items={CachedOwnedItems.Count}");
                OnDataSaved?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveManager] SaveCoinsAndItems failed: {ex.Message}");
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Lưu Trữ", $"Không thể lưu xu và vật phẩm lên đám mây: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the currently equipped outfit (called from the Locker UI).
        /// </summary>
        public async Task SaveEquippedOutfitAsync(int hat, int glasses, int necklace, int emotion, int color)
        {
            CachedEquipped = new EquippedOutfit
            {
                hat      = hat,
                glasses  = glasses,
                necklace = necklace,
                emotion  = emotion,
                color    = color
            };
            try
            {
                await EnsureServicesReady();
                var data = new Dictionary<string, object>
                {
                    { KEY_EQUIPPED, CachedEquipped }
                };
                await CloudSaveService.Instance.Data.Player.SaveAsync(data);
                Debug.Log($"[CloudSaveManager] Saved outfit Hat={hat} Glasses={glasses} Color={color}");
                OnDataSaved?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CloudSaveManager] SaveEquippedOutfit failed: {ex.Message}");
                StitchUIController.Instance?.ShowErrorPopup("Lỗi Lưu Trữ", $"Không thể lưu trang phục được trang bị lên đám mây: {ex.Message}");
            }
        }

        public bool OwnsItem(string itemId) => CachedOwnedItems.Contains(itemId);

        private async Task EnsureServicesReady()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
        }
    }
}
