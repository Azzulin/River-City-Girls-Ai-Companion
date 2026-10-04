using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using HarmonyLib;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    // Depois que o PlayerInput le o controle, sobrescreve os botoes do Player 2 com as decisoes da IA.
    [HarmonyPatch(typeof(PlayerInput), "Update")]
    internal static class PlayerInputUpdatePatch
    {
        private static readonly Action<CombatInput, int> SetH = Setter<int>("HorizontalDir");
        private static readonly Action<CombatInput, int> SetV = Setter<int>("VerticalDir");
        private static readonly Action<CombatInput, bool> SetJump = Setter<bool>("Jump");
        private static readonly Action<CombatInput, bool> SetJumpRelease = Setter<bool>("JumpRelease");
        private static readonly Action<CombatInput, bool> SetQuick = Setter<bool>("Quick");
        private static readonly Action<CombatInput, bool> SetHeavy = Setter<bool>("Heavy");
        private static readonly Action<CombatInput, bool> SetSpecial = Setter<bool>("Special");
        private static readonly Action<CombatInput, bool> SetBlock = Setter<bool>("Block");
        private static readonly Action<CombatInput, bool> SetRecruit = Setter<bool>("Recruit");
        private static readonly Action<CombatInput, bool> SetTaunt = Setter<bool>("Taunt");
        private static readonly Action<CombatInput, bool> SetRun = Setter<bool>("Run");
        private static readonly Action<CombatInput, bool> SetDodge = Setter<bool>("Dodge");
        private static readonly Action<CombatInput, bool> SetBackAttack = Setter<bool>("BackAttack");
        private static readonly Action<CombatInput, bool> SetStart = Setter<bool>("StartInput");

        private static Action<CombatInput, T> Setter<T>(string property)
        {
            MethodInfo m = typeof(CombatInput).GetProperty(property, BindingFlags.Public | BindingFlags.Instance).GetSetMethod(true);
            return (Action<CombatInput, T>)Delegate.CreateDelegate(typeof(Action<CombatInput, T>), m);
        }

        private static void Postfix(PlayerInput __instance)
        {
            if (!CompanionPlugin.IsActive || __instance.PlayerID != 1 || __instance.LockInput || GameState.CurrentState != GameStates.Playing)
            {
                return;
            }
            PlayerManager pm = CompanionPlugin.PM;
            if (pm == null || pm.PlayerTwo == null || pm.PlayerTwo.gameObject != __instance.gameObject)
            {
                return;
            }
            AiInput o;
            try
            {
                o = CompanionPlugin.Brain.Tick(pm.PlayerTwo, pm.PlayerOne);
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro na IA: " + e);
                o = new AiInput();
            }
            bool locked = __instance.LockStandardButtonPress;
            SetH(__instance, o.H);
            SetV(__instance, o.V);
            SetJump(__instance, o.Jump && !locked);
            SetJumpRelease(__instance, o.JumpRelease);
            SetQuick(__instance, o.Quick && !locked);
            __instance.InteractQuick = o.Interact && !locked;
            SetHeavy(__instance, o.Heavy && !locked);
            SetSpecial(__instance, o.Special && !locked);
            SetBlock(__instance, o.Block && !locked);
            SetRecruit(__instance, o.Recruit && !locked);
            SetTaunt(__instance, false);
            SetRun(__instance, o.Run && o.H != 0);
            SetDodge(__instance, o.Dodge);
            SetBackAttack(__instance, false);
            SetStart(__instance, false);
        }
    }

    // Quando voce sai da loja, o jogo passa a vez pro Player 2 (tela com o nome e a carteira dela).
    // A parceira usa essa vez de verdade: compra item por item (com efeito e dinheiro descendo) e sai.
    [HarmonyPatch(typeof(UI_StoreScreenV2), "LeaveStore")]
    internal static class StoreLeavePatch
    {
        private static readonly FieldInfo TargetPlayer = AccessTools.Field(typeof(UI_StoreScreenV2), "_targetPlayerInput");
        private static readonly FieldInfo PlayerCount = AccessTools.Field(typeof(UI_StoreScreenV2), "_playerCount");
        private static readonly FieldInfo InStore = AccessTools.Field(typeof(UI_StoreScreenV2), "_inStore");
        private static readonly FieldInfo MoneyText = AccessTools.Field(typeof(UI_StoreScreenV2), "_playerMoneyText");

        private static void Prefix(UI_StoreScreenV2 __instance)
        {
            if (!CompanionPlugin.IsActive || !CompanionPlugin.ShopEnabled.Value)
            {
                return;
            }
            if ((int)TargetPlayer.GetValue(__instance) != 0 || (int)PlayerCount.GetValue(__instance) != 0)
            {
                return;
            }
            PlayerManager pm = CompanionPlugin.PM;
            if (pm == null || pm.PlayerTwo == null || !CompanionPlugin.IsAlive(pm.PlayerTwo))
            {
                return;
            }
            bool getsTurn = GlobalSettings.instance != null && !GlobalSettings.instance.SinglePlayer && pm.BothPlayersAlive() && CompanionPlugin.Instance != null;
            if (getsTurn)
            {
                // Deixa o jogo passar a vez pra ela; a corrotina faz as compras quando a tela dela abrir.
                CompanionPlugin.Instance.StartCoroutine(StoreTurn(__instance, pm.PlayerTwo));
                return;
            }
            try
            {
                CompanionShopper.Shop(__instance.StoreDisplayData, pm.PlayerTwo);
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro nas compras da IA: " + e);
            }
            PlayerCount.SetValue(__instance, 1);
        }

        private static IEnumerator StoreTurn(UI_StoreScreenV2 store, RCG.Player p2)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f && !((bool)InStore.GetValue(store) && store.CurrentPlayerInput == 1))
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.8f);

            float before = CompanionShopper.MoneyOf(p2);
            List<string> bought = null;
            try
            {
                bought = CompanionShopper.Shop(store.StoreDisplayData, p2, false);
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro nas compras da IA: " + e);
            }
            if (bought == null)
            {
                bought = new List<string>();
            }
            float after = CompanionShopper.MoneyOf(p2);
            TMP_Text money = MoneyText.GetValue(store) as TMP_Text;
            for (int i = 0; i < bought.Count; i++)
            {
                if (money != null)
                {
                    money.text = "$" + Mathf.Lerp(before, after, (i + 1f) / bought.Count).ToString("0.00");
                }
                store.PlayMoneyBuyVFX();
                if (i == 0 && store.StoreDisplayData != null)
                {
                    RCGAudio.instance.PlayOneShot(store.StoreDisplayData.VOOnPurchase);
                }
                yield return new WaitForSecondsRealtime(0.45f);
            }
            store.UpdateStoreDisplay();
            yield return new WaitForSecondsRealtime(bought.Count == 0 ? 1.2f : 0.9f);
            if (store.CurrentPlayerInput == 1)
            {
                store.LeaveStore();
            }
            if (bought.Count > 0)
            {
                CompanionPlugin.Say(bought.Count == 1 ? "Comprei " + bought[0] + "!" : "Comprei " + bought.Count + " coisas!");
            }
            else
            {
                CompanionPlugin.Say("Nada pra mim aqui!");
            }
        }
    }

    // Quando voce entra numa porta, a parceira entra junto (sem esperar a contagem de 10s).
    [HarmonyPatch(typeof(Door), "MultiPlayerCheckForSwitch")]
    internal static class DoorFollowPatch
    {
        private static readonly MethodInfo Original = AccessTools.Method(typeof(Door), "MultiPlayerCheckForSwitch");
        private static bool _inside;

        private static void Postfix(Door __instance, RCG.Player _player)
        {
            if (_inside || !CompanionPlugin.IsActive || !CompanionPlugin.FollowThroughDoors.Value || _player == null || _player.PlayerID != 0)
            {
                return;
            }
            PlayerManager pm = CompanionPlugin.PM;
            if (pm == null || !CompanionPlugin.IsAlive(pm.PlayerTwo))
            {
                return;
            }
            _inside = true;
            try
            {
                Original.Invoke(__instance, new object[] { pm.PlayerTwo });
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogWarning("Parceira nao conseguiu seguir pela porta: " + e.Message);
            }
            finally
            {
                _inside = false;
            }
        }
    }

    internal static class P2
    {
        public static bool Is(RCG.Player p)
        {
            PlayerManager pm = CompanionPlugin.PM;
            return p != null && pm != null && pm.PlayerTwo == p;
        }
    }

    // Usado para a IA ajustar o alcance: se esta acertando, esta na distancia certa.
    [HarmonyPatch(typeof(RCG.Player), "HitSomething")]
    internal static class PlayerHitPatch
    {
        private static void Prefix(IDamageable victim, out int __state)
        {
            CombatEntity v = victim as CombatEntity;
            __state = v != null ? v.Stamina : -1;
        }

        private static void Postfix(RCG.Player __instance, IDamageable victim, DamageInfo damageInfo, int __state)
        {
            if (P2.Is(__instance))
            {
                CompanionPlugin.Brain.OnHitLanded();
                CombatEntity v = victim as CombatEntity;
                int diff = v != null && __state >= 0 ? __state - v.Stamina : 0;
                // Se o jogo ainda nao descontou a vida neste ponto, usa o dano base do golpe.
                int dmg = diff > 0 ? diff : (damageInfo != null ? damageInfo.DamageAmount : 0);
                CompanionTelemetry.Hit(__instance, v, dmg);
            }
        }
    }

    // Parry: aprende o tempo do golpe quando ela apanha...
    [HarmonyPatch(typeof(RCG.Player), "DamageEvent", new Type[] { typeof(DamageInfo) })]
    internal static class PlayerDamagePatch
    {
        private static void Prefix(RCG.Player __instance, DamageInfo damageInfo, out int __state)
        {
            __state = __instance.Stamina;
            if (P2.Is(__instance) && damageInfo != null)
            {
                AttackLearner.OnHitReceived(damageInfo.Attacker as CombatEntity);
                CompanionPlugin.Brain.OnDamaged();
            }
        }

        private static void Postfix(RCG.Player __instance, DamageInfo damageInfo, int __state)
        {
            if (P2.Is(__instance) && damageInfo != null)
            {
                CompanionTelemetry.Damaged(__instance, damageInfo.Attacker, Mathf.Max(0, __state - __instance.Stamina));
            }
        }
    }

    // ...ou quando defende.
    [HarmonyPatch(typeof(RCG.Player), "BlockEvent")]
    internal static class PlayerBlockPatch
    {
        private static void Prefix(RCG.Player __instance, DamageInfo damageInfo)
        {
            if (P2.Is(__instance) && damageInfo != null)
            {
                AttackLearner.OnHitReceived(damageInfo.Attacker as CombatEntity);
                CompanionPlugin.Brain.OnBlocked();
                CombatEntity a = damageInfo.Attacker as CombatEntity;
                CompanionTelemetry.Event("Bloqueou", "golpe de " + (a != null ? a.name : "?") + " defendido");
            }
        }
    }

    [HarmonyPatch(typeof(RCG.Player), "BlockHit_PushBack")]
    internal static class PlayerParryPatch
    {
        private static void Postfix(RCG.Player __instance, bool __result)
        {
            if (__result && P2.Is(__instance))
            {
                CompanionSpeech.Say("parry", 0.8f);
                CompanionTelemetry.Event("Parry", "parry perfeito!");
                if (CompanionPlugin.VerboseLog.Value)
                {
                    CompanionPlugin.Log.LogInfo("Parry!");
                }
            }
        }
    }

    [HarmonyPatch(typeof(RCG.Player), "KilledSomething")]
    internal static class PlayerKillPatch
    {
        private static void Postfix(RCG.Player __instance)
        {
            if (P2.Is(__instance))
            {
                CompanionSpeech.Say("kill", 0.35f);
                CompanionTelemetry.Event("Derrotou", "inimigo derrotado por ela");
            }
        }
    }

    [HarmonyPatch(typeof(PlayerAttributes), "LevelUp")]
    internal static class LevelUpPatch
    {
        private static void Postfix(PlayerCharacters _player)
        {
            PlayerManager pm = CompanionPlugin.PM;
            if (pm != null && pm.PlayerTwo != null && pm.PlayerTwo.ClassNameToPlayerCharacter == _player)
            {
                CompanionSpeech.Say("nivel");
                CompanionTelemetry.Event("SubiuNivel", "nivel " + PlayerAttributes.Instance.Players[(int)_player].Level);
                CompanionPlugin.Log.LogInfo(pm.PlayerTwo.ClassName + " subiu para o nivel " + PlayerAttributes.Instance.Players[(int)_player].Level);
            }
        }
    }
}
