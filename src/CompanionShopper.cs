using System.Collections.Generic;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    // Compras da parceira: usa as mesmas funcoes que a tela de loja do jogo usa.
    internal static class CompanionShopper
    {
        private static readonly string[] AirMoveTriggers = { "Cheer Drill", "Dropkick", "Hurricane Kick", "Half Moon Kick" };
        private static readonly string[] AirMoveNames = { "Air Cheer Drill", "Jump Dropkick", "Jump Half Moon Kick", "Jump Hurricane Kick" };

        public static void Shop(Data_Store store, RCG.Player p2)
        {
            if (store == null || store.Items == null || p2 == null)
            {
                return;
            }
            PlayerCharacters c = p2.ClassNameToPlayerCharacter;
            List<string> bought = new List<string>();
            if (store.StoreType == StoreTypes.Dojo)
            {
                ShopDojo(store, c, bought);
            }
            else
            {
                ShopRegular(store, p2, c, bought);
            }

            if (bought.Count == 0)
            {
                CompanionPlugin.Log.LogInfo(p2.ClassName + " nao comprou nada (dinheiro: $" + Money(c).ToString("0.00") + ").");
                return;
            }
            p2.UpdateFromAttributes();
            PersistentData.Instance.SaveAll();
            CompanionPlugin.Log.LogInfo(p2.ClassName + " comprou: " + string.Join(", ", bought.ToArray()) + " | sobrou $" + Money(c).ToString("0.00"));
            CompanionPlugin.Say(bought.Count == 1 ? "Comprei " + bought[0] + "!" : "Comprei " + bought.Count + " coisas!");
        }

        private static void ShopDojo(Data_Store store, PlayerCharacters c, List<string> bought)
        {
            int level = PlayerAttributes.Instance.Players[(int)c].Level;
            List<Data_MovesItem> moves = new List<Data_MovesItem>();
            Data_MovesItem airMove = null;
            foreach (Data_Store.StoreDataItem sdi in store.Items)
            {
                Data_MovesItem m = sdi == null ? null : sdi.Item as Data_MovesItem;
                if (m == null || m.BelongsTo != c || !m.AvailableInStore(level))
                {
                    continue;
                }
                if (System.Array.IndexOf(AirMoveNames, m.ItemNameEnglish) >= 0)
                {
                    airMove = m;
                    continue;
                }
                if (!OwnsMove(c, m))
                {
                    moves.Add(m);
                }
            }
            // Golpes mais baratos primeiro: aprende mais coisas com o mesmo dinheiro.
            moves.Sort((a, b) => a.ItemPrice.CompareTo(b.ItemPrice));
            foreach (Data_MovesItem m in moves)
            {
                if (!Pay(c, m.ItemPrice, 0f))
                {
                    continue;
                }
                PlayerGlobalInventory.instance.ReceiveItem(c, m);
                bought.Add(m.ItemNameEnglish);
                if (airMove != null && System.Array.IndexOf(AirMoveTriggers, m.ItemNameEnglish) >= 0)
                {
                    PlayerGlobalInventory.instance.ReceiveItem(c, airMove);
                }
            }
        }

        private static void ShopRegular(Data_Store store, RCG.Player p2, PlayerCharacters c, List<string> bought)
        {
            float reserve = CompanionPlugin.MoneyReserve.Value;
            List<Data_InventoryItem> foods = new List<Data_InventoryItem>();
            List<Data_EquipItem> equips = new List<Data_EquipItem>();
            foreach (Data_Store.StoreDataItem sdi in store.Items)
            {
                if (sdi == null || sdi.Item == null || sdi.EventOnPurchase != null || sdi.Item is Data_QuestItemRequirement)
                {
                    continue;
                }
                Data_Item item = sdi.Item;
                string lower = item.name.ToLower();
                if (lower.Contains("sauna") || item.ItemNameEnglish.Contains("Merv Double"))
                {
                    continue;
                }
                if (item.ItemType == InventoryItemTypes.Useable && item is Data_InventoryItem)
                {
                    foods.Add((Data_InventoryItem)item);
                }
                else if (item.ItemType == InventoryItemTypes.Equip && item is Data_EquipItem)
                {
                    equips.Add((Data_EquipItem)item);
                }
            }

            // 1) Comer agora as comidas que ela nunca comeu: dao bonus PERMANENTE de atributo.
            foods.Sort((a, b) => (StatTotal(b) * 100 / Mathf.Max(1, b.ItemPrice)).CompareTo(StatTotal(a) * 100 / Mathf.Max(1, a.ItemPrice)));
            foreach (Data_InventoryItem f in foods)
            {
                if (StatTotal(f) <= 0 || PlayerGlobalInventory.instance.HaveYouConsumedThis(f, c))
                {
                    continue;
                }
                if (!Pay(c, f.ItemPrice, reserve))
                {
                    continue;
                }
                Helper_ApplyItemToPlayer.ApplyItemToPlayer(c, f);
                AddSeen(f);
                bought.Add(f.ItemNameEnglish + " (comeu)");
            }

            // 2) Acessorios, se tiver espaco livre para equipar.
            if (CompanionPlugin.BuyAccessories.Value)
            {
                equips.Sort((a, b) => b.ItemPrice.CompareTo(a.ItemPrice));
                foreach (Data_EquipItem e in equips)
                {
                    int slot = FreeEquipSlot(c);
                    if (slot < 0)
                    {
                        break;
                    }
                    if (OwnsEquip(c, e) || !PlayerGlobalInventory.instance.PlayerInventories[(int)c].EquipInventory.CheckCanAddItem(e))
                    {
                        continue;
                    }
                    if (!Pay(c, e.ItemPrice, reserve))
                    {
                        continue;
                    }
                    PlayerGlobalInventory.instance.ReceiveItem(c, e);
                    Equip(c, slot, e);
                    bought.Add(e.ItemNameEnglish + " (equipou)");
                }
            }

            // 3) Comidas para carregar e se curar depois.
            PlayerInventory useables = PlayerGlobalInventory.instance.PlayerInventories[(int)c].UseablesInventory;
            int want = CompanionPlugin.CarryHealItems.Value;
            int guard = 0;
            while (CountHealItems(useables) < want && guard++ < 10)
            {
                Data_InventoryItem best = null;
                float bestValue = 0f;
                foreach (Data_InventoryItem f in foods)
                {
                    if (f.StaminaRegen <= 0 || !useables.CheckCanAddItem(f) || Money(c) - f.ItemPrice < reserve)
                    {
                        continue;
                    }
                    float value = f.StaminaRegen / (float)Mathf.Max(1, f.ItemPrice);
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = f;
                    }
                }
                if (best == null || !PlayerGlobalInventory.instance.ReceiveItem(c, best))
                {
                    break;
                }
                Pay(c, best.ItemPrice, reserve, true);
                bought.Add(best.ItemNameEnglish + " (mochila)");
            }
        }

        private static float Money(PlayerCharacters c)
        {
            return PlayerGlobalInventory.instance.PlayerInventories[(int)c].Money;
        }

        // Cobra o preco igual a loja do jogo (inclusive o efeito de "gift card" de acessorios).
        private static bool Pay(PlayerCharacters c, int price, float reserve, bool alreadyChecked = false)
        {
            float p = price;
            if (!alreadyChecked && (!PlayerGlobalInventory.instance.PlayerInventories[(int)c].CanAfford(p) || Money(c) - p < reserve))
            {
                return false;
            }
            float refund = PlayerEquipEffectManager.Instance.GiftCardEffect(c, p);
            PlayerGlobalInventory.instance.TakeMonies(c, p);
            PlayerGlobalInventory.instance.ReceiveMonies(c, refund);
            return true;
        }

        private static int StatTotal(Data_InventoryItem f)
        {
            return f.Stat_ST + f.Stat_AT + f.Stat_WP + f.Stat_SP + f.Stat_AG + f.Stat_LK;
        }

        private static int CountHealItems(PlayerInventory inv)
        {
            int n = 0;
            for (int i = 0; i < inv.Items.Count; i++)
            {
                Data_InventoryItem f = inv.Items[i] as Data_InventoryItem;
                if (f != null && f.StaminaRegen > 0)
                {
                    n++;
                }
            }
            return n;
        }

        private static bool OwnsMove(PlayerCharacters c, Data_MovesItem m)
        {
            foreach (Data_Item it in PlayerGlobalInventory.instance.PlayerInventories[(int)c].MoveInventory.Items)
            {
                Data_MovesItem owned = it as Data_MovesItem;
                if (owned != null && owned.ItemNameEnglish == m.ItemNameEnglish && owned.Acquired)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool OwnsEquip(PlayerCharacters c, Data_EquipItem e)
        {
            foreach (Data_Item it in PlayerGlobalInventory.instance.PlayerInventories[(int)c].EquipInventory.Items)
            {
                if (it != null && it.ItemNameEnglish == e.ItemNameEnglish)
                {
                    return true;
                }
            }
            return false;
        }

        private static int FreeEquipSlot(PlayerCharacters c)
        {
            Data_EquipItem[] eq = PlayerAttributes.Instance.Players[(int)c].Equips;
            if (eq == null)
            {
                return -1;
            }
            for (int i = 0; i < eq.Length && i < 2; i++)
            {
                if (eq[i] == null)
                {
                    return i;
                }
            }
            return -1;
        }

        // Igual ao UI_PhoneScreen_OutfitsV2.EquipItemsOnPlayer.
        private static void Equip(PlayerCharacters c, int slot, Data_EquipItem e)
        {
            Data_EquipItem[] eq = PlayerAttributes.Instance.Players[(int)c].Equips;
            eq[slot] = e;
            List<string> names = new List<string>();
            names.Add(eq[0] != null ? eq[0].name : string.Empty);
            names.Add(eq.Length > 1 && eq[1] != null ? eq[1].name : string.Empty);
            PersistentData.Instance.EquippedItems.Set((int)c, names);
        }

        private static void AddSeen(Data_Item item)
        {
            if (!PlayerGlobalInventory.instance.HaveYouSeenThis(item))
            {
                PlayerGlobalInventory.instance.AddToSeenItems(item);
            }
        }
    }
}
