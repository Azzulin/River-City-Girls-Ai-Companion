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
            SetJump(__instance, o.Jump && !locked);
            SetJumpRelease(__instance, false);
            SetQuick(__instance, o.Quick && !locked);
            __instance.InteractQuick = false;
            SetHeavy(__instance, o.Heavy && !locked);
            SetSpecial(__instance, o.Special && !locked);
            SetBlock(__instance, o.Block && !locked);
            SetRecruit(__instance, false);
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

    // Usado para a IA ajustar o alcance: se esta acertando, esta na distancia certa.
    [HarmonyPatch(typeof(RCG.Player), "HitSomething")]
    internal static class PlayerHitPatch
    {
        private static void Postfix(RCG.Player __instance)
        {
            PlayerManager pm = CompanionPlugin.PM;
            if (pm != null && pm.PlayerTwo == __instance)
            {
                CompanionPlugin.Brain.OnHitLanded();
            }
        }
    }
}
