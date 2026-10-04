using System;
using System.Reflection;
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
            SetJump(__instance, false);
            SetJumpRelease(__instance, false);
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

    // Quando voce sai da loja, em vez de passar a vez pro controle 2, a parceira faz as compras dela.
    [HarmonyPatch(typeof(UI_StoreScreenV2), "LeaveStore")]
    internal static class StoreLeavePatch
    {
        private static readonly FieldInfo TargetPlayer = AccessTools.Field(typeof(UI_StoreScreenV2), "_targetPlayerInput");
        private static readonly FieldInfo PlayerCount = AccessTools.Field(typeof(UI_StoreScreenV2), "_playerCount");

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
            try
            {
                CompanionShopper.Shop(__instance.StoreDisplayData, pm.PlayerTwo);
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro nas compras da IA: " + e);
            }
            // Pula a vez do Player 2 na loja: o jogo fecha a loja normalmente.
            PlayerCount.SetValue(__instance, 1);
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
        private static void Postfix(RCG.Player __instance)
        {
            if (P2.Is(__instance))
            {
                CompanionPlugin.Brain.OnHitLanded();
            }
        }
    }

    // Parry: aprende o tempo do golpe quando ela apanha...
    [HarmonyPatch(typeof(RCG.Player), "DamageEvent", new Type[] { typeof(DamageInfo) })]
    internal static class PlayerDamagePatch
    {
        private static void Prefix(RCG.Player __instance, DamageInfo damageInfo)
        {
            if (P2.Is(__instance) && damageInfo != null)
            {
                AttackLearner.OnHitReceived(damageInfo.Attacker as CombatEntity);
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
                CompanionPlugin.Log.LogInfo(pm.PlayerTwo.ClassName + " subiu para o nivel " + PlayerAttributes.Instance.Players[(int)_player].Level);
            }
        }
    }
}
