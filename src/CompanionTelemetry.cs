using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    // Registro de acoes da parceira, para achar onde ela esta sendo ineficiente.
    // Arquivo: BepInEx\RCG_AICompanion_acoes.log (o da sessao anterior vira _anterior.log).
    internal static class CompanionTelemetry
    {
        private class IntentStats
        {
            public float Time;
            public int Presses;
            public int Hits;
            public int Damage;
            public int DamageTaken;
        }

        private static StreamWriter _w;
        private static float _nextFlush;
        private static float _nextSummary;
        private static float _sessionStart;
        private static int _linesThisSecond;
        private static int _dropped;
        private static float _secondStart;

        private static string _intent = string.Empty;
        private static string _lastPressIntent = string.Empty;
        private static float _lastPressAt = -10f;

        // Estatisticas do periodo (zeradas a cada resumo) e da sessao inteira.
        private static readonly Dictionary<string, IntentStats> Period = new Dictionary<string, IntentStats>();
        private static readonly Dictionary<string, IntentStats> Session = new Dictionary<string, IntentStats>();
        private static readonly Dictionary<string, int> PeriodCounters = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> SessionCounters = new Dictionary<string, int>();
        private static float _periodIdleInCombat;
        private static float _sessionIdleInCombat;
        private static float _periodCombat;
        private static float _sessionCombat;

        public static bool Enabled
        {
            get { return CompanionPlugin.ActionLog != null && CompanionPlugin.ActionLog.Value; }
        }

        public static void Begin()
        {
            if (!Enabled)
            {
                return;
            }
            try
            {
                string path = Path.Combine(Paths.BepInExRootPath, "RCG_AICompanion_acoes.log");
                string old = Path.Combine(Paths.BepInExRootPath, "RCG_AICompanion_acoes_anterior.log");
                if (File.Exists(path))
                {
                    if (File.Exists(old))
                    {
                        File.Delete(old);
                    }
                    File.Move(path, old);
                }
                _w = new StreamWriter(path, false, Encoding.UTF8);
                _w.WriteLine("=== RCG AI Companion - registro de acoes - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
                _w.WriteLine("Formato: [tempo] TIPO detalhes. Botoes: Q=rapido H=forte S=especial B=defesa D=esquiva J=pulo I=pegar R=recruta");
                _w.Flush();
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogWarning("Nao consegui criar o registro de acoes: " + e.Message);
                _w = null;
            }
        }

        private static void Line(string kind, string text)
        {
            if (_w == null)
            {
                return;
            }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _secondStart >= 1f)
            {
                if (_dropped > 0)
                {
                    _w.WriteLine("        (" + _dropped + " linhas omitidas: muitas acoes no mesmo segundo)");
                }
                _secondStart = now;
                _linesThisSecond = 0;
                _dropped = 0;
            }
            if (++_linesThisSecond > 40)
            {
                _dropped++;
                return;
            }
            _w.WriteLine("[" + UnityEngine.Time.time.ToString("0000.00") + "] " + kind.PadRight(9) + " " + text);
        }

        // ------------------------------------------------------------ Chamado todo frame pelo cerebro

        public static void Frame(string intent, string detail, AiInput o, RCG.Player p2, CombatEntity target, bool inCombat)
        {
            if (_w == null || p2 == null)
            {
                return;
            }
            float dt = UnityEngine.Time.unscaledDeltaTime;
            if (dt > 0.25f)
            {
                dt = 0.25f;
            }
            if (string.IsNullOrEmpty(intent))
            {
                intent = "?";
            }

            Stat(Period, intent).Time += dt;
            Stat(Session, intent).Time += dt;
            if (inCombat)
            {
                _periodCombat += dt;
                _sessionCombat += dt;
                bool doingNothing = o.H == 0 && o.V == 0 && !o.Quick && !o.Heavy && !o.Special && !o.Block && !o.Dodge && !o.Jump;
                string s = p2.Fsm.GetCurrentState() ?? string.Empty;
                if (doingNothing && s.EndsWith("PlayerIdle"))
                {
                    _periodIdleInCombat += dt;
                    _sessionIdleInCombat += dt;
                }
            }

            if (intent != _intent)
            {
                Line("INTENCAO", _intent + " -> " + intent + Describe(p2, target) + (string.IsNullOrEmpty(detail) ? string.Empty : " | " + detail));
                _intent = intent;
            }

            string buttons = (o.Quick ? "Q" : "") + (o.Heavy ? "H" : "") + (o.Special ? "S" : "") + (o.Dodge ? "D" : "") + (o.Jump ? "J" : "") + (o.Recruit ? "R" : "");
            if (buttons.Length > 0)
            {
                Stat(Period, intent).Presses++;
                Stat(Session, intent).Presses++;
                _lastPressIntent = intent;
                _lastPressAt = UnityEngine.Time.time;
                string dir = (o.H != 0 ? (o.H > 0 ? " dir" : " esq") : "") + (o.V != 0 ? (o.V > 0 ? " cima" : " baixo") : "");
                Line("BOTAO", buttons + dir + " | " + intent + Describe(p2, target) + " estado=" + Short(p2.Fsm.GetCurrentState()));
            }

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now >= _nextFlush)
            {
                _nextFlush = now + 1f;
                _w.Flush();
            }
            if (_nextSummary <= 0f)
            {
                _nextSummary = now + 60f;
                _sessionStart = now;
            }
            if (now >= _nextSummary)
            {
                _nextSummary = now + 60f;
                Summary("60 segundos", false);
            }
        }

        // ------------------------------------------------------------ Eventos

        public static void Hit(RCG.Player p2, object victimObj, int damage)
        {
            if (_w == null)
            {
                return;
            }
            CombatEntity victim = victimObj as CombatEntity;
            string intent = UnityEngine.Time.time - _lastPressAt < 0.8f ? _lastPressIntent : _intent;
            Stat(Period, intent).Hits++;
            Stat(Session, intent).Hits++;
            Stat(Period, intent).Damage += damage;
            Stat(Session, intent).Damage += damage;
            Line("ACERTOU", Name(victim) + " dano=" + damage + " (" + intent + ")" + (victim != null ? " vidaDele=" + victim.Stamina : string.Empty));
        }

        public static void Damaged(RCG.Player p2, object attackerObj, int damage)
        {
            if (_w == null || p2 == null)
            {
                return;
            }
            CombatEntity attacker = attackerObj as CombatEntity;
            bool behind = false;
            if (attacker != null)
            {
                float rel = (attacker.transform.position.x - p2.transform.position.x) * p2.Facing.FacingSign;
                behind = rel < -0.1f;
            }
            Stat(Period, _intent).DamageTaken += damage;
            Stat(Session, _intent).DamageTaken += damage;
            Count("Golpes recebidos");
            if (behind)
            {
                Count("Golpes recebidos pelas costas");
            }
            Line("APANHOU", "de " + Name(attacker) + " dano=" + damage + " | fazendo=" + _intent + " estado=" + Short(p2.Fsm.GetCurrentState()) + (behind ? " PELAS COSTAS" : string.Empty) + " vida=" + Mathf.RoundToInt(p2.StaminaPercent * 100f) + "%" + (attacker != null ? " golpeDele=" + Short(attacker.Fsm.GetCurrentState()) : string.Empty));
        }

        public static void Event(string kind, string text)
        {
            if (_w == null)
            {
                return;
            }
            Count(kind);
            Line(kind.ToUpper(), text);
        }

        public static void Count(string key)
        {
            int v;
            PeriodCounters.TryGetValue(key, out v);
            PeriodCounters[key] = v + 1;
            SessionCounters.TryGetValue(key, out v);
            SessionCounters[key] = v + 1;
        }

        // ------------------------------------------------------------ Resumo

        public static void Summary(string reason, bool session)
        {
            if (_w == null)
            {
                return;
            }
            Dictionary<string, IntentStats> stats = session ? Session : Period;
            Dictionary<string, int> counters = session ? SessionCounters : PeriodCounters;
            float idle = session ? _sessionIdleInCombat : _periodIdleInCombat;
            float combat = session ? _sessionCombat : _periodCombat;

            float total = 0f;
            int presses = 0, hits = 0, dmg = 0, taken = 0;
            foreach (IntentStats s in stats.Values)
            {
                total += s.Time;
                presses += s.Presses;
                hits += s.Hits;
                dmg += s.Damage;
                taken += s.DamageTaken;
            }
            if (total <= 0.1f)
            {
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("------ RESUMO " + (session ? "DA SESSAO" : "DO PERIODO") + " (" + reason + ") ------");
            sb.AppendLine("Tempo: " + total.ToString("0") + "s | em combate: " + combat.ToString("0") + "s | parada sem fazer nada em combate: " + idle.ToString("0.0") + "s (" + Pct(idle, combat) + ")");
            sb.AppendLine("Botoes: " + presses + " | acertos: " + hits + " (" + Pct(hits, presses) + " de aproveitamento) | dano causado: " + dmg + " | dano recebido: " + taken);
            sb.AppendLine(string.Format("{0,-22}{1,8}{2,8}{3,8}{4,8}{5,8}{6,10}", "Intencao", "Tempo", "%", "Botoes", "Acertos", "Taxa", "Apanhou"));
            List<KeyValuePair<string, IntentStats>> rows = new List<KeyValuePair<string, IntentStats>>(stats);
            rows.Sort((a, b) => b.Value.Time.CompareTo(a.Value.Time));
            foreach (KeyValuePair<string, IntentStats> kv in rows)
            {
                IntentStats s = kv.Value;
                sb.AppendLine(string.Format("{0,-22}{1,7:0.0}s{2,8}{3,8}{4,8}{5,8}{6,10}", kv.Key, s.Time, Pct(s.Time, total), s.Presses, s.Hits, s.Presses > 0 ? Pct(s.Hits, s.Presses) : "-", s.DamageTaken));
            }
            if (counters.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (KeyValuePair<string, int> kv in counters)
                {
                    parts.Add(kv.Key + "=" + kv.Value);
                }
                parts.Sort();
                sb.AppendLine("Eventos: " + string.Join(", ", parts.ToArray()));
            }
            sb.AppendLine("------------------------------------------------");
            _w.Write(sb.ToString());
            _w.Flush();

            if (!session)
            {
                Period.Clear();
                PeriodCounters.Clear();
                _periodIdleInCombat = 0f;
                _periodCombat = 0f;
            }
        }

        public static void End()
        {
            if (_w == null)
            {
                return;
            }
            Summary("periodo final", false);
            Summary("fim da sessao", true);
            _w.Flush();
            _w.Close();
            _w = null;
        }

        // ------------------------------------------------------------ Util

        private static IntentStats Stat(Dictionary<string, IntentStats> d, string key)
        {
            IntentStats s;
            if (!d.TryGetValue(key, out s))
            {
                s = new IntentStats();
                d[key] = s;
            }
            return s;
        }

        private static string Describe(RCG.Player p2, CombatEntity target)
        {
            string s = " | vida=" + Mathf.RoundToInt(p2.StaminaPercent * 100f) + "%";
            if (target != null && target.isActiveAndEnabled)
            {
                Vector3 d = target.transform.position - p2.transform.position;
                s += " alvo=" + Name(target) + " dx=" + d.x.ToString("0.00") + " dz=" + d.z.ToString("0.00") + " alvoEstado=" + Short(target.Fsm.GetCurrentState());
            }
            return s;
        }

        private static string Name(CombatEntity e)
        {
            return e == null ? "?" : e.name.Replace("(Clone)", string.Empty);
        }

        private static string Short(string state)
        {
            return string.IsNullOrEmpty(state) ? "-" : state.Replace("RCG.", string.Empty);
        }

        private static string Pct(float a, float b)
        {
            return b <= 0f ? "0%" : Mathf.RoundToInt(a * 100f / b) + "%";
        }
    }
}
