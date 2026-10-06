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
    [BepInPlugin("rcg.aicompanion", "RCG AI Companion", "2.10.0")]
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
        internal static ConfigEntry<bool> ActionLog;
        internal static ConfigEntry<string> PartnerCharacter;
        internal static ConfigEntry<KeyboardShortcut> PartnerKey;
        internal static ConfigEntry<bool> TestMode;

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
            CarryHealItems = Config.Bind("Loja", "MaxComidasNaMochila", 6, "Quantas comidas de cura ela tenta carregar (limitado pelos espacos da mochila dela).");
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

            PartnerCharacter = Config.Bind("Geral", "PersonagemDaParceira", "Auto", new ConfigDescription(
                "Quem a IA controla. Auto = a dupla padrao do jogo (Misako<->Kyoko, Kunio<->Riki). " +
                "Kunio e Riki so ficam disponiveis depois de zerar o jogo. Nao pode ser o mesmo personagem que o seu.",
                new AcceptableValueList<string>("Auto", "Misako", "Kyoko", "Kunio", "Riki")));
            PartnerKey = Config.Bind("Geral", "TeclaTrocarParceira", new KeyboardShortcut(KeyCode.F7), "Troca a parceira na hora (passa pelas personagens disponiveis) e lembra a escolha.");
            TestMode = Config.Bind("Debug", "ModoTeste", false, "Somente para desenvolvimento. F11 = provoca Game Over com a parceira morrendo por ultimo.");
            ActionLog = Config.Bind("Debug", "RegistroDeAcoes", true, "Grava todas as acoes dela em BepInEx\\RCG_AICompanion_acoes.log (com resumo de eficiencia a cada 60s). A sessao anterior fica em _anterior.log.");
            CompanionTelemetry.Begin();
            AttackLearner.Load();
            new Harmony("rcg.aicompanion").PatchAll(typeof(CompanionPlugin).Assembly);
            Log.LogInfo("RCG AI Companion v2.10 carregado. F7 troca a parceira, F8 liga/desliga, F9 chama a parceira, F10 troca a ordem.");
            if (TestMode.Value)
            {
                Log.LogWarning("MODO DE TESTE ATIVO (comandos em BepInEx\\teste_comando.txt, F11 = Game Over de teste).");
            }
        }

        private void OnApplicationQuit()
        {
            AttackLearner.Save();
            CompanionTelemetry.End();
        }

        private float _nextLearnerSave;
        private RCG.Player _lastP1;

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
                CompanionTelemetry.Event("IA", AiEnabled.Value ? "ligada (F8)" : "desligada (F8)");
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

            StoreWatchdog();

            if (TestMode.Value)
            {
                if (new KeyboardShortcut(KeyCode.F11).IsDown())
                {
                    StartCoroutine(TestGameOverP2Last());
                }
                PollTestCommand();
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

            // Troca de area (o jogo recria os jogadores): fecha um resumo do trecho anterior.
            if (p1 != null && p1 != _lastP1)
            {
                if (_lastP1 != null)
                {
                    CompanionTelemetry.Summary("troca de area", false);
                }
                _lastP1 = p1;
                CompanionTelemetry.Event("Area", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }

            if (PartnerKey.Value.IsDown())
            {
                CyclePartner();
            }

            if (ModeKey.Value.IsDown())
            {
                CompanionMode next = (CompanionMode)(((int)Brain.Mode + 1) % 4);
                Brain.SetMode(next, p1, p2);
                Log.LogInfo("Ordem: " + next);
                CompanionTelemetry.Event("Ordem", next.ToString());
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

        // Vigia da loja: roda mesmo com o jogo pausado. Se a vez do Player 2 ficar parada
        // (sem a rotina da IA, ou com a rotina travada), fecha a loja para o jogador nao ficar preso.
        private void StoreWatchdog()
        {
            UI_StoreScreenV2 store = UI_StoreScreenV2.Instance;
            if (!IsActive || store == null || store.CurrentPlayerInput != 1)
            {
                _storeP2Since = -1f;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (_storeP2Since < 0f)
            {
                _storeP2Since = now;
                return;
            }
            bool hung;
            string why;
            if (StoreLeavePatch.TurnActive)
            {
                hung = now - StoreLeavePatch.TurnStartedAt > 25f;
                why = "a vez dela passou de 25s";
            }
            else
            {
                hung = now - _storeP2Since > 4f;
                why = "vez do Player 2 sem a IA controlando por 4s";
            }
            if (hung)
            {
                Log.LogWarning("Loja: " + why + ". Fechando a loja para nao prender o jogador.");
                CompanionTelemetry.Event("LojaVigia", why + " -> fechando a loja");
                _storeP2Since = -1f;
                StoreLeavePatch.ForceLeave(store, "vigia: " + why);
            }
        }

        private float _storeP2Since = -1f;

        // TESTE: comandos por arquivo (BepInEx\teste_comando.txt), para testes automatizados sem depender
        // de simulacao de teclado. O arquivo e apagado depois de lido.
        private float _nextTestPoll;

        private void PollTestCommand()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextTestPoll)
            {
                return;
            }
            _nextTestPoll = now + 0.5f;
            string path = System.IO.Path.Combine(Paths.BepInExRootPath, "teste_comando.txt");
            if (!System.IO.File.Exists(path))
            {
                return;
            }
            string cmd;
            try
            {
                cmd = System.IO.File.ReadAllText(path).Trim().ToLowerInvariant();
                System.IO.File.Delete(path);
            }
            catch
            {
                return;
            }
            Log.LogInfo("TESTE comando recebido: " + cmd);
            PlayerManager pm = PM;
            switch (cmd)
            {
                case "gameover":
                    StartCoroutine(TestGameOverP2Last());
                    break;
                case "status":
                    Log.LogInfo("TESTE status: estado=" + GameState.CurrentState + " timeScale=" + Time.timeScale +
                        " P1=" + (pm != null && pm.PlayerOne != null ? pm.PlayerOne.ClassName + " vida " + pm.PlayerOne.Stamina : "-") +
                        " P2=" + (pm != null && pm.PlayerTwo != null ? pm.PlayerTwo.ClassName + " vida " + pm.PlayerTwo.Stamina : "-") +
                        " IA=" + IsActive + " parceiraConfig=" + PartnerCharacter.Value +
                        (GlobalSettings.instance != null ? " chars=" + GlobalSettings.instance.Player0Character + "/" + GlobalSettings.instance.Player1Character + " solo=" + GlobalSettings.instance.SinglePlayer : string.Empty));
                    break;
                case "inputs":
                    StartCoroutine(TestDumpInputs());
                    break;
                case "gameover_auto":
                    StartCoroutine(GameOverAutoTest.Run());
                    break;
                case "parceira_ciclo":
                    CyclePartner();
                    break;
                default:
                    if (cmd.StartsWith("ui:") && TestInput.Queue(cmd.Substring(3)))
                    {
                        break;
                    }
                    if (cmd.StartsWith("parceira:"))
                    {
                        // TESTE: troca ignorando o bloqueio de Kunio/Riki (para testar num save nao zerado).
                        try
                        {
                            PlayerCharacters c = (PlayerCharacters)Enum.Parse(typeof(PlayerCharacters), cmd.Substring(9), true);
                            Log.LogInfo("TESTE parceira: pedindo " + c + " -> " + (SwapPartner(c, true) ? "iniciado" : "recusado"));
                        }
                        catch (Exception e)
                        {
                            Log.LogWarning("TESTE parceira: " + e.Message);
                        }
                        break;
                    }
                    Log.LogWarning("TESTE comando desconhecido: " + cmd);
                    break;
            }
        }

        // TESTE: durante 4s, registra o que os PlayerInput da tela de Game Over estao lendo.
        private System.Collections.IEnumerator TestDumpInputs()
        {
            UI_ContinueOrExit_Main ui = UnityEngine.Object.FindObjectOfType<UI_ContinueOrExit_Main>();
            if (ui == null)
            {
                Log.LogWarning("TESTE inputs: tela de Game Over nao encontrada.");
                yield break;
            }
            PlayerInput[] ins = { ui._inputPlayer_0, ui._inputPlayer_1 };
            for (int k = 0; k < 2; k++)
            {
                PlayerInput pi = ins[k];
                if (pi == null)
                {
                    Log.LogInfo("TESTE inputs: _inputPlayer_" + k + " = null");
                    continue;
                }
                Rewired.Player rp = pi.RewiredPlayer;
                Log.LogInfo("TESTE inputs: _inputPlayer_" + k + " obj=" + pi.gameObject.name + " enabled=" + pi.enabled + " ativo=" + pi.isActiveAndEnabled +
                    " PlayerID=" + pi.PlayerID + " lock=" + pi.LockInput +
                    (rp != null ? " rewired: teclado=" + rp.controllers.hasKeyboard + " joysticks=" + rp.controllers.joystickCount : " rewired=null"));
            }
            float end = Time.realtimeSinceStartup + 4f;
            int lastH = 99;
            bool lastJ = false;
            while (Time.realtimeSinceStartup < end)
            {
                PlayerInput p0 = ui._inputPlayer_0;
                if (p0 != null && (p0.UI_HorizontalDir != lastH || p0.UI_Jump != lastJ))
                {
                    lastH = p0.UI_HorizontalDir;
                    lastJ = p0.UI_Jump;
                    Rewired.Player rp = p0.RewiredPlayer;
                    Log.LogInfo("TESTE inputs: P1 UI_Horizontal=" + lastH + " UI_Jump=" + lastJ + (rp != null ? " eixoBruto=" + rp.GetAxis("MoveHorizontal").ToString("0.00") + " pulo=" + rp.GetButton("Jump") : string.Empty) + " estado=" + GameState.CurrentState);
                }
                yield return null;
            }
            Log.LogInfo("TESTE inputs: fim da amostragem.");
        }

        // TESTE: reproduz o relato do Game Over travado (voce morre primeiro, a parceira por ultimo).
        private System.Collections.IEnumerator TestGameOverP2Last()
        {
            PlayerManager pm = PM;
            if (pm == null || pm.PlayerOne == null || pm.PlayerTwo == null)
            {
                Log.LogWarning("TESTE Game Over: precisa dos dois jogadores em jogo.");
                yield break;
            }
            Log.LogInfo("TESTE Game Over: matando o Player 1 e depois o Player 2...");
            RCG.Player p1 = pm.PlayerOne;
            RCG.Player p2 = pm.PlayerTwo;
            p1.Stamina = 0;
            Singleton<PlayerDeathManager>.instance.Die(p1);
            yield return new WaitForSecondsRealtime(0.5f);
            p2.Stamina = 0;
            Singleton<PlayerDeathManager>.instance.Die(p2);
            Log.LogInfo("TESTE Game Over: os dois morreram (Player 2 por ultimo). Tela deve aparecer com controle do Player 1.");
        }

        // ------------------------------------------------------------ Escolha da parceira

        private static readonly PlayerCharacters[] PartnerOrder = { PlayerCharacters.Misako, PlayerCharacters.Kyoko, PlayerCharacters.Kunio, PlayerCharacters.Riki };
        private static MethodInfo _spawnNewPlayer;
        internal bool SwapInProgress;

        private static bool BeatenGame
        {
            get { return EventManager.instance != null && EventManager.instance.GetHasBeatenGameTimes() >= 1; }
        }

        // Personagens que podem ser a parceira: nunca a mesma do jogador; Kunio/Riki so depois de zerar.
        internal static List<PlayerCharacters> AvailablePartners(bool ignoreLock)
        {
            List<PlayerCharacters> list = new List<PlayerCharacters>();
            PlayerCharacters mine = GlobalSettings.instance != null ? GlobalSettings.instance.Player0Character : PlayerCharacters.Kyoko;
            foreach (PlayerCharacters c in PartnerOrder)
            {
                bool locked = (c == PlayerCharacters.Kunio || c == PlayerCharacters.Riki) && !BeatenGame;
                if (c != mine && (ignoreLock || !locked))
                {
                    list.Add(c);
                }
            }
            return list;
        }

        // F7: passa para a proxima parceira disponivel.
        private void CyclePartner()
        {
            PlayerManager pm = PM;
            if (pm == null || pm.PlayerTwo == null)
            {
                return;
            }
            List<PlayerCharacters> options = AvailablePartners(false);
            PlayerCharacters current = pm.PlayerTwo.ClassNameToPlayerCharacter;
            if (options.Count <= 1)
            {
                string only = options.Count == 1 ? options[0].ToString() : "-";
                pm.PlayerTwo.DisplayTextAbove("So a " + only + " disponivel", true);
                Log.LogInfo("Trocar parceira: so ha uma opcao disponivel (" + only + "). Kunio/Riki liberam depois de zerar o jogo.");
                return;
            }
            int idx = options.IndexOf(current);
            PlayerCharacters next = options[(idx + 1) % options.Count];
            SwapPartner(next, false);
        }

        // Troca a parceira na hora: a atual sai (salvando o progresso dela) e a escolhida entra
        // carregando o proprio progresso. Mesmo caminho do jogo quando um jogador sai e outro entra.
        internal bool SwapPartner(PlayerCharacters wanted, bool ignoreLock)
        {
            PlayerManager pm = PM;
            RCG.Player p2 = pm != null ? pm.PlayerTwo : null;
            if (SwapInProgress || p2 == null || pm.PlayerOne == null || GlobalSettings.instance == null)
            {
                return false;
            }
            if (GameState.CurrentState != GameStates.Playing || !IsAlive(p2) || !p2.IsGrounded)
            {
                string why = GameState.CurrentState != GameStates.Playing ? "jogo nao esta em andamento" : !IsAlive(p2) ? "ela esta caida" : "ela esta no ar";
                p2.DisplayTextAbove("Agora nao da!", true);
                Log.LogInfo("Trocar parceira: agora nao da (" + why + "). Tente de novo em instantes.");
                return false;
            }
            if (!AvailablePartners(ignoreLock).Contains(wanted))
            {
                Log.LogWarning("Trocar parceira: " + wanted + " nao esta disponivel.");
                return false;
            }
            if (p2.ClassNameToPlayerCharacter == wanted)
            {
                return true;
            }
            if (_spawnNewPlayer == null)
            {
                _spawnNewPlayer = AccessTools.Method(typeof(DeathRespawnManager), "SpawnNewPlayer");
            }
            object drm = _deathRespawnInstance == null ? null : _deathRespawnInstance.GetValue(null);
            if (drm == null || _spawnNewPlayer == null)
            {
                Log.LogWarning("Trocar parceira: nao encontrei o sistema de entrada de jogadores do jogo.");
                return false;
            }
            PlayerCharacters old = p2.ClassNameToPlayerCharacter;
            PartnerCharacter.Value = wanted.ToString(); // lembra a escolha para as proximas sessoes
            StartCoroutine(DoSwap(pm, p2, wanted, drm));
            Log.LogInfo("Trocando parceira: " + old + " -> " + wanted);
            CompanionTelemetry.Event("TrocaParceira", old + " -> " + wanted);
            return true;
        }

        private System.Collections.IEnumerator DoSwap(PlayerManager pm, RCG.Player p2, PlayerCharacters wanted, object drm)
        {
            SwapInProgress = true;
            try
            {
                pm.PlayerQuit(p2); // salva o progresso dela e remove do jogo
            }
            catch (Exception e)
            {
                Log.LogError("Erro ao tirar a parceira atual: " + e);
            }
            yield return new WaitForSecondsRealtime(0.35f);
            try
            {
                GlobalSettings.instance.SetPlayerCharacter(1, wanted);
                if (UI_HUDManager.Instance != null)
                {
                    UI_HUDManager.Instance.SetPlayerHUDData(1, wanted);
                }
                _spawnNewPlayer.Invoke(drm, new object[] { 1, wanted });
                Brain.Reset();
                if (pm.PlayerTwo != null)
                {
                    pm.PlayerTwo.DisplayTextAbove("Oi! Sou a " + wanted + "!", false);
                    Log.LogInfo("Parceira agora e " + pm.PlayerTwo.ClassName + " (nivel " + PlayerAttributes.Instance.Players[(int)wanted].Level + ")");
                }
            }
            catch (Exception e)
            {
                Log.LogError("Erro ao colocar a nova parceira: " + e);
            }
            SwapInProgress = false;
        }

        // Troca o personagem do Player 2 antes de ela entrar, se o jogador escolheu um na configuracao.
        private static void ApplyPartnerChoice()
        {
            string choice = PartnerCharacter.Value;
            if (string.IsNullOrEmpty(choice) || choice == "Auto" || GlobalSettings.instance == null)
            {
                return;
            }
            PlayerCharacters wanted;
            try
            {
                wanted = (PlayerCharacters)Enum.Parse(typeof(PlayerCharacters), choice, true);
            }
            catch
            {
                return;
            }
            PlayerCharacters mine = GlobalSettings.instance.Player0Character;
            if (wanted == mine)
            {
                Log.LogWarning("PersonagemDaParceira = " + choice + " e o mesmo personagem do Player 1; usando a dupla padrao.");
                return;
            }
            bool needsBeatenGame = wanted == PlayerCharacters.Kunio || wanted == PlayerCharacters.Riki;
            if (needsBeatenGame && (EventManager.instance == null || EventManager.instance.GetHasBeatenGameTimes() < 1))
            {
                Log.LogWarning("PersonagemDaParceira = " + choice + " ainda nao esta liberado (precisa zerar o jogo); usando a dupla padrao.");
                return;
            }
            if (GlobalSettings.instance.Player1Character != wanted)
            {
                GlobalSettings.instance.SetPlayerCharacter(1, wanted);
                if (UI_HUDManager.Instance != null)
                {
                    UI_HUDManager.Instance.SetPlayerHUDData(1, wanted);
                }
                Log.LogInfo("Parceira escolhida na configuracao: " + wanted);
            }
        }

        private void TryAutoJoin(PlayerManager pm, RCG.Player p1)
        {
            if (SwapInProgress || !AutoJoin.Value || p1 == null || !IsAlive(p1) || Time.time < _nextJoinAttempt)
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
                ApplyPartnerChoice();
                _spawnCheck.Invoke(drm, new object[] { 1 });
                if (pm.PlayerTwo != null)
                {
                    Brain.Reset();
                    Log.LogInfo("Parceira entrou no jogo: " + pm.PlayerTwo.ClassName);
                    CompanionTelemetry.Event("Entrou", pm.PlayerTwo.ClassName + " entrou no jogo (vida " + Mathf.RoundToInt(pm.PlayerTwo.StaminaPercent * 100f) + "%)");
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
            CompanionTelemetry.Event("Curou", used.ItemNameEnglish + " | vida agora " + Mathf.RoundToInt(p2.StaminaPercent * 100f) + "% | " + (inCombat ? "em combate" : "fora de combate"));
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
                string why = _farTimer > farLimit ? "longe demais (dx=" + dx.ToString("0.0") + " dy=" + dy.ToString("0.0") + ")" : "presa sem sair do lugar";
                TeleportNear(p2, p1, why);
            }
        }

        internal void TeleportNear(RCG.Player p2, RCG.Player p1)
        {
            TeleportNear(p2, p1, "F9");
        }

        internal void TeleportNear(RCG.Player p2, RCG.Player p1, string why)
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
            CompanionTelemetry.Event("Teleporte", why);
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
