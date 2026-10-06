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

        // Estado da vez dela (lido pelo vigia no CompanionPlugin).
        internal static bool TurnActive;
        internal static float TurnStartedAt = -1f;

        private static void Prefix(UI_StoreScreenV2 __instance)
        {
            // IA desligada (F8): comportamento normal do jogo (um amigo pode usar o controle 2).
            if (!CompanionPlugin.IsActive)
            {
                return;
            }
            if ((int)TargetPlayer.GetValue(__instance) != 0 || (int)PlayerCount.GetValue(__instance) != 0)
            {
                return;
            }
            PlayerManager pm = CompanionPlugin.PM;
            if (pm == null || pm.PlayerTwo == null)
            {
                return; // sem Player 2: o jogo fecha a loja normalmente
            }
            RCG.Player p2 = pm.PlayerTwo;
            bool gameGivesTurn = GlobalSettings.instance != null && !GlobalSettings.instance.SinglePlayer && pm.BothPlayersAlive();
            bool canShop = CompanionPlugin.ShopEnabled.Value && CompanionPlugin.IsAlive(p2) && CompanionPlugin.Instance != null;

            if (gameGivesTurn && canShop)
            {
                // Deixa o jogo passar a vez pra ela; a corrotina faz as compras quando a tela dela abrir.
                TurnActive = true;
                TurnStartedAt = Time.realtimeSinceStartup;
                CompanionTelemetry.Event("LojaVez", "vez dela comecou (" + SafeStoreName(__instance) + ")");
                CompanionPlugin.Instance.StartCoroutine(StoreTurn(__instance, p2));
                return;
            }

            // Qualquer outro caso: NUNCA deixa a vez do Player 2 para um controle que nao existe.
            if (canShop)
            {
                SafeShop(__instance, p2, true);
            }
            PlayerCount.SetValue(__instance, 1);
            CompanionTelemetry.Event("LojaVez", "vez dela pulada (" + (!CompanionPlugin.ShopEnabled.Value ? "compras desligadas" : !CompanionPlugin.IsAlive(p2) ? "ela esta caida" : "jogo nao daria a vez") + ")");
        }

        // Toda a vez dela fica dentro de try/finally: se QUALQUER coisa der erro no meio,
        // o finally fecha a loja mesmo assim (antes, um erro deixava o jogador preso).
        private static IEnumerator StoreTurn(UI_StoreScreenV2 store, RCG.Player p2)
        {
            try
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 6f && !SafeIsP2Ready(store))
                {
                    yield return null;
                }
                if (!SafeIsP2Ready(store))
                {
                    CompanionTelemetry.Event("LojaVez", "tela dela nao abriu direito em 6s (ex.: dojo sem golpes pra ela); comprando e saindo assim mesmo");
                }
                yield return new WaitForSecondsRealtime(0.8f);

                float before = SafeMoney(p2);
                List<string> bought = SafeShop(store, p2, false);
                float after = SafeMoney(p2);
                for (int i = 0; i < bought.Count; i++)
                {
                    SafeBuyEffect(store, Mathf.Lerp(before, after, (i + 1f) / bought.Count), i == 0);
                    yield return new WaitForSecondsRealtime(0.45f);
                }
                SafeUpdateDisplay(store);
                yield return new WaitForSecondsRealtime(bought.Count == 0 ? 1.2f : 0.9f);

                ForceLeave(store, "fim normal da vez dela");
                CompanionPlugin.Say(bought.Count == 0 ? "Nada pra mim aqui!" : bought.Count == 1 ? "Comprei " + bought[0] + "!" : "Comprei " + bought.Count + " coisas!");
            }
            finally
            {
                if (TurnActive)
                {
                    ForceLeave(store, "erro durante a vez dela");
                }
            }
        }

        // Sai da vez dela na loja (a loja fecha). Seguro para chamar mais de uma vez.
        internal static void ForceLeave(UI_StoreScreenV2 store, string reason)
        {
            TurnActive = false;
            TurnStartedAt = -1f;
            try
            {
                if (store != null && store.CurrentPlayerInput == 1)
                {
                    store.LeaveStore();
                    CompanionTelemetry.Event("LojaVez", "saiu da loja: " + reason);
                }
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro ao sair da loja (" + reason + "): " + e);
                CompanionTelemetry.Event("LojaErro", "erro ao sair: " + e.Message);
            }
        }

        private static bool SafeIsP2Ready(UI_StoreScreenV2 store)
        {
            try
            {
                return (bool)InStore.GetValue(store) && store.CurrentPlayerInput == 1;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> SafeShop(UI_StoreScreenV2 store, RCG.Player p2, bool announce)
        {
            try
            {
                List<string> b = CompanionShopper.Shop(store.StoreDisplayData, p2, announce);
                return b ?? new List<string>();
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogError("Erro nas compras da IA: " + e);
                CompanionTelemetry.Event("LojaErro", "erro nas compras: " + e.Message);
                return new List<string>();
            }
        }

        private static float SafeMoney(RCG.Player p2)
        {
            try
            {
                return CompanionShopper.MoneyOf(p2);
            }
            catch
            {
                return 0f;
            }
        }

        private static void SafeBuyEffect(UI_StoreScreenV2 store, float money, bool withVoice)
        {
            try
            {
                TMP_Text text = MoneyText.GetValue(store) as TMP_Text;
                if (text != null)
                {
                    text.text = "$" + money.ToString("0.00");
                }
                store.PlayMoneyBuyVFX();
                if (withVoice && store.StoreDisplayData != null)
                {
                    RCGAudio.instance.PlayOneShot(store.StoreDisplayData.VOOnPurchase);
                }
            }
            catch (Exception e)
            {
                CompanionTelemetry.Event("LojaErro", "efeito de compra falhou (ignorado): " + e.Message);
            }
        }

        private static void SafeUpdateDisplay(UI_StoreScreenV2 store)
        {
            try
            {
                store.UpdateStoreDisplay();
            }
            catch (Exception e)
            {
                CompanionTelemetry.Event("LojaErro", "atualizar tela falhou (ignorado): " + e.Message);
            }
        }

        private static string SafeStoreName(UI_StoreScreenV2 store)
        {
            try
            {
                return store.StoreDisplayData != null ? store.StoreDisplayData.StoreName + " / " + store.StoreDisplayData.StoreType : "?";
            }
            catch
            {
                return "?";
            }
        }
    }

    // GAME OVER: a tela "Continue / Quit" so aceita o controle de quem morreu POR ULTIMO.
    // Se a parceira (IA, sem controle fisico) morria depois de voce, ninguem conseguia escolher
    // nada (softlock relatado por tester). Com a IA ligada, a tela fica sempre com o Player 1.
    [HarmonyPatch(typeof(UI_ContinueOrExit), "SetEnable")]
    internal static class GameOverOwnerPatch
    {
        private static void Prefix(bool bEnable, ref int lastPlayerID)
        {
            if (bEnable && lastPlayerID != 0 && CompanionPlugin.IsActive)
            {
                CompanionTelemetry.Event("GameOver", "tela de Game Over era do Player " + (lastPlayerID + 1) + " (IA): passando o controle para o Player 1");
                CompanionPlugin.Log.LogInfo("Game Over: controle da tela passado para o Player 1 (a parceira morreu por ultimo).");
                lastPlayerID = 0;
            }
        }
    }

    // Segunda trava: mesmo que algo defina a tela para o Player 2, a IA ligada devolve para o Player 1.
    // O Postfix registra cada mudanca de selecao/confirmacao (usado nos testes automaticos).
    [HarmonyPatch(typeof(UI_ContinueOrExit_Main), "Update")]
    internal static class GameOverInputPatch
    {
        private static readonly FieldInfo Selected = AccessTools.Field(typeof(UI_ContinueOrExit_Main), "_selected");
        private static readonly FieldInfo Confirmed = AccessTools.Field(typeof(UI_ContinueOrExit_Main), "bSelected");
        private static int _lastSelected = -2;
        private static bool _lastConfirmed;

        private static void Prefix(UI_ContinueOrExit_Main __instance)
        {
            if (__instance._LastDeathPlayerID != 0 && CompanionPlugin.IsActive)
            {
                __instance._LastDeathPlayerID = 0;
            }
        }

        private static void Postfix(UI_ContinueOrExit_Main __instance)
        {
            int sel = (int)Selected.GetValue(__instance);
            bool conf = (bool)Confirmed.GetValue(__instance);
            if (sel != _lastSelected || conf != _lastConfirmed)
            {
                string opt = sel == 0 ? "Continue" : sel == 1 ? "Quit" : sel.ToString();
                CompanionPlugin.Log.LogInfo("Game Over: selecao=" + opt + (conf ? " CONFIRMADO" : string.Empty) + " (controle do Player " + (__instance._LastDeathPlayerID + 1) + ")");
                CompanionTelemetry.Event("GameOver", "selecao=" + opt + (conf ? " CONFIRMADO" : string.Empty));
                _lastSelected = sel;
                _lastConfirmed = conf;
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
