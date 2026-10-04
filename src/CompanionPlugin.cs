using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    [BepInPlugin("rcg.aicompanion", "RCG AI Companion", "2.0.0")]
    public class CompanionPlugin : BaseUnityPlugin
    {
        internal static CompanionPlugin Instance;
        internal static ManualLogSource Log;

        // Geral
        internal static ConfigEntry<bool> AiEnabled;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<KeyboardShortcut> TeleportKey;
        internal static ConfigEntry<bool> AutoJoin;
        internal static ConfigEntry<bool> FollowThroughDoors;

        // Combate
        internal static ConfigEntry<float> Aggressiveness;
        internal static ConfigEntry<float> BlockChance;
        internal static ConfigEntry<bool> UseSpecials;
        internal static ConfigEntry<float> LeashDistance;
        internal static ConfigEntry<float> AttackRange;
        internal static ConfigEntry<bool> AttackBeggingEnemies;

        // Cura
        internal static ConfigEntry<float> HealThreshold;
        internal static ConfigEntry<float> HealCooldown;

        // Loja
        internal static ConfigEntry<bool> ShopEnabled;
        internal static ConfigEntry<float> MoneyReserve;
        internal static ConfigEntry<int> CarryHealItems;
        internal static ConfigEntry<bool> BuyAccessories;

        // Novas habilidades
        internal static ConfigEntry<KeyboardShortcut> ModeKey;
        internal static ConfigEntry<bool> UseParry;
        internal static ConfigEntry<bool> UseWeapons;
        internal static ConfigEntry<bool> PickupFood;
        internal static ConfigEntry<bool> UseRecruits;
        internal static ConfigEntry<bool> Talk;
        internal static ConfigEntry<float> TalkFrequency;
        internal static ConfigEntry<bool> UsePlatforming;
        internal static ConfigEntry<bool> UseAirAttacks;

        internal static ConfigEntry<bool> VerboseLog;

        internal static readonly CompanionBrain Brain = new CompanionBrain();

        private static MethodInfo _spawnCheck;
        private static FieldInfo _deathRespawnInstance;
        private static FieldInfo _playerManagerInstance;

        private float _nextJoinAttempt;
        private float _nextHealCheck;
        private float _healCooldownUntil;
        private Vector3 _lastP2Pos;
        private float _stuckTimer;
        private float _farTimer;
        private readonly List<string> _pendingMessages = new List<string>();
        private float _nextMessageAt;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            AiEnabled = Config.Bind("Geral", "IAAtiva", true, "Liga/desliga a IA que controla o Player 2.");
            ToggleKey = Config.Bind("Geral", "TeclaLigarDesligar", new KeyboardShortcut(KeyCode.F8), "Liga/desliga a IA durante o jogo (desligada, um amigo pode usar o controle 2).");
            TeleportKey = Config.Bind("Geral", "TeclaChamarParceira", new KeyboardShortcut(KeyCode.F9), "Teleporta a parceira para perto de voce.");
            AutoJoin = Config.Bind("Geral", "EntrarAutomaticamente", true, "A parceira entra sozinha no jogo (como se apertassem ataque no controle 2).");
            FollowThroughDoors = Config.Bind("Geral", "SeguirPelasPortas", true, "Quando voce entra numa porta, a parceira entra junto na hora.");

            Aggressiveness = Config.Bind("Combate", "Agressividade", 0.7f, new ConfigDescription("0 = cautelosa, 1 = muito agressiva.", new AcceptableValueRange<float>(0f, 1f)));
            BlockChance = Config.Bind("Combate", "ChanceDeDefender", 0.55f, new ConfigDescription("Chance de defender quando um inimigo vai atacar ela.", new AcceptableValueRange<float>(0f, 1f)));
            UseSpecials = Config.Bind("Combate", "UsarEspeciais", true, "Usa golpes especiais quando tem barra e varios inimigos perto.");
            LeashDistance = Config.Bind("Combate", "DistanciaMaxima", 8f, "Distancia maxima de voce em que ela vai atras de inimigos.");
            AttackRange = Config.Bind("Combate", "AlcanceInicial", 1.0f, "Alcance inicial dos golpes (ela ajusta sozinha conforme acerta/erra).");
            AttackBeggingEnemies = Config.Bind("Combate", "BaterEmInimigosImplorando", false, "Se falso, ela deixa os inimigos implorando pra voce recrutar.");

            HealThreshold = Config.Bind("Cura", "VidaParaCurar", 0.35f, new ConfigDescription("Usa comida do inventario quando a vida cai abaixo dessa fracao.", new AcceptableValueRange<float>(0.05f, 0.95f)));
            HealCooldown = Config.Bind("Cura", "IntervaloEntreCuras", 6f, "Segundos minimos entre duas curas.");

            ShopEnabled = Config.Bind("Loja", "FazerCompras", true, "Quando voce sai de uma loja/dojo, ela faz as compras dela.");
            MoneyReserve = Config.Bind("Loja", "DinheiroReserva", 10f, "Quanto dinheiro ela tenta guardar (exceto para golpes do dojo).");
            CarryHealItems = Config.Bind("Loja", "ComidasNaMochila", 2, "Quantas comidas de cura ela tenta carregar.");
            BuyAccessories = Config.Bind("Loja", "ComprarAcessorios", true, "Compra acessorios e equipa sempre os 2 mais uteis que ela tiver.");

            ModeKey = Config.Bind("Geral", "TeclaOrdens", new KeyboardShortcut(KeyCode.F10), "Troca a ordem da parceira: Normal > Agressiva > Defensiva > Fica aqui.");
            UseParry = Config.Bind("Combate", "UsarParry", true, "Aprende o tempo dos golpes de cada inimigo e faz parry.");
            UseWeapons = Config.Bind("Combate", "UsarArmas", true, "Pega armas do chao e usa.");
            PickupFood = Config.Bind("Cura", "PegarComidaDoChao", true, "Pega comida que os inimigos deixam cair (se voce estiver mais longe dela).");
            UseRecruits = Config.Bind("Combate", "UsarRecrutas", true, "Chama o recruta dela quando tem muitos inimigos ou chefe, e recruta inimigos que ela agarrar.");
            UsePlatforming = Config.Bind("Movimento", "Plataforma", true, "Grava o caminho do jogador (pulos, pulos na parede, escadas) e refaz quando voce sobe/desce de plataforma. Tambem pula obstaculos quando trava.");
            UseAirAttacks = Config.Bind("Combate", "AtaquesAereos", true, "Pula para atacar: malabarismo em inimigos no ar, entrada pulando e combos aereos.");
            Talk = Config.Bind("Personalidade", "Falas", true, "Ela comenta o que acontece (texto em cima dela).");
            TalkFrequency = Config.Bind("Personalidade", "FrequenciaDasFalas", 1f, new ConfigDescription("0 = quase nunca, 1 = normal.", new AcceptableValueRange<float>(0f, 1f)));

            VerboseLog = Config.Bind("Debug", "LogDetalhado", false, "Escreve detalhes da IA no LogOutput.log do BepInEx.");

            _spawnCheck = AccessTools.Method(typeof(DeathRespawnManager), "SpawnCheck");
            _deathRespawnInstance = AccessTools.Field(typeof(DeathRespawnManager), "s_instance");
            _playerManagerInstance = AccessTools.Field(typeof(PlayerManager), "s_instance");

            AttackLearner.Load();
            new Harmony("rcg.aicompanion").PatchAll(typeof(CompanionPlugin).Assembly);
            Log.LogInfo("RCG AI Companion 2.0 carregado. F8 liga/desliga, F9 chama a parceira, F10 troca a ordem.");
        }

        private void OnApplicationQuit()
        {
            AttackLearner.Save();
        }

        private float _nextLearnerSave;

        private static readonly string[] ModeNames = { "Modo: Normal", "Modo: Agressiva!", "Modo: Defensiva", "Fico aqui!" };

        internal static PlayerManager PM
        {
            get { return _playerManagerInstance == null ? null : _playerManagerInstance.GetValue(null) as PlayerManager; }
        }

        internal static bool IsActive
        {
            get { return AiEnabled.Value; }
        }

        internal static bool IsAlive(RCG.Player p)
        {
            return p != null && p.isActiveAndEnabled && p.Stamina > 0 && !p.PlayerDeath.IsDying();
        }

        internal static void Say(string msg, bool log = true)
        {
            if (Instance != null && Instance._pendingMessages.Count < 3)
            {
                Instance._pendingMessages.Add(msg);
            }
            if (log)
            {
                Log.LogInfo(msg);
            }
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                AiEnabled.Value = !AiEnabled.Value;
                Brain.Reset();
                Log.LogInfo("IA " + (AiEnabled.Value ? "LIGADA" : "DESLIGADA"));
                PlayerManager pmT = PM;
                if (pmT != null && pmT.PlayerTwo != null)
                {
                    pmT.PlayerTwo.DisplayTextAbove(AiEnabled.Value ? "IA ON" : "IA OFF", true);
                }
            }

            if (Time.time >= _nextLearnerSave)
            {
                _nextLearnerSave = Time.time + 60f;
                AttackLearner.Save();
            }

            if (!IsActive || GameState.CurrentState != GameStates.Playing)
            {
                return;
            }

            PlayerManager pm = PM;
            if (pm == null)
            {
                return;
            }
            RCG.Player p1 = pm.PlayerOne;
            RCG.Player p2 = pm.PlayerTwo;

            if (ModeKey.Value.IsDown())
            {
                CompanionMode next = (CompanionMode)(((int)Brain.Mode + 1) % 4);
                Brain.SetMode(next, p1, p2);
                Log.LogInfo("Ordem: " + next);
                if (p2 != null)
                {
                    _pendingMessages.Clear();
                    p2.DisplayTextAbove(ModeNames[(int)next], true);
                }
            }

            if (p2 == null)
            {
                TryAutoJoin(pm, p1);
                return;
            }

            if (TeleportKey.Value.IsDown() && p1 != null)
            {
                TeleportNear(p2, p1);
            }

            ShowPendingMessages(p2);

            if (!IsAlive(p2))
            {
                return;
            }

            if (Time.time >= _nextHealCheck)
            {
                _nextHealCheck = Time.time + 0.4f;
                TryHeal(p2);
            }

            if (p1 != null && p1.isActiveAndEnabled)
            {
                CheckTeleport(p2, p1);
            }
        }

        private void TryAutoJoin(PlayerManager pm, RCG.Player p1)
        {
            if (!AutoJoin.Value || p1 == null || !IsAlive(p1) || Time.time < _nextJoinAttempt)
            {
                return;
            }
            _nextJoinAttempt = Time.time + 1.5f;
            object drm = _deathRespawnInstance == null ? null : _deathRespawnInstance.GetValue(null);
            if (drm == null || _spawnCheck == null)
            {
                return;
            }
            try
            {
                _spawnCheck.Invoke(drm, new object[] { 1 });
                if (pm.PlayerTwo != null)
                {
                    Brain.Reset();
                    Log.LogInfo("Parceira entrou no jogo: " + pm.PlayerTwo.ClassName);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Falha ao fazer a parceira entrar: " + e.Message);
            }
        }

        private void TryHeal(RCG.Player p2)
        {
            if (Time.time < _healCooldownUntil)
            {
                return;
            }
            bool inCombat = Brain.InCombat;
            float threshold = inCombat ? HealThreshold.Value : Mathf.Max(HealThreshold.Value, 0.5f);
            if (p2.StaminaPercent > threshold)
            {
                return;
            }
            PlayerCharacters character = p2.ClassNameToPlayerCharacter;
            PlayerInventory inv = PlayerGlobalInventory.instance.PlayerInventories[(int)character].UseablesInventory;
            if (inv == null)
            {
                return;
            }
            float missing = (1f - p2.StaminaPercent) * 100f;
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < inv.Items.Count; i++)
            {
                Data_InventoryItem item = inv.Items[i] as Data_InventoryItem;
                if (item == null || item.ItemNameEnglish.Contains("Merv Double"))
                {
                    continue;
                }
                float heal = item.SpecialEffect == UseableSpecialEffect.ReplenishStaminaAndSpecial ? 100f : item.StaminaRegen;
                if (heal <= 0f)
                {
                    continue;
                }
                // Prefere a menor comida que cubra o que falta; se nenhuma cobrir, a maior.
                float score = heal >= missing ? heal - missing : 1000f + (missing - heal);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            if (best < 0)
            {
                return;
            }
            Data_InventoryItem used = inv.RemoveItem(best) as Data_InventoryItem;
            if (used == null)
            {
                return;
            }
            Helper_ApplyItemToPlayer.ApplyItemToPlayer(character, used);
            p2.UpdateFromAttributes();
            _healCooldownUntil = Time.time + HealCooldown.Value;
            CompanionSpeech.Say("comer");
            Log.LogInfo(p2.ClassName + " comeu " + used.ItemNameEnglish + " (vida agora " + Mathf.RoundToInt(p2.StaminaPercent * 100f) + "%)");
        }

        private void CheckTeleport(RCG.Player p2, RCG.Player p1)
        {
            if (!IsAlive(p1))
            {
                return;
            }
            Vector3 a = p2.transform.position;
            Vector3 b = p1.transform.position;
            float dx = Mathf.Abs(a.x - b.x);
            float dy = Mathf.Abs(a.y - b.y);
            float dist = Vector3.Distance(a, b);

            if (Brain.Mode == CompanionMode.FicaAqui)
            {
                _farTimer = 0f;
            }
            else if (dx > 11f || dy > 2.5f)
            {
                _farTimer += Time.deltaTime;
            }
            else
            {
                _farTimer = 0f;
            }

            if (Brain.WantsToMove && dist > 2.5f && (a - _lastP2Pos).sqrMagnitude < 0.0004f)
            {
                _stuckTimer += Time.deltaTime;
            }
            else
            {
                _stuckTimer = 0f;
            }
            _lastP2Pos = a;

            // Navegando pelas plataformas: da tempo pra ela tentar antes de teleportar.
            bool navigating = Brain.Nav.Navigating && Brain.Nav.Fails < 3;
            float farLimit = navigating ? 12f : 2.5f;
            float stuckLimit = navigating ? 6f : 3.5f;
            if (_farTimer > farLimit || _stuckTimer > stuckLimit)
            {
                TeleportNear(p2, p1);
            }
        }

        internal void TeleportNear(RCG.Player p2, RCG.Player p1)
        {
            _farTimer = 0f;
            _stuckTimer = 0f;
            if (!p1.IsGrounded)
            {
                return;
            }
            string state = p2.Fsm.GetCurrentState() ?? string.Empty;
            if (!(state.Contains("Idle") || state.Contains("Walk") || state.Contains("Run") || state.Contains("Fall")))
            {
                return;
            }
            Vector3 pos = p1.transform.position;
            pos.x -= p1.Facing.FacingSign * 0.9f;
            p2.transform.position = pos;
            Brain.Nav.Reset();
            if (p2.EntityPhysics != null)
            {
                p2.EntityPhysics.Velocity = Vector3.zero;
            }
            _lastP2Pos = pos;
            if (VerboseLog.Value)
            {
                Log.LogInfo("Parceira teleportada para perto do jogador.");
            }
        }

        private void ShowPendingMessages(RCG.Player p2)
        {
            if (_pendingMessages.Count == 0 || Time.time < _nextMessageAt)
            {
                return;
            }
            string msg = _pendingMessages[0];
            _pendingMessages.RemoveAt(0);
            p2.DisplayTextAbove(msg, false);
            _nextMessageAt = Time.time + 1.2f;
        }
    }
}
