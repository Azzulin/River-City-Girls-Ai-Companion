using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace RCGCompanion
{
    // Aprende quanto tempo cada tipo de inimigo leva entre comecar um golpe e o golpe acertar.
    // Com isso a parceira aperta defesa no instante certo (parry: janela de ~0,1s no jogo).
    internal static class AttackLearner
    {
        private class Timing
        {
            public float Delay;
            public float Spread = 0.15f; // desvio medio: se for grande, o tempo nao e confiavel
            public int Samples;
        }

        private static readonly Dictionary<CombatEntity, string> LastState = new Dictionary<CombatEntity, string>();
        private static readonly Dictionary<CombatEntity, float> AttackStart = new Dictionary<CombatEntity, float>();
        private static readonly Dictionary<CombatEntity, float> LearnedFrom = new Dictionary<CombatEntity, float>();
        private static readonly Dictionary<string, Timing> Timings = new Dictionary<string, Timing>();
        private static readonly List<CombatEntity> Dead = new List<CombatEntity>();
        private static bool _dirty;
        private static bool _loaded;
        private static float _nextCleanup;

        private static string FilePath
        {
            get { return Path.Combine(Paths.ConfigPath, "rcg.aicompanion.parry.txt"); }
        }

        public static string Key(CombatEntity e)
        {
            return e.GetType().Name + ":" + e.ClassName;
        }

        // Cada inimigo tem varios golpes com tempos diferentes: aprende por golpe tambem.
        public static string MoveKey(CombatEntity e)
        {
            UnityEngine.Object move = e.CurrentMove as UnityEngine.Object;
            if (move == null)
            {
                return null;
            }
            return Key(e) + ":" + move.name;
        }

        public static bool IsAttackState(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            return s.Contains("Attack") || s.Contains("Swing") || s.EndsWith("Throw") || s.Contains("Tackle") || s.Contains("Charge");
        }

        public static void Track(List<CombatEntity> enemies)
        {
            float now = Time.time;
            for (int i = 0; i < enemies.Count; i++)
            {
                CombatEntity e = enemies[i];
                string s = e.Fsm.GetCurrentState();
                string prev;
                LastState.TryGetValue(e, out prev);
                bool atk = IsAttackState(s);
                if (atk && (prev == null || prev != s))
                {
                    AttackStart[e] = now;
                }
                else if (!atk)
                {
                    AttackStart.Remove(e);
                }
                LastState[e] = s;
            }
            if (now >= _nextCleanup)
            {
                _nextCleanup = now + 5f;
                Cleanup();
            }
        }

        // Inimigo atordoado/apanhando as vezes "pisca" no estado de ataque por 1 frame (a IA dele tenta
        // atacar e o jogo devolve pro atordoamento). Isso NAO e golpe: so conta ataque estavel.
        public static bool TryGetAttackStart(CombatEntity e, out float start)
        {
            if (!AttackStart.TryGetValue(e, out start))
            {
                return false;
            }
            if (Time.time - start < 0.05f || IsIncapacitated(e))
            {
                return false;
            }
            return true;
        }

        public static bool IsIncapacitated(CombatEntity e)
        {
            return e.IsGroggy || e.IsGettingHit || e.IsLying || e.InKnockdown || e.IsDead;
        }

        // So devolve o tempo se ele for confiavel (2+ amostras e pouca variacao).
        public static bool TryGetDelay(CombatEntity e, out float delay)
        {
            string mk = MoveKey(e);
            Timing t;
            if (mk != null && Timings.TryGetValue(mk, out t) && t.Samples >= 2 && t.Spread <= 0.08f)
            {
                delay = t.Delay;
                return true;
            }
            if (Timings.TryGetValue(Key(e), out t) && t.Samples >= 2 && t.Spread <= 0.06f)
            {
                delay = t.Delay;
                return true;
            }
            delay = 0f;
            return false;
        }

        // Chamado quando a parceira apanha ou defende um golpe.
        public static void OnHitReceived(CombatEntity attacker)
        {
            if (attacker == null)
            {
                return;
            }
            float start;
            if (!AttackStart.TryGetValue(attacker, out start) || Time.time - start < 0.05f)
            {
                return;
            }
            float learned;
            if (LearnedFrom.TryGetValue(attacker, out learned) && Mathf.Approximately(learned, start))
            {
                return; // so o primeiro acerto de cada golpe conta
            }
            LearnedFrom[attacker] = start;
            float d = Time.time - start;
            if (d < 0.03f || d > 2f)
            {
                return;
            }
            Learn(Key(attacker), d);
            string mk = MoveKey(attacker);
            if (mk != null)
            {
                Learn(mk, d);
            }
            _dirty = true;
        }

        private static void Learn(string key, float d)
        {
            Timing t;
            if (!Timings.TryGetValue(key, out t))
            {
                t = new Timing();
                Timings[key] = t;
            }
            if (t.Samples == 0)
            {
                t.Delay = d;
            }
            else
            {
                t.Spread = t.Spread * 0.6f + Mathf.Abs(d - t.Delay) * 0.4f;
                t.Delay = t.Delay * 0.7f + d * 0.3f;
            }
            t.Samples++;
            if (CompanionPlugin.VerboseLog.Value)
            {
                CompanionPlugin.Log.LogInfo("Aprendendo " + key + ": acerta em " + t.Delay.ToString("0.000") + "s, variacao " + t.Spread.ToString("0.000") + " (" + t.Samples + " amostras)");
            }
        }

        private static void Cleanup()
        {
            Dead.Clear();
            foreach (CombatEntity e in LastState.Keys)
            {
                if (e == null || !e.isActiveAndEnabled)
                {
                    Dead.Add(e);
                }
            }
            for (int i = 0; i < Dead.Count; i++)
            {
                LastState.Remove(Dead[i]);
                AttackStart.Remove(Dead[i]);
                LearnedFrom.Remove(Dead[i]);
            }
        }

        public static void Load()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('=');
                    if (parts.Length != 2)
                    {
                        continue;
                    }
                    string[] v = parts[1].Split(';');
                    if (v.Length < 2)
                    {
                        continue;
                    }
                    Timing t = new Timing();
                    t.Delay = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture);
                    t.Samples = int.Parse(v[1]);
                    if (v.Length >= 3)
                    {
                        t.Spread = float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture);
                    }
                    Timings[parts[0]] = t;
                }
                CompanionPlugin.Log.LogInfo("Parry: " + Timings.Count + " tipos de inimigo ja conhecidos.");
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogWarning("Nao consegui ler o arquivo de parry: " + e.Message);
            }
        }

        public static void Save()
        {
            if (!_dirty)
            {
                return;
            }
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, Timing> kv in Timings)
                {
                    lines.Add(kv.Key + "=" + kv.Value.Delay.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + ";" + kv.Value.Samples + ";" + kv.Value.Spread.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture));
                }
                File.WriteAllLines(FilePath, lines.ToArray());
                _dirty = false;
            }
            catch (Exception e)
            {
                CompanionPlugin.Log.LogWarning("Nao consegui salvar o arquivo de parry: " + e.Message);
            }
        }
    }
}
