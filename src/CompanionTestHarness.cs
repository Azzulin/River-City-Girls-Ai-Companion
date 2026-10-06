using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    // Ferramentas de TESTE (so funcionam com Debug.ModoTeste = true).
    // Permitem simular os comandos de menu do Player 1 por dentro do jogo, sem depender do teclado
    // (no PC de desenvolvimento o teclado nao esta ligado ao Player 1 do Rewired).
    internal static class TestInput
    {
        private static readonly MethodInfo SetUIH = Setter("UI_HorizontalDir");
        private static readonly MethodInfo SetUIV = Setter("UI_VerticalDir");
        private static readonly MethodInfo SetUIJump = Setter("UI_Jump");
        private static readonly MethodInfo SetUISpecial = Setter("UI_Special");
        private static readonly MethodInfo SetUIStart = Setter("UI_StartInput");

        private static int _h;
        private static int _v;
        private static bool _jump;
        private static bool _special;
        private static bool _start;
        private static int _framesLeft;
        private static int _lastFrame = -1;

        private static MethodInfo Setter(string prop)
        {
            PropertyInfo p = typeof(PlayerInput).GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
            return p != null ? p.GetSetMethod(true) : null;
        }

        public static bool Busy
        {
            get { return _framesLeft > 0; }
        }

        // nome: confirmar, voltar, start, esquerda, direita, cima, baixo
        private static readonly List<int> _logged = new List<int>();

        public static bool Queue(string name)
        {
            _logged.Clear();
            _h = 0;
            _v = 0;
            _jump = false;
            _special = false;
            _start = false;
            switch (name)
            {
                case "confirmar": _jump = true; _framesLeft = 2; break;
                case "voltar": _special = true; _framesLeft = 2; break;
                case "start": _start = true; _framesLeft = 2; break;
                case "esquerda": _h = -1; _framesLeft = 4; break;
                case "direita": _h = 1; _framesLeft = 4; break;
                case "cima": _v = 1; _framesLeft = 4; break;
                case "baixo": _v = -1; _framesLeft = 4; break;
                default: return false;
            }
            return true;
        }

        // Chamado depois do PlayerInput.Update de cada PlayerInput do Player 1.
        public static void Apply(PlayerInput pi)
        {
            if (_framesLeft <= 0)
            {
                return;
            }
            if (!_logged.Contains(pi.GetInstanceID()))
            {
                _logged.Add(pi.GetInstanceID());
                CompanionPlugin.Log.LogInfo("TESTE ui: aplicando em " + pi.gameObject.name + " (estado=" + GameState.CurrentState + ")");
            }
            Set(SetUIH, pi, _h);
            Set(SetUIV, pi, _v);
            Set(SetUIJump, pi, _jump);
            Set(SetUISpecial, pi, _special);
            Set(SetUIStart, pi, _start);
            if (Time.frameCount != _lastFrame)
            {
                _lastFrame = Time.frameCount;
                _framesLeft--;
            }
        }

        private static void Set(MethodInfo m, PlayerInput pi, object value)
        {
            if (m != null)
            {
                m.Invoke(pi, new object[] { value });
            }
        }
    }

    [HarmonyPatch(typeof(PlayerInput), "Update")]
    internal static class TestInputPatch
    {
        private static void Postfix(PlayerInput __instance)
        {
            if (CompanionPlugin.TestMode != null && CompanionPlugin.TestMode.Value && __instance.PlayerID == 0)
            {
                TestInput.Apply(__instance);
            }
        }
    }

    // Teste automatico do relato "Game Over travado": prova o bug (sem correcao) e a correcao.
    internal static class GameOverAutoTest
    {
        private static readonly FieldInfo Selected = AccessTools.Field(typeof(UI_ContinueOrExit_Main), "_selected");
        private static readonly FieldInfo Confirmed = AccessTools.Field(typeof(UI_ContinueOrExit_Main), "bSelected");

        private static void Log(string s)
        {
            CompanionPlugin.Log.LogInfo("[TESTE GAMEOVER] " + s);
        }

        private static IEnumerator Press(string name)
        {
            TestInput.Queue(name);
            while (TestInput.Busy)
            {
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.4f);
        }

        public static IEnumerator Run()
        {
            PlayerManager pm = CompanionPlugin.PM;
            if (pm == null || pm.PlayerOne == null || pm.PlayerTwo == null)
            {
                Log("FALHOU: precisa dos dois jogadores em jogo.");
                yield break;
            }
            List<string> results = new List<string>();

            // 1) SEM a correcao (IA desligada = patches de Game Over inativos).
            CompanionPlugin.AiEnabled.Value = false;
            Log("Etapa 1: IA desligada (sem a correcao). Matando Player 1 e depois Player 2...");
            RCG.Player p1 = pm.PlayerOne;
            RCG.Player p2 = pm.PlayerTwo;
            p1.Stamina = 0;
            Singleton<PlayerDeathManager>.instance.Die(p1);
            yield return new WaitForSecondsRealtime(0.5f);
            p2.Stamina = 0;
            Singleton<PlayerDeathManager>.instance.Die(p2);

            UI_ContinueOrExit_Main ui = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                ui = UnityEngine.Object.FindObjectOfType<UI_ContinueOrExit_Main>();
                if (ui != null && ui.isActiveAndEnabled)
                {
                    break;
                }
                yield return null;
            }
            if (ui == null || !ui.isActiveAndEnabled)
            {
                Log("FALHOU: a tela de Game Over nao apareceu.");
                CompanionPlugin.AiEnabled.Value = true;
                yield break;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Log("Tela de Game Over aberta. Dono da tela = Player " + (ui._LastDeathPlayerID + 1) + ", selecao inicial = " + Sel(ui));

            int before = (int)Selected.GetValue(ui);
            yield return Press("direita");
            int after = (int)Selected.GetValue(ui);
            bool bugReproduced = ui._LastDeathPlayerID == 1 && after == before;
            results.Add("Sem correcao, Player 1 aperta DIREITA: selecao " + Name(before) + " -> " + Name(after) + (bugReproduced ? "  => BUG REPRODUZIDO (Player 1 nao controla a tela)" : "  => (bug nao reproduzido)"));
            Log(results[results.Count - 1]);

            // 2) COM a correcao (IA ligada).
            CompanionPlugin.AiEnabled.Value = true;
            yield return new WaitForSecondsRealtime(0.3f);
            Log("Etapa 2: IA ligada (com a correcao). Dono da tela agora = Player " + (ui._LastDeathPlayerID + 1));

            yield return Press("direita");
            bool okRight = (int)Selected.GetValue(ui) == 1;
            results.Add("Com correcao, Player 1 aperta DIREITA: selecao = " + Sel(ui) + (okRight ? "  => OK" : "  => FALHOU"));
            Log(results[results.Count - 1]);

            yield return Press("esquerda");
            bool okLeft = (int)Selected.GetValue(ui) == 0;
            results.Add("Com correcao, Player 1 aperta ESQUERDA: selecao = " + Sel(ui) + (okLeft ? "  => OK" : "  => FALHOU"));
            Log(results[results.Count - 1]);

            yield return Press("confirmar");
            bool okConfirm = (bool)Confirmed.GetValue(ui);
            results.Add("Com correcao, Player 1 CONFIRMA 'Continue': " + (okConfirm ? "confirmado  => OK" : "nada aconteceu  => FALHOU"));
            Log(results[results.Count - 1]);

            // 3) Depois do Continue: a fase reinicia e a parceira volta.
            float t1 = Time.realtimeSinceStartup;
            bool back = false;
            while (Time.realtimeSinceStartup - t1 < 20f)
            {
                PlayerManager pm2 = CompanionPlugin.PM;
                if (pm2 != null && pm2.PlayerOne != null && pm2.PlayerOne != p1 && pm2.PlayerTwo != null && GameState.CurrentState == GameStates.Playing)
                {
                    back = true;
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            results.Add("Depois do Continue: " + (back ? "fase reiniciou e a parceira voltou  => OK" : "fase/parceira nao voltaram em 20s  => FALHOU"));
            Log(results[results.Count - 1]);

            bool pass = okRight && okLeft && okConfirm && back;
            Log("RESULTADO: " + (pass ? "PASSOU" : "FALHOU") + (bugReproduced ? " (bug original confirmado antes da correcao)" : string.Empty));
            CompanionTelemetry.Event("TesteGameOver", (pass ? "PASSOU" : "FALHOU") + " | " + string.Join(" | ", results.ToArray()));
        }

        private static string Sel(UI_ContinueOrExit_Main ui)
        {
            return Name((int)Selected.GetValue(ui));
        }

        private static string Name(int sel)
        {
            return sel == 0 ? "Continue" : sel == 1 ? "Quit" : sel.ToString();
        }
    }
}
