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

        // Retorna a lista do que ela comprou (vazia se nada).
        public static List<string> Shop(Data_Store store, RCG.Player p2, bool announce = true)
        {
            List<string> bought = new List<string>();
            if (store == null || store.Items == null || p2 == null)
            {
                return bought;
            }
            PlayerCharacters c = p2.ClassNameToPlayerCharacter;
            if (store.StoreType == StoreTypes.Dojo)
            {
                ShopDojo(store, c, bought);
            }
            else
            {
                ShopRegular(store, p2, c, bought);
            }

            string equipChange = CompanionPlugin.BuyAccessories.Value ? OptimizeEquips(c) : null;
            if (equipChange != null)
            {
                bought.Add(equipChange);
            }

            if (bought.Count == 0)
            {
                CompanionPlugin.Log.LogInfo(p2.ClassName + " nao comprou nada (dinheiro: $" + Money(c).ToString("0.00") + ").");
                CompanionTelemetry.Event("Loja", store.StoreName + ": nada comprado, dinheiro $" + Money(c).ToString("0.00"));
                return bought;
            }
            CompanionTelemetry.Event("Loja", store.StoreName + ": " + string.Join(", ", bought.ToArray()) + " | sobrou $" + Money(c).ToString("0.00"));
            p2.UpdateFromAttributes();
            PersistentData.Instance.SaveAll();
            CompanionPlugin.Log.LogInfo(p2.ClassName + " comprou: " + string.Join(", ", bought.ToArray()) + " | sobrou $" + Money(c).ToString("0.00"));
            if (announce)
            {
                CompanionPlugin.Say(bought.Count == 1 ? "Comprei " + bought[0] + "!" : "Comprei " + bought.Count + " coisas!");
            }
            return bought;
        }

        internal static float MoneyOf(RCG.Player p)
        {
            return Money(p.ClassNameToPlayerCharacter);
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

            // 2) Acessorios: compra se for melhor que o pior que ela esta usando (ou se tiver espaco livre).
            if (CompanionPlugin.BuyAccessories.Value)
            {
                equips.Sort((a, b) => EquipScore(b).CompareTo(EquipScore(a)));
                foreach (Data_EquipItem e in equips)
                {
                    if (OwnsEquip(c, e) || !PlayerGlobalInventory.instance.PlayerInventories[(int)c].EquipInventory.CheckCanAddItem(e))
                    {
                        continue;
                    }
                    int worstSlot;
                    int worstScore = WorstEquipped(c, out worstSlot);
                    if (worstSlot < 0 || EquipScore(e) <= worstScore + 10)
                    {
                        continue;
                    }
                    if (!Pay(c, e.ItemPrice, reserve))
                    {
                        continue;
                    }
                    PlayerGlobalInventory.instance.ReceiveItem(c, e);
                    bought.Add(e.ItemNameEnglish);
                }
            }

            // 2b) Se esta machucada, come na hora ate ficar quase cheia (a comida mais barata que resolve).
            int healGuard = 0;
            while (p2.StaminaPercent < 0.95f && healGuard++ < 6)
            {
                float missing = (1f - p2.StaminaPercent) * 100f;
                Data_InventoryItem pick = null;
                float pickScore = float.MaxValue;
                foreach (Data_InventoryItem f in foods)
                {
                    if (f.StaminaRegen <= 0 || Money(c) - f.ItemPrice < reserve)
                    {
                        continue;
                    }
                    float score = f.StaminaRegen >= missing ? f.ItemPrice : 10000f - f.StaminaRegen;
                    if (score < pickScore)
                    {
                        pickScore = score;
                        pick = f;
                    }
                }
                if (pick == null || !Pay(c, pick.ItemPrice, reserve))
                {
                    break;
                }
                Helper_ApplyItemToPlayer.ApplyItemToPlayer(c, pick);
                AddSeen(pick);
                bought.Add(pick.ItemNameEnglish + " (comeu pra curar)");
            }

            // 3) Comidas para carregar e se curar depois (enche os espacos livres da mochila).
            PlayerInventory useables = PlayerGlobalInventory.instance.PlayerInventories[(int)c].UseablesInventory;
            int want = CompanionPlugin.CarryHealItems.Value;
            int guard = 0;
            while (CountHealItems(useables) < want && guard++ < 12)
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

        // Nota de utilidade de cada efeito de acessorio para uma lutadora controlada por IA.
        internal static int EquipScore(Data_EquipItem e)
        {
            if (e == null)
            {
                return -1;
            }
            switch (e.EquipEffect)
            {
                case EquipEffect.InfiniteSP: return 100;
                case EquipEffect.Refill5StaminaOnKill: return 75;
                case EquipEffect.SuperArmorChance15: return 65;
                case EquipEffect.PreventStun: return 60;
                case EquipEffect.Damage10x_15PercSpeed: return 60;
                case EquipEffect.IncreasedInvulnerability2Seconds: return 55;
                case EquipEffect.SpecialFill: return 55;
                case EquipEffect.GetUpFaster: return 50;
                case EquipEffect.ReducedKnockdownChance10: return 50;
                case EquipEffect.Regain1Stamina2Minutes: return 45;
                case EquipEffect.StunDamage10: return 45;
                case EquipEffect.ExplosiveHitChance5: return 42;
                case EquipEffect.MaleDamage10: return 40;
                case EquipEffect.FemaleDamage10: return 38;
                case EquipEffect.PoisonChance10: return 38;
                case EquipEffect.AndroidDamage10: return 30;
                case EquipEffect.Reflect1Damage: return 35;
                case EquipEffect.AGminus2APplus2: return 35;
                case EquipEffect.GroundHitDamage10: return 32;
                case EquipEffect.HitThrownWeaponsBack: return 30;
                case EquipEffect.MoreItemsChance10: return 30;
                case EquipEffect.Percent20MoreMoneyChance10: return 30;
                case EquipEffect.LowerPrices10: return 30;
                case EquipEffect.NoSlowHeavy: return 28;
                case EquipEffect.ExtraRecruitHeart: return 25;
                case EquipEffect.RecruitDamage10: return 25;
                case EquipEffect.ThrowDamage10: return 22;
                case EquipEffect.SprintKnockdown: return 20;
                case EquipEffect.Speed6x: return 20;
                case EquipEffect.HasebeCharm: return 20;
                case EquipEffect.MamiCharm: return 20;
                case EquipEffect.ThrownWeaponSpeed15: return 15;
                case EquipEffect.DoubleJump: return 10;
                case EquipEffect.EnemiesTaunt10: return 8;
                case EquipEffect.FloatyJump: return 5;
                case EquipEffect.SlowTimeOnPopUp3Seconds: return 5;
                default: return 20;
            }
        }

        // Retorna a nota do pior acessorio equipado (ou -1 se tiver espaco vazio).
        private static int WorstEquipped(PlayerCharacters c, out int slot)
        {
            Data_EquipItem[] eq = PlayerAttributes.Instance.Players[(int)c].Equips;
            slot = -1;
            if (eq == null)
            {
                return int.MaxValue;
            }
            int worst = int.MaxValue;
            for (int i = 0; i < eq.Length && i < 2; i++)
            {
                int s = EquipScore(eq[i]);
                if (s < worst)
                {
                    worst = s;
                    slot = i;
                }
            }
            return worst;
        }

        // Equipa os 2 acessorios mais uteis que ela possui. Retorna uma descricao se mudou algo.
        private static string OptimizeEquips(PlayerCharacters c)
        {
            Data_EquipItem[] eq = PlayerAttributes.Instance.Players[(int)c].Equips;
            if (eq == null || eq.Length < 2)
            {
                return null;
            }
            List<Data_EquipItem> owned = new List<Data_EquipItem>();
            foreach (Data_Item it in PlayerGlobalInventory.instance.PlayerInventories[(int)c].EquipInventory.Items)
            {
                Data_EquipItem e = it as Data_EquipItem;
                if (e != null && !owned.Exists(x => x.ItemNameEnglish == e.ItemNameEnglish))
                {
                    owned.Add(e);
                }
            }
            if (owned.Count == 0)
            {
                return null;
            }
            owned.Sort((a, b) => EquipScore(b).CompareTo(EquipScore(a)));
            Data_EquipItem best0 = owned[0];
            Data_EquipItem best1 = owned.Count > 1 ? owned[1] : null;
            if (SameSet(eq[0], eq[1], best0, best1))
            {
                return null;
            }
            eq[0] = best0;
            eq[1] = best1;
            SaveEquips(c, eq);
            return "equipou " + best0.ItemNameEnglish + (best1 != null ? " + " + best1.ItemNameEnglish : string.Empty);
        }

        private static bool SameSet(Data_EquipItem a0, Data_EquipItem a1, Data_EquipItem b0, Data_EquipItem b1)
        {
            string x0 = a0 != null ? a0.ItemNameEnglish : string.Empty;
            string x1 = a1 != null ? a1.ItemNameEnglish : string.Empty;
            string y0 = b0 != null ? b0.ItemNameEnglish : string.Empty;
            string y1 = b1 != null ? b1.ItemNameEnglish : string.Empty;
            return (x0 == y0 && x1 == y1) || (x0 == y1 && x1 == y0);
        }

        // Igual ao UI_PhoneScreen_OutfitsV2.EquipItemsOnPlayer.
        private static void SaveEquips(PlayerCharacters c, Data_EquipItem[] eq)
        {
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
