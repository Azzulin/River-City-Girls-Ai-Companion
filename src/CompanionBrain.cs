using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    internal struct AiInput
    {
        public int H;
        public int V;
        public bool Jump;
        public bool JumpRelease;
        public bool Quick;
        public bool Heavy;
        public bool Special;
        public bool Block;
        public bool Run;
        public bool Dodge;
        public bool Interact;
        public bool Recruit;
    }

    internal enum CompanionMode
    {
        Normal,
        Agressiva,
        Defensiva,
        FicaAqui
    }

    // Decide, a cada frame, quais "botoes" a parceira aperta.
    internal class CompanionBrain
    {
        private enum Defense
        {
            None,
            Block,
            Parry,
            Dodge
        }

        private const float MinRange = 0.45f;

        private static readonly string[] BossVulnerable = { "GetHit", "Groggy", "Knockdown", "Lie", "Dazed", "Blownback", "Bounce", "Stun", "Missed", "Collide", "BigBreath", "Taunt", "Wait" };
        private static readonly string[] BossNeutral = { "Idle", "Walk", "Run" };
        private static readonly MethodInfo GetHighestInteract = AccessTools.Method(typeof(InteractableEntityCollider), "GetHighestInteract");
        private static readonly FieldInfo StayInteractables = AccessTools.Field(typeof(InteractableEntityCollider), "_stayIInteractables");
        private static readonly FieldInfo DropV2Item = AccessTools.Field(typeof(InteractEntity_DropV2), "_item");
        private static readonly FieldInfo LightDropItem = AccessTools.Field(typeof(InteractEntity_LightDrop), "_item");

        public CompanionMode Mode = CompanionMode.Normal;
        public bool InCombat;
        public bool WantsToMove;

        private Vector3 _holdAnchor;

        // Calibracao do eixo de profundidade
        private float _zSign = 1f;
        private int _zAgreement;
        private bool _zCalibrated;
        private int _lastV;
        private float _lastZ;

        // Ataque
        private CombatEntity _target;
        private float _retargetAt;
        private int _comboStep;
        private int _quickHits = 3;
        private float _nextPressAt;
        private float _pauseUntil;
        private float _retreatUntil;
        private float _range = -1f;
        private int _missStreak;
        private int _groundPress = 2; // o log mostrou que o ataque forte acerta inimigo caido
        private int _revivePress;
        private float _defenseReadyAt;
        private float _turnStart = -1f;
        private float _turnLastCall;
        private float _turnFailUntil;
        private string _intent = string.Empty;
        private bool _p2WasDown;
        private float _p1DownAt;
        private readonly int[] _groundHits = new int[4];
        private int _groundLocked = 2; // varias sessoes confirmaram: ataque forte acerta inimigo caido
        private int _groundMissStreak;
        private readonly int[] _reviveHits = new int[3];
        private int _reviveLocked = -1;

        // Diagnostico: guarda as ultimas trocas de estado dela para achar comportamentos "piscando".
        private readonly List<string> _trace = new List<string>();
        private readonly List<float> _traceTimes = new List<float>();
        private string _traceLastState = string.Empty;
        private float _nextTraceDump;
        private float _side = -1f;
        private CombatEntity _sideTarget;
        private float _sideLockedUntil;
        private float _escapeUntil;
        private float _escapeCooldownUntil;
        private float _escapeZ;
        private int _reviveCredited = -1;
        private float _lastRevivePressAt = -10f;
        private int _groundCredited = -1;
        private float _carryIdleSince = -1f;

        // Defesa
        private CombatEntity _lastThreat;
        private float _lastThreatStart = -1f;
        private Defense _defense;
        private float _blockUntil;
        private float _dodgeCooldownUntil;

        // Outros
        private float _nextRevivePress;
        private float _recruitCooldownUntil;
        private bool _pressedRecruitInGrab;
        private CombatEntity _lastBoss;
        private bool _p1WasDown;
        private bool _wasInCombat;
        private float _combatEndedAt = -1f;
        private bool _saidLowHp;
        private Vector3 _lastP1Pos;
        private float _p1IdleSince;

        // Pegar armas/comida
        private MonoBehaviour _pickTarget;
        private float _pickGiveUpAt;
        private float _nextPickScan;
        private float _nextPickLog;
        private readonly Dictionary<int, float> _pickBlacklist = new Dictionary<int, float>();

        private readonly List<CombatEntity> _enemies = new List<CombatEntity>();

        // Plataforma e ataques aereos
        public readonly CompanionNavigator Nav = new CompanionNavigator();
        private RCG.Player _p2;
        private bool _airActive;
        private bool _airJumped;
        private float _airCreatedAt;
        private float _airStartedAt;
        private int _airPresses;
        private int _airMaxPresses;
        private float _airNextPress;
        private int _airDir;
        private bool _airShortHop;
        private bool _airEndHeavy;
        private CombatEntity _airTarget;
        private float _nextAirDecision;

        public void Reset()
        {
            _airActive = false;
            Nav.Reset();
            _target = null;
            _comboStep = 0;
            _lastThreat = null;
            _defense = Defense.None;
            _blockUntil = 0f;
            _pickTarget = null;
            InCombat = false;
            WantsToMove = false;
        }

        public void SetMode(CompanionMode mode, RCG.Player p1, RCG.Player p2)
        {
            Mode = mode;
            RCG.Player anchor = p1 != null ? p1 : p2;
            if (anchor != null)
            {
                _holdAnchor = anchor.transform.position;
            }
        }

        private float Aggressiveness
        {
            get
            {
                switch (Mode)
                {
                    case CompanionMode.Agressiva: return 1f;
                    case CompanionMode.Defensiva: return 0.15f;
                    default: return CompanionPlugin.Aggressiveness.Value;
                }
            }
        }

        private float BlockChance
        {
            get
            {
                switch (Mode)
                {
                    case CompanionMode.Agressiva: return CompanionPlugin.BlockChance.Value * 0.6f;
                    case CompanionMode.Defensiva: return Mathf.Max(CompanionPlugin.BlockChance.Value, 0.9f);
                    default: return CompanionPlugin.BlockChance.Value;
                }
            }
        }

        private float Leash
        {
            get
            {
                switch (Mode)
                {
                    case CompanionMode.Agressiva: return CompanionPlugin.LeashDistance.Value + 3f;
                    case CompanionMode.Defensiva: return Mathf.Min(CompanionPlugin.LeashDistance.Value, 3.5f);
                    case CompanionMode.FicaAqui: return 3f;
                    default: return CompanionPlugin.LeashDistance.Value;
                }
            }
        }

        public void OnHitLanded()
        {
            _missStreak = 0;
            if (_target != null && _target.IsLying)
            {
                _groundMissStreak = 0;
            }
            if (_target != null && _target.IsLying && _groundPress > 0 && _groundCredited != _groundPress)
            {
                _groundPress--; // acertou no chao: repete o mesmo botao
                _groundCredited = _groundPress;
                int b = _groundPress % 4;
                _groundHits[b]++;
                if (_groundHits[b] >= 2)
                {
                    _groundLocked = b;
                }
                if (CompanionPlugin.VerboseLog.Value)
                {
                    CompanionPlugin.Log.LogInfo("Acertou inimigo no chao com o botao " + (_groundPress % 4));
                }
            }
            float max = Mathf.Max(CompanionPlugin.AttackRange.Value, MinRange);
            _range = Mathf.Min(_range + 0.02f, max * 1.3f);
        }

        public AiInput Tick(RCG.Player p2, RCG.Player p1)
        {
            AiInput o = new AiInput();
            float now = Time.time;
            if (_range < 0f)
            {
                _range = Mathf.Max(CompanionPlugin.AttackRange.Value, MinRange);
            }

            _p2 = p2;
            CalibrateZ(p2);
            if (CompanionPlugin.UsePlatforming.Value)
            {
                Nav.Record(p1);
            }

            if (!CompanionPlugin.IsAlive(p2))
            {
                InCombat = false;
                WantsToMove = false;
                _airActive = false;
                _intent = "Caida";
                return Finish(o);
            }

            Vector3 anchorPos = Mode == CompanionMode.FicaAqui ? _holdAnchor : (p1 != null ? p1.transform.position : p2.transform.position);
            CollectEnemies(anchorPos, p2.transform.position.y);

            // No meio de um pulo de plataforma ou de um combo aereo: termina o que comecou.
            if (Nav.Navigating && !p2.IsGrounded && p1 != null && Nav.Drive(p1, p2, ref o, (int)_zSign))
            {
                _intent = "Plataforma(ar)";
                return Finish(o);
            }
            if (_airActive && TickAir(p2, now, ref o))
            {
                _intent = "ComboAereo";
                return Finish(o);
            }
            AttackLearner.Track(_enemies);
            UpdateCombatState(p2, p1, now);

            // 1) Reviver o jogador caido: ficar do lado e bater nele (eh assim que o jogo revive).
            bool p1Down = p1 != null && p1.isActiveAndEnabled && p1.Fsm.IsCurrentState<PlayerDeathLie_Down>();
            if (p1Down && !_p1WasDown)
            {
                CompanionSpeech.Say("parceiraCaiu");
                _p1DownAt = now;
                CompanionTelemetry.Event("JogadorCaiu", "indo reviver | distancia x=" + (p1.transform.position.x - p2.transform.position.x).ToString("0.00"));
            }
            else if (!p1Down && _p1WasDown && p1 != null && p1.Stamina > 0)
            {
                CompanionSpeech.Say("revivida");
                CompanionTelemetry.Event("Reviveu", "jogador de pe em " + (now - _p1DownAt).ToString("0.0") + "s");
            }
            _p1WasDown = p1Down;
            if (p1Down)
            {
                _intent = "Reviver";
                Revive(p2, p1, now, ref o);
                return Finish(o);
            }

            // 2) Segurando um inimigo agarrado: tenta recrutar se ela nao tem ajudante.
            if (HandleGrab(p2, ref o))
            {
                _intent = "Agarrando(recrutar)";
                return Finish(o);
            }

            // 3) Defesa: parry (se ja aprendeu o tempo do golpe), defesa normal ou esquiva.
            if (HandleDefense(p2, now, ref o))
            {
                return Finish(o); // HandleDefense define a intencao exata
            }

            // 4) Pegar arma/comida que ja estava indo buscar.
            if (ContinuePickup(p2, now, ref o))
            {
                _intent = "Pegar";
                return Finish(o);
            }

            // 5) Escolher alvo e lutar.
            bool lowHp = p2.StaminaPercent < 0.2f;
            CombatEntity target = PickTarget(p2, p1, lowHp);
            if (target != null)
            {
                TryRecruit(p2, target, now, ref o);
                if (o.Recruit)
                {
                    _intent = "Recruta";
                }
                else
                {
                    _intent = "Lutar";
                    Fight(p2, p1, target, ref o); // Fight refina a intencao
                }
                return Finish(o);
            }
            _comboStep = 0;

            // Acabou a luta segurando objeto pesado (lixeira, bicicleta...): arremessa longe e segue.
            if (p2.IsCarryingHeavyPickupObject() && p2.IsGrounded)
            {
                if (_carryIdleSince < 0f)
                {
                    _carryIdleSince = now;
                }
                else if (now - _carryIdleSince > 2f && now >= _nextPressAt)
                {
                    _intent = "LargarObjeto";
                    int away = p1 != null && p1.transform.position.x > p2.transform.position.x ? -1 : 1;
                    if (FaceTowards(p2, p2.transform.position.x + away, ref o))
                    {
                        return Finish(o);
                    }
                    o.Heavy = true;
                    _nextPressAt = now + 1.0f;
                    return Finish(o);
                }
            }
            else
            {
                _carryIdleSince = -1f;
            }

            // 6) Sem luta: procura comida por perto.
            if (StartPickup(p2, p1, now, ref o))
            {
                _intent = "PegarComida";
                return Finish(o);
            }

            // 7) Seguir o jogador (ou ficar no lugar). Se ele estiver em outra altura, refaz o caminho dele.
            if (Mode != CompanionMode.FicaAqui && CompanionPlugin.UsePlatforming.Value && p1 != null && Nav.NeedsPath(p1, p2) && Nav.Drive(p1, p2, ref o, (int)_zSign))
            {
                _intent = "Plataforma";
                return Finish(o);
            }
            if (Mode == CompanionMode.FicaAqui)
            {
                _intent = "FicaAqui";
                MoveTo(p2, _holdAnchor.x, _holdAnchor.z, 0.6f, 0.35f, ref o);
            }
            else if (p1 != null && p1.isActiveAndEnabled)
            {
                _intent = "Seguir";
                Vector3 pp = p1.transform.position;
                float behind = -p1.Facing.FacingSign * 1.3f;
                MoveTo(p2, pp.x + behind, pp.z + 0.35f, 0.6f, 0.35f, ref o);
            }
            return Finish(o);
        }

        private void UpdateCombatState(RCG.Player p2, RCG.Player p1, float now)
        {
            InCombat = _enemies.Count > 0;
            if (_wasInCombat && !InCombat)
            {
                _combatEndedAt = now;
            }
            if (InCombat)
            {
                _combatEndedAt = -1f;
            }
            else if (_combatEndedAt > 0f && now - _combatEndedAt > 1.2f)
            {
                _combatEndedAt = -1f;
                CompanionSpeech.Say("vitoria", 0.7f);
                _saidLowHp = false;
            }
            _wasInCombat = InCombat;

            if (InCombat && !_saidLowHp && p2.StaminaPercent < 0.25f)
            {
                _saidLowHp = true;
                CompanionSpeech.Say("vidaBaixa");
            }

            // Jogador parado por muito tempo fora de combate.
            if (p1 != null)
            {
                Vector3 pp = p1.transform.position;
                if ((pp - _lastP1Pos).sqrMagnitude > 0.01f || InCombat)
                {
                    _p1IdleSince = now;
                }
                _lastP1Pos = pp;
                if (now - _p1IdleSince > 25f)
                {
                    _p1IdleSince = now;
                    CompanionSpeech.Say("parado", 0.6f);
                }
            }
        }

        // ---------------------------------------------------------------- Reviver

        // Nos dados do jogo, a condicao de reviver (NextToDyingPlayerCondition) aceita ate 1.0 na
        // horizontal e 0.5 na profundidade; o golpe ainda precisa estar na faixa de Z do acerto.
        // Fica AO LADO (nao em cima, senao esbarra no corpo), virada, e descobre qual botao funciona.
        private void Revive(RCG.Player p2, RCG.Player p1, float now, ref AiInput o)
        {
            Vector3 me = p2.transform.position;
            Vector3 pp = p1.transform.position;

            // Detecta se o ultimo golpe acertou (o jogo coloca o caido no estado de "apanhar" deitado).
            string p1State = p1.Fsm.GetCurrentState() ?? string.Empty;
            if (p1State.EndsWith("PlayerDeathLie_GetHit") && now - _lastRevivePressAt < 0.6f && _reviveCredited != _revivePress)
            {
                _revivePress--; // funcionou: repete o mesmo botao
                _reviveCredited = _revivePress;
                int rb = _revivePress % 3;
                _reviveHits[rb]++;
                if (_reviveHits[rb] >= 2)
                {
                    _reviveLocked = rb;
                }
                if (CompanionPlugin.VerboseLog.Value)
                {
                    CompanionPlugin.Log.LogInfo("Reviver: acertou com o botao " + (_revivePress % 3));
                }
            }

            float side = me.x <= pp.x ? -1f : 1f;
            bool placed = MoveTo(p2, pp.x + side * 0.5f, pp.z, 0.22f, 0.1f, ref o);
            bool close = Mathf.Abs(pp.x - me.x) <= 0.85f && Mathf.Abs(pp.z - me.z) <= 0.16f;
            if (!placed && !close)
            {
                return;
            }
            o.H = 0;
            o.V = 0;
            o.Run = false;
            if (FaceTowards(p2, pp.x, ref o) || now < _nextRevivePress || !CanAct(p2))
            {
                return;
            }
            if (_reviveLocked >= 0)
            {
                _revivePress = _reviveLocked;
            }
            switch (_revivePress % 3)
            {
                case 0: o.Heavy = true; break;
                case 1: o.Quick = true; break;
                default: o.Quick = true; o.V = -1; break;
            }
            if (CompanionPlugin.VerboseLog.Value)
            {
                CompanionPlugin.Log.LogInfo("Reviver: botao " + (_revivePress % 3) + " dx=" + (pp.x - me.x).ToString("0.00") + " dz=" + (pp.z - me.z).ToString("0.00"));
            }
            _revivePress++;
            _lastRevivePressAt = now;
            _nextRevivePress = now + 0.3f;
        }

        // ---------------------------------------------------------------- Defesa

        private bool HandleDefense(RCG.Player p2, float now, ref AiInput o)
        {
            if (!p2.IsGrounded)
            {
                return false; // no ar nao da pra defender
            }
            // Defesa ja em andamento: segura so ate o golpe passar e larga (pra contra-atacar).
            if (_blockUntil > 0f)
            {
                if (now < _blockUntil)
                {
                    _intent = "Defesa(segurando)";
                    o.Block = true;
                    return true;
                }
                _blockUntil = 0f;
                _defenseReadyAt = now + 0.5f;
                CompanionTelemetry.Event("FimDefesa", "largou a defesa, contra-atacando");
                _pauseUntil = 0f;      // contra-ataque imediato
                _nextPressAt = now;
                _comboStep = 0;
            }

            // Depois de uma defesa/esquiva, um respiro antes de decidir outra (evita "piscar" a defesa).
            if (now < _defenseReadyAt)
            {
                return false;
            }

            float start;
            CombatEntity threat = FindThreat(p2, out start);
            if (threat == null)
            {
                _lastThreat = null;
                _defense = Defense.None;
                return false;
            }
            if (threat != _lastThreat || !Mathf.Approximately(start, _lastThreatStart))
            {
                _lastThreat = threat;
                _lastThreatStart = start;
                _defense = ChooseDefense(threat, now);
                float kd;
                bool kn = AttackLearner.TryGetDelay(threat, out kd);
                CompanionTelemetry.Event("Ameaca", threat.name + " golpe=" + (threat.Fsm.GetCurrentState() ?? "").Replace("RCG.", "") + " comecou ha " + (now - start).ToString("0.00") + "s dx=" + (threat.transform.position.x - p2.transform.position.x).ToString("0.00") + " decisao=" + _defense + (kn ? " tempoConhecido=" + kd.ToString("0.00") + "s" : " tempoDesconhecido"));
                if (CompanionPlugin.VerboseLog.Value && _defense != Defense.None)
                {
                    CompanionPlugin.Log.LogInfo("Defesa: " + _defense + " contra " + threat.name + " (" + threat.Fsm.GetCurrentState() + ", golpe ha " + (now - start).ToString("0.00") + "s)");
                }
            }
            if (_defense == Defense.None)
            {
                return false;
            }

            if (_defense == Defense.Dodge)
            {
                if (now < _dodgeCooldownUntil || !CanAct(p2))
                {
                    return false;
                }
                float dz = p2.transform.position.z - threat.transform.position.z;
                int dir = Mathf.Abs(dz) < 0.05f ? (Random.value < 0.5f ? 1 : -1) : (dz > 0f ? 1 : -1);
                o.Dodge = true;
                o.V = dir * (int)_zSign;
                _dodgeCooldownUntil = now + 1.2f;
                _defenseReadyAt = now + 0.5f;
                _defense = Defense.None;
                _intent = "Esquiva";
                CompanionTelemetry.Count("Esquivas");
                CompanionSpeech.Say("esquiva", 0.3f);
                return true;
            }

            // Defesa so funciona de frente: vira para o inimigo antes de defender.
            if (FaceTowards(p2, threat.transform.position.x, ref o))
            {
                _intent = "Defesa(virando)";
                return true;
            }

            float delay;
            bool known = AttackLearner.TryGetDelay(threat, out delay);
            bool timed = _defense == Defense.Parry && known;
            if (timed)
            {
                float hitAt = start + delay;
                if (now < hitAt - 0.06f)
                {
                    _intent = "Parry(esperando)";
                    return true; // espera o instante do parry parada (sem se comprometer com ataque)
                }
                _blockUntil = Mathf.Max(now, hitAt) + 0.15f;
                _intent = "Parry";
                CompanionTelemetry.Count("Tentativas de parry");
            }
            else
            {
                // Sem tempo confiavel: defende ja e segura no maximo 0,45s (ou ate bloquear o golpe).
                _blockUntil = now + 0.45f;
                _intent = "Defesa";
                CompanionTelemetry.Count("Defesas iniciadas");
            }
            _defense = Defense.None;
            o.Block = true;
            return true;
        }

        // Chamado quando ela bloqueia um golpe: larga a defesa logo depois.
        public void OnBlocked()
        {
            if (_blockUntil > 0f)
            {
                _blockUntil = Mathf.Min(_blockUntil, Time.time + 0.1f);
            }
        }

        // Chamado quando ela apanha: nao adianta continuar defendendo.
        public void OnDamaged()
        {
            _blockUntil = 0f;
        }

        private Defense ChooseDefense(CombatEntity threat, float now)
        {
            bool boss = IsBoss(threat);
            float r = Random.value;
            float block = BlockChance;
            float delay;
            bool learned = AttackLearner.TryGetDelay(threat, out delay);
            if (threat.Unblockable || boss)
            {
                return r < Mathf.Max(block, 0.5f) + 0.2f ? Defense.Dodge : Defense.None;
            }
            if (r < block)
            {
                return learned && CompanionPlugin.UseParry.Value ? Defense.Parry : Defense.Block;
            }
            if (r < block + 0.15f && now >= _dodgeCooldownUntil)
            {
                return Defense.Dodge;
            }
            return Defense.None;
        }

        // So conta como ameaca um golpe que JA comecou, vindo de um inimigo perto,
        // virado para ela e na mesma faixa de profundidade.
        private CombatEntity FindThreat(RCG.Player p2, out float start)
        {
            Vector3 me = p2.transform.position;
            CombatEntity best = null;
            float bestStart = 0f;
            float bestDist = float.MaxValue;
            float now = Time.time;
            for (int i = 0; i < _enemies.Count; i++)
            {
                CombatEntity e = _enemies[i];
                float s;
                if (!AttackLearner.TryGetAttackStart(e, out s) || now - s > 1.0f)
                {
                    continue;
                }
                Vector3 ep = e.transform.position;
                float dx = me.x - ep.x;
                float reach = IsBoss(e) ? 3.5f : 1.9f;
                if (Mathf.Abs(dx) > reach || Mathf.Abs(ep.z - me.z) > 0.5f)
                {
                    continue;
                }
                int facingMe = dx >= 0f ? 1 : -1;
                if (Mathf.Abs(dx) > 0.15f && e.Facing.FacingSign != facingMe)
                {
                    continue; // golpe virado para o outro lado
                }
                EnemyEnitity ee = e as EnemyEnitity;
                if (ee != null && ee._target != null && ee._target != p2 && Mathf.Abs(dx) > 0.9f)
                {
                    continue; // atacando outra pessoa, longe dela
                }
                float d = Mathf.Abs(dx);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                    bestStart = s;
                }
            }
            start = bestStart;
            return best;
        }

        private static bool CanAct(RCG.Player p2)
        {
            string s = p2.Fsm.GetCurrentState() ?? string.Empty;
            return s.Contains("Idle") || s.Contains("Walk") || s.Contains("Run");
        }

        // ---------------------------------------------------------------- Ataque

        private void Fight(RCG.Player p2, RCG.Player p1, CombatEntity target, ref AiInput o)
        {
            float now = Time.time;
            Vector3 me = p2.transform.position;
            Vector3 t = target.transform.position;
            float aggr = Aggressiveness;
            bool boss = IsBoss(target);
            bool vulnerable = false;

            if (boss)
            {
                if (target != _lastBoss)
                {
                    _lastBoss = target;
                    CompanionSpeech.Say("chefe");
                }
                string s = target.Fsm.GetCurrentState() ?? string.Empty;
                vulnerable = ContainsAny(s, BossVulnerable);
                bool neutral = ContainsAny(s, BossNeutral);
                // Chefe fazendo algo perigoso, ou ela acabou de bater: recua mantendo distancia.
                if ((!vulnerable && !neutral) || (now < _retreatUntil && !vulnerable))
                {
                    float away = me.x <= t.x ? -1f : 1f;
                    float zOff = (me.z >= t.z ? 1f : -1f) * 0.9f;
                    MoveTo(p2, t.x + away * 2.8f, t.z + zOff, 0.4f, 0.25f, ref o);
                    _comboStep = 0;
                    _intent = "Chefe(recuar)";
                    return;
                }
            }

            // Inimigo caido: o golpe no chao (condicao "EnemyLie" nos dados do jogo) sai com ela a ate
            // 1.0 na horizontal e 0.5 na profundidade, e o caido sendo o inimigo mais proximo dela.
            // Fica AO LADO (nao em cima, senao esbarra no corpo), virada pra ele.
            // Inimigo se levantando nao toma dano: nao gasta golpe, fica na distancia esperando ele levantar.
            string tState = target.Fsm.GetCurrentState() ?? string.Empty;
            if (tState.Contains("Getup"))
            {
                float wside = me.x <= t.x ? -1f : 1f;
                MoveTo(p2, t.x + wside * _range * 1.1f, t.z, 0.25f, 0.2f, ref o);
                FaceTowards(p2, t.x, ref o);
                _intent = "EsperandoLevantar";
                _comboStep = 0;
                return;
            }

            if (target.IsLying && target.CanBeGroundhit && !p2.IsCarryingPickupObject())
            {
                _intent = "AtacarCaido";
                float gside = me.x <= t.x ? -1f : 1f;
                bool placed = MoveTo(p2, t.x + gside * 0.55f, t.z, 0.25f, 0.18f, ref o);
                bool closeG = Mathf.Abs(t.x - me.x) <= 0.9f && Mathf.Abs(t.z - me.z) <= 0.35f;
                if (placed || closeG)
                {
                    o.H = 0;
                    o.V = 0;
                    o.Run = false;
                    if (!FaceTowards(p2, t.x, ref o) && now >= _nextPressAt)
                    {
                        // Nao da pra saber pelos dados qual botao o combo usa: alterna ate acertar.
                        if (_groundLocked >= 0)
                        {
                            _groundPress = _groundLocked; // ja sabe qual botao funciona
                            if (++_groundMissStreak > 4)
                            {
                                // 4 golpes seguidos sem acertar com o botao "certo": volta a testar os outros.
                                CompanionTelemetry.Event("ChaoBotao", "botao " + _groundLocked + " errou 4 seguidas, testando os outros");
                                _groundLocked = -1;
                                _groundMissStreak = 0;
                            }
                        }
                        switch (_groundPress % 4)
                        {
                            case 2: o.Heavy = true; break;
                            case 3: o.Quick = true; o.V = -1; break;
                            default: o.Quick = true; break;
                        }
                        if (CompanionPlugin.VerboseLog.Value)
                        {
                            CombatEntity closest = p2.GetEnemyTarget();
                            CompanionPlugin.Log.LogInfo("Chao: botao " + (_groundPress % 4) + " dx=" + (t.x - me.x).ToString("0.00") + " dz=" + (t.z - me.z).ToString("0.00") + " alvoDoJogo=" + (closest == target ? "ok" : (closest == null ? "nada" : closest.name)) + " estado=" + p2.Fsm.GetCurrentState());
                        }
                        _groundPress++;
                        _nextPressAt = now + Random.Range(0.22f, 0.3f);
                    }
                }
                _comboStep = 0;
                return;
            }

            // Cercada (inimigo dos dois lados): esquiva pra fora da linha e se reposiciona.
            if (HandleSurrounded(p2, now, ref o))
            {
                _intent = "Cercada(saindo)";
                return;
            }

            float rangeMul = p2.IsCarryingPickupObject() ? 1.35f : 1f;
            float range = _range * rangeMul;
            float side = ChooseSide(p2, p1, target, range, now);
            float standX = t.x + side * range * 0.85f;
            float zTol = Mathf.Clamp(p2.ZDiffHitTol / 100f, 0.12f, 0.5f) * 0.7f;
            bool stunned = ContainsAny(target.Fsm.GetCurrentState() ?? string.Empty, BossVulnerable);

            // Precisa trocar de lado: contorna o inimigo por fora da linha dele (nao atravessa).
            if (Mathf.Sign(me.x - t.x) != side && Mathf.Abs(me.x - t.x) < 2.2f)
            {
                float zOut = t.z + (me.z >= t.z ? 0.8f : -0.8f);
                MoveTo(p2, standX, zOut, 0.25f, 0.15f, ref o);
                _intent = "Contornar";
                return;
            }

            // Espacamento: entre um combo e outro recua um pouco (exceto se ele estiver atordoado).
            if (now < _pauseUntil && !stunned && _comboStep == 0)
            {
                float back = Mode == CompanionMode.Defensiva ? 1.9f : Mathf.Lerp(1.6f, 1.05f, Aggressiveness);
                MoveTo(p2, t.x + side * range * back, t.z, 0.25f, 0.3f, ref o);
                FaceTowards(p2, t.x, ref o);
                _intent = "Espacamento";
                return;
            }
            float dx = Mathf.Abs(t.x - me.x);
            float adz = Mathf.Abs(t.z - me.z);
            int toward = t.x >= me.x ? 1 : -1;

            // Ataques aereos.
            bool canAir = CompanionPlugin.UseAirAttacks.Value && p2.IsGrounded && CanAct(p2) && now >= _nextAirDecision && !p2.IsCarryingHeavyPickupObject();
            if (canAir)
            {
                // Inimigo jogado pro alto: pula e emenda golpes no ar (malabarismo).
                if (!target.IsGrounded && !target.IsLying && dx < 1.6f && adz < zTol * 1.8f)
                {
                    _nextAirDecision = now + 0.4f;
                    if (Random.value < 0.65f + aggr * 0.25f)
                    {
                        StartAir(target, toward, 3, false, Random.value < 0.4f);
                        TickAir(p2, now, ref o);
                        _intent = "Malabarismo";
                        return;
                    }
                }
                // Entrada pulando: chega no inimigo com um pulo baixo e chute.
                else if (dx > range * 1.3f && dx < 2.3f && adz < zTol && !boss)
                {
                    _nextAirDecision = now + 0.8f;
                    if (Random.value < 0.15f + aggr * 0.25f)
                    {
                        StartAir(target, toward, 1, true, false);
                        TickAir(p2, now, ref o);
                        _intent = "EntradaPulando";
                        return;
                    }
                }
            }

            bool inPlace = MoveTo(p2, standX, t.z, 0.22f, zTol, ref o);
            // "Perto o suficiente" exige uma distancia minima: colada no inimigo ela nao consegue
            // virar nem acertar, entao primeiro recua para a distancia de golpe.
            bool closeEnough = dx <= range * 1.15f && dx >= range * 0.4f && Mathf.Abs(t.z - me.z) <= zTol * 1.4f;
            if (!inPlace && !closeEnough)
            {
                _intent = dx < range * 0.4f ? "Recuar(colada)" : "Aproximar";
                return;
            }
            o.H = 0;
            o.Run = false;

            if (FaceTowards(p2, t.x, ref o))
            {
                _intent = "Virar";
                return;
            }

            // Fogo amigo ligado: nao bate se voce estiver na frente dela.
            if (GlobalSettings.instance != null && GlobalSettings.instance.FriendlyFire && p1 != null && p1.isActiveAndEnabled)
            {
                Vector3 pp = p1.transform.position;
                float ahead = (pp.x - me.x) * p2.Facing.FacingSign;
                if (ahead > 0f && ahead < range * 1.3f && Mathf.Abs(pp.z - me.z) < zTol * 1.5f)
                {
                    o.V = (pp.z > me.z ? -1 : 1) * (int)_zSign;
                    _intent = "EvitarFogoAmigo";
                    return;
                }
            }

            if (now < _nextPressAt || now < _pauseUntil)
            {
                _intent = _comboStep > 0 ? "Combo" : "AguardandoAtaque";
                return;
            }
            _intent = "Combo";

            if (CompanionPlugin.UseSpecials.Value && _comboStep == 0 && p2.SpecialPercent >= 0.5f && (CountEnemiesNear(me, 2.5f) >= 2 || (boss && vulnerable)) && Random.value < 0.35f + aggr * 0.2f)
            {
                o.Special = true;
                _nextPressAt = now + 0.7f;
                RegisterSwing();
                _intent = "Especial";
                return;
            }

            // De vez em quando troca o combo de chao por um combo aereo.
            if (_comboStep == 0 && CompanionPlugin.UseAirAttacks.Value && !boss && p2.IsGrounded && now >= _nextAirDecision && Random.value < 0.08f + aggr * 0.07f)
            {
                _nextAirDecision = now + 1.5f;
                StartAir(target, toward, 2, false, Random.value < 0.5f);
                TickAir(p2, now, ref o);
                _intent = "ComboAereo";
                return;
            }

            int maxQuick = boss && !vulnerable ? 2 : _quickHits;
            if (_comboStep < maxQuick)
            {
                o.Quick = true;
                _comboStep++;
                _nextPressAt = now + Random.Range(0.13f, 0.2f);
            }
            else
            {
                o.Heavy = true;
                float r = Random.value;
                if (r < 0.2f)
                {
                    o.V = 1;
                }
                else if (r < 0.35f)
                {
                    o.V = -1;
                }
                else if (r < 0.6f)
                {
                    o.H = (int)p2.Facing.FacingSign;
                }
                _comboStep = 0;
                _quickHits = Random.Range(2, 5);
                _pauseUntil = now + Mathf.Lerp(0.9f, 0.2f, aggr) + Random.value * 0.3f;
                _nextPressAt = _pauseUntil;
                if (boss && !vulnerable)
                {
                    _retreatUntil = now + Mathf.Lerp(1.4f, 0.6f, aggr);
                }
            }
            RegisterSwing();
        }

        // ---------------------------------------------------------------- Posicionamento

        // Escolhe de que lado do inimigo atacar: perto dela, longe de outros inimigos e,
        // se possivel, do lado oposto ao jogador (pinca: o inimigo fica entre as duas).
        private float ChooseSide(RCG.Player p2, RCG.Player p1, CombatEntity target, float range, float now)
        {
            if (target == _sideTarget && now < _sideLockedUntil)
            {
                return _side;
            }
            Vector3 me = p2.transform.position;
            Vector3 t = target.transform.position;
            float best = me.x <= t.x ? -1f : 1f;
            float bestScore = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                Vector3 spot = new Vector3(t.x + s * range * 0.85f, t.y, t.z);
                float score = Mathf.Abs(spot.x - me.x) + Mathf.Abs(spot.z - me.z) * 1.5f;
                if (Mathf.Sign(me.x - t.x) != s && Mathf.Abs(me.x - t.x) < 2.2f)
                {
                    score += 1.2f; // teria que contornar
                }
                score += 1.8f * EnemiesNear(spot, 1.3f, target);
                if (p1 != null && p1.isActiveAndEnabled && p1.Stamina > 0)
                {
                    Vector3 pp = p1.transform.position;
                    if (Mathf.Abs(pp.x - t.x) < 2.5f && Mathf.Abs(pp.z - t.z) < 0.8f && Mathf.Sign(pp.x - t.x) == s)
                    {
                        score += 2.0f; // o jogador ja esta desse lado
                    }
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            _side = best;
            _sideTarget = target;
            _sideLockedUntil = now + 1.5f;
            return best;
        }

        private bool HandleSurrounded(RCG.Player p2, float now, ref AiInput o)
        {
            Vector3 me = p2.transform.position;
            if (now < _escapeUntil)
            {
                MoveTo(p2, me.x, _escapeZ, 5f, 0.15f, ref o);
                return true;
            }
            if (now < _escapeCooldownUntil || !CanAct(p2))
            {
                return false;
            }
            int left = 0;
            int right = 0;
            float zSum = 0f;
            for (int i = 0; i < _enemies.Count; i++)
            {
                CombatEntity e = _enemies[i];
                if (e.IsLying)
                {
                    continue;
                }
                Vector3 ep = e.transform.position;
                float dx = ep.x - me.x;
                if (Mathf.Abs(dx) < 1.6f && Mathf.Abs(ep.z - me.z) < 0.6f)
                {
                    if (dx < 0f)
                    {
                        left++;
                    }
                    else
                    {
                        right++;
                    }
                    zSum += ep.z;
                }
            }
            if (left == 0 || right == 0)
            {
                return false;
            }
            // Foge na profundidade para o lado com menos inimigos e usa a esquiva (que tem invencibilidade).
            float avgZ = zSum / (left + right);
            int dir = me.z >= avgZ ? 1 : -1;
            _escapeZ = me.z + dir * 1.0f;
            _escapeUntil = now + 0.7f;
            _escapeCooldownUntil = now + 2.5f;
            o.Dodge = true;
            o.V = dir * (int)_zSign;
            CompanionTelemetry.Event("Cercada", left + " inimigo(s) a esquerda x " + right + " a direita, esquivando na profundidade");
            if (CompanionPlugin.VerboseLog.Value)
            {
                CompanionPlugin.Log.LogInfo("Cercada (" + left + " x " + right + "): saindo da linha.");
            }
            return true;
        }

        private int EnemiesNear(Vector3 pos, float radius, CombatEntity except)
        {
            int n = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                CombatEntity e = _enemies[i];
                if (e == except || e.IsLying)
                {
                    continue;
                }
                Vector3 ep = e.transform.position;
                if (Mathf.Abs(ep.x - pos.x) <= radius && Mathf.Abs(ep.z - pos.z) <= 0.7f)
                {
                    n++;
                }
            }
            return n;
        }

        // ---------------------------------------------------------------- Combo aereo

        private void StartAir(CombatEntity target, int dir, int presses, bool shortHop, bool endHeavy)
        {
            _airActive = true;
            _airJumped = false;
            _airCreatedAt = Time.time;
            _airTarget = target;
            _airDir = dir;
            _airPresses = 0;
            _airMaxPresses = presses;
            _airShortHop = shortHop;
            _airEndHeavy = endHeavy;
            _comboStep = 0;
        }

        private bool TickAir(RCG.Player p2, float now, ref AiInput o)
        {
            if (!_airJumped)
            {
                if (now - _airCreatedAt > 0.4f)
                {
                    _airActive = false;
                    return false;
                }
                if (p2.IsGrounded && CanAct(p2))
                {
                    o.Jump = true;
                    o.H = _airDir;
                    _airJumped = true;
                    _airStartedAt = now;
                    _airNextPress = now + (_airShortHop ? 0.12f : 0.2f);
                }
                return true;
            }

            float t = now - _airStartedAt;
            if ((t > 0.2f && p2.IsGrounded) || t > 1.6f)
            {
                _airActive = false;
                _pauseUntil = now + 0.15f;
                return false;
            }

            // Mira no inimigo enquanto esta no ar.
            Vector3 me = p2.transform.position;
            if (_airTarget != null && _airTarget.isActiveAndEnabled)
            {
                Vector3 tp = _airTarget.transform.position;
                float dx = tp.x - me.x;
                o.H = Mathf.Abs(dx) > 0.35f ? (dx > 0f ? 1 : -1) : 0;
                float dz = tp.z - me.z;
                if (Mathf.Abs(dz) > 0.1f)
                {
                    o.V = (dz > 0f ? 1 : -1) * (int)_zSign;
                }
            }
            else
            {
                o.H = _airDir;
            }

            if (_airShortHop && t >= 0.06f && t < 0.1f)
            {
                o.JumpRelease = true; // pulo baixo
            }

            if (_airPresses < _airMaxPresses && now >= _airNextPress)
            {
                bool last = _airPresses == _airMaxPresses - 1;
                if (last && _airEndHeavy)
                {
                    o.Heavy = true;
                }
                else
                {
                    o.Quick = true;
                }
                _airPresses++;
                _airNextPress = now + 0.15f;
                RegisterSwing();
            }
            return true;
        }

        private void RegisterSwing()
        {
            _missStreak++;
            if (_missStreak >= 8)
            {
                _missStreak = 0;
                _range = Mathf.Max(MinRange, _range * 0.85f);
                CompanionTelemetry.Event("Alcance", "8 golpes seguidos sem acertar: alcance reduzido para " + _range.ToString("0.00"));
                if (CompanionPlugin.VerboseLog.Value)
                {
                    CompanionPlugin.Log.LogInfo("Errando muito, diminuindo alcance para " + _range.ToString("0.00"));
                }
            }
        }

        // ---------------------------------------------------------------- Recrutas

        private void TryRecruit(RCG.Player p2, CombatEntity target, float now, ref AiInput o)
        {
            if (!CompanionPlugin.UseRecruits.Value || now < _recruitCooldownUntil || !CanAct(p2))
            {
                return;
            }
            PlayerRecruit rec = p2._playerRecruit;
            if (rec == null || rec.State != PlayerRecruit.RecruitState.Idle || rec.mAssistHealth <= 0 || rec._EnemySummon != null)
            {
                return;
            }
            bool crowded = CountEnemiesNear(p2.transform.position, 4f) >= 3;
            bool bossFight = IsBoss(target) && Random.value < 0.5f;
            _recruitCooldownUntil = now + 3f;
            if (crowded || bossFight)
            {
                o.Recruit = true;
                CompanionSpeech.Say("recruta");
            }
        }

        private bool HandleGrab(RCG.Player p2, ref AiInput o)
        {
            if (!p2.Fsm.IsCurrentState<PlayerMasterIdle>())
            {
                _pressedRecruitInGrab = false;
                return false;
            }
            PlayerRecruit rec = p2._playerRecruit;
            if (CompanionPlugin.UseRecruits.Value && !_pressedRecruitInGrab && rec != null && rec.mAssistHealth <= 0)
            {
                _pressedRecruitInGrab = true;
                o.Recruit = true;
                return true;
            }
            return false; // segue o fluxo normal: bate/arremessa quem esta agarrado
        }

        // ---------------------------------------------------------------- Armas e comida no chao

        private bool StartPickup(RCG.Player p2, RCG.Player p1, float now, ref AiInput o)
        {
            if (now < _nextPickScan || Mode == CompanionMode.FicaAqui)
            {
                return false;
            }
            _nextPickScan = now + 0.5f;
            MonoBehaviour best = FindPickup(p2, p1, false, now);
            if (best == null)
            {
                return false;
            }
            _pickTarget = best;
            _pickGiveUpAt = now + 4f;
            return ContinuePickup(p2, now, ref o);
        }

        private bool ContinuePickup(RCG.Player p2, float now, ref AiInput o)
        {
            // Em combate, so pega arma se ela estiver colada e ninguem estiver em cima dela.
            if (_pickTarget == null && InCombat && now >= _nextPickScan && Mode != CompanionMode.FicaAqui)
            {
                _nextPickScan = now + 0.5f;
                _pickTarget = FindPickup(p2, null, true, now);
                _pickGiveUpAt = now + 2f;
            }
            if (_pickTarget == null)
            {
                return false;
            }
            if (!_pickTarget.isActiveAndEnabled || now > _pickGiveUpAt || (_pickTarget is PickupObject && (p2.IsCarryingPickupObject() || ((PickupObject)_pickTarget).HasOwner())))
            {
                if (_pickTarget is PickupObject && p2.IsCarryingPickupObject() && p2.PickupObject == _pickTarget)
                {
                    CompanionSpeech.Say("arma", 0.6f);
                    CompanionTelemetry.Event("PegouArma", _pickTarget.name);
                }
                else if (_pickTarget.isActiveAndEnabled && now > _pickGiveUpAt)
                {
                    _pickBlacklist[_pickTarget.GetInstanceID()] = now + 10f;
                    CompanionTelemetry.Event("DesistiuDePegar", _pickTarget.name + " (nao alcancou a tempo)");
                }
                else if (!_pickTarget.isActiveAndEnabled)
                {
                    CompanionTelemetry.Event("PegouItem", _pickTarget.name);
                }
                _pickTarget = null;
                return false;
            }
            // Objetos solidos (lixeira etc.) nao deixam ela chegar no centro: basta o objeto entrar
            // na area de interacao dela. Ai so segura o botao se o jogo for pegar exatamente essa
            // coisa (ou outra arma), nunca porta/loja/NPC.
            InteractableEntityCollider col = p2._InteractableEntityCollider;
            object highest = GetHighestInteract == null || col == null ? null : GetHighestInteract.Invoke(col, null);
            bool inRange = InInteractList(col, _pickTarget);
            bool safe = highest != null && (ReferenceEquals(highest, _pickTarget) || (_pickTarget is Weapon && highest is Weapon && !((Weapon)highest).IsQuestItem));
            if (CompanionPlugin.VerboseLog.Value && now >= _nextPickLog)
            {
                _nextPickLog = now + 1f;
                CompanionPlugin.Log.LogInfo("Pegar " + _pickTarget.name + ": naArea=" + inRange + " maisProximo=" + (highest == null ? "nada" : ((MonoBehaviour)highest).name));
            }
            if (inRange && safe)
            {
                o.Interact = true;
                return true;
            }
            Vector3 tp = _pickTarget.transform.position;
            MoveTo(p2, tp.x, tp.z, 0.05f, 0.05f, ref o);
            return true;
        }

        private MonoBehaviour FindPickup(RCG.Player p2, RCG.Player p1, bool combatOnly, float now)
        {
            Vector3 me = p2.transform.position;
            MonoBehaviour best = null;
            float bestDist = float.MaxValue;

            // Armas so durante a luta (fora de combate ela nao sai carregando lixeira por ai).
            if (CompanionPlugin.UseWeapons.Value && combatOnly && !p2.IsCarryingPickupObject())
            {
                float maxDist = combatOnly ? 2.2f : 4.5f;
                foreach (Weapon w in Object.FindObjectsOfType<Weapon>())
                {
                    if (w is Weapon_EnemyBody || w is Weapon_EnemyBody_Controller || w.IsQuestItem || w.HasOwner() || !w.IsGrounded || !w.CanBePickupedUp() || Blacklisted(w, now))
                    {
                        continue;
                    }
                    float d = Dist(me, w.transform.position);
                    if (d < maxDist && d < bestDist && Mathf.Abs(w.transform.position.y - me.y) < 0.8f)
                    {
                        best = w;
                        bestDist = d;
                    }
                }
            }
            if (combatOnly)
            {
                if (best != null && NearestEnemyDist(me) < 1.5f)
                {
                    return null;
                }
                return best;
            }

            if (CompanionPlugin.PickupFood.Value && HasUseableRoom(p2))
            {
                foreach (InteractEntity drop in FoodDrops())
                {
                    if (Blacklisted(drop, now))
                    {
                        continue;
                    }
                    Vector3 dp = drop.transform.position;
                    float d = Dist(me, dp);
                    // Deixa a comida pra voce se voce estiver mais perto.
                    if (p1 != null && p1.isActiveAndEnabled && Dist(p1.transform.position, dp) < d)
                    {
                        continue;
                    }
                    if (d < 6f && d < bestDist && Mathf.Abs(dp.y - me.y) < 0.8f)
                    {
                        best = drop;
                        bestDist = d;
                        CompanionSpeech.Say("comida", 0.5f);
                    }
                }
            }
            return best;
        }

        private static bool InInteractList(InteractableEntityCollider col, MonoBehaviour target)
        {
            if (col == null || StayInteractables == null)
            {
                return false;
            }
            System.Collections.IList list = StayInteractables.GetValue(col) as System.Collections.IList;
            if (list == null)
            {
                return false;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], target))
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerable<InteractEntity> FoodDrops()
        {
            if (DropV2Item != null)
            {
                foreach (InteractEntity_DropV2 d in Object.FindObjectsOfType<InteractEntity_DropV2>())
                {
                    Data_Item item = DropV2Item.GetValue(d) as Data_Item;
                    if (d.IsActive && d.CanInteract && item != null && item.ItemType == InventoryItemTypes.Useable)
                    {
                        yield return d;
                    }
                }
            }
            if (LightDropItem != null)
            {
                foreach (InteractEntity_LightDrop d in Object.FindObjectsOfType<InteractEntity_LightDrop>())
                {
                    Data_Item item = LightDropItem.GetValue(d) as Data_Item;
                    if (d.IsActive && d.CanInteract && item != null && item.ItemType == InventoryItemTypes.Useable)
                    {
                        yield return d;
                    }
                }
            }
        }

        private static bool HasUseableRoom(RCG.Player p2)
        {
            PlayerInventory inv = PlayerGlobalInventory.instance.PlayerInventories[(int)p2.ClassNameToPlayerCharacter].UseablesInventory;
            if (inv == null)
            {
                return false;
            }
            for (int i = 0; i < inv.Items.Count; i++)
            {
                if (inv.Items[i] == null)
                {
                    return true;
                }
            }
            return false;
        }

        private bool Blacklisted(MonoBehaviour m, float now)
        {
            float until;
            return _pickBlacklist.TryGetValue(m.GetInstanceID(), out until) && now < until;
        }

        // ---------------------------------------------------------------- Alvos

        private CombatEntity PickTarget(RCG.Player p2, RCG.Player p1, bool lowHp)
        {
            float now = Time.time;
            if (_target != null && IsValidEnemy(_target) && now < _retargetAt && _enemies.Contains(_target))
            {
                return _target;
            }
            Vector3 me = p2.transform.position;
            CombatEntity best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                CombatEntity e = _enemies[i];
                Vector3 ep = e.transform.position;
                float score = Mathf.Abs(ep.x - me.x) + Mathf.Abs(ep.z - me.z) * 2f;
                EnemyEnitity ee = e as EnemyEnitity;
                if (ee != null && p1 != null && ee._target == p1)
                {
                    score -= Mode == CompanionMode.Defensiva ? 3f : 1.5f; // ajuda quem esta apanhando
                }
                if (e.IsLying && !e.CanBeGroundhit)
                {
                    score += 3f;
                }
                // Prefere inimigos na borda do grupo a se enfiar no meio da multidao.
                score += 0.8f * EnemiesNear(ep, 1.3f, e);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            // Com pouca vida, ou no modo defensivo, so luta se o inimigo estiver perto.
            float maxScore = lowHp ? 1.5f : (Mode == CompanionMode.Defensiva ? 4f : float.MaxValue);
            if (best != null && bestScore > maxScore)
            {
                best = null;
            }
            if (best != _target)
            {
                _comboStep = 0;
            }
            _target = best;
            _retargetAt = now + 1.0f;
            return best;
        }

        private void CollectEnemies(Vector3 anchor, float myY)
        {
            _enemies.Clear();
            CombatEnitityMainManager mgr = CombatEnitityMainManager.GetInstance(false);
            if (mgr == null)
            {
                return;
            }
            List<CombatEntity> list = mgr.GetListOfCombatEntity(TeamType.Enemy);
            if (list == null)
            {
                return;
            }
            float leash = Leash;
            for (int i = 0; i < list.Count; i++)
            {
                CombatEntity e = list[i];
                if (!IsValidEnemy(e))
                {
                    continue;
                }
                Vector3 ep = e.transform.position;
                if (Mathf.Abs(ep.x - anchor.x) > leash)
                {
                    continue;
                }
                // Inimigo em outra plataforma (no chao, longe da altura dela) nao da pra alcancar.
                float dy = Mathf.Abs(ep.y - myY);
                if ((e.IsGrounded && dy > 1.0f) || dy > 3.5f)
                {
                    continue;
                }
                _enemies.Add(e);
            }
        }

        private static bool IsValidEnemy(CombatEntity e)
        {
            if (e == null || !e.isActiveAndEnabled || e.IsDead || e.Stamina <= 0)
            {
                return false;
            }
            EnemyEnitity ee = e as EnemyEnitity;
            if (ee != null)
            {
                if (ee.IsRecruitAssistEntity())
                {
                    return false;
                }
                if (!CompanionPlugin.AttackBeggingEnemies.Value && (e.Fsm.IsCurrentState<EnemyBegging>() || e.Fsm.IsCurrentState<EnemyBeggingEnd>()))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsBoss(CombatEntity e)
        {
            return e is BossBaseEntity || e is SubBossBaseEntity || e is IBoss;
        }

        private int CountEnemiesNear(Vector3 pos, float radius)
        {
            int n = 0;
            for (int i = 0; i < _enemies.Count; i++)
            {
                Vector3 ep = _enemies[i].transform.position;
                if (Mathf.Abs(ep.x - pos.x) <= radius && Mathf.Abs(ep.z - pos.z) <= 0.8f)
                {
                    n++;
                }
            }
            return n;
        }

        private float NearestEnemyDist(Vector3 pos)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                best = Mathf.Min(best, Dist(pos, _enemies[i].transform.position));
            }
            return best;
        }

        // ---------------------------------------------------------------- Movimento

        private static float Dist(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z) * 2f;
        }

        private static bool ContainsAny(string s, string[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (s.Contains(parts[i]))
                {
                    return true;
                }
            }
            return false;
        }

        // Retorna true se precisou "tocar" o direcional para virar de frente.
        // O registro mostrou dois problemas aqui:
        // 1) um toque de 1 frame no direcional nao vira a personagem (o jogo precisa de alguns frames);
        //    ela ficava tocando pra sempre (Andar/Parada a cada 0,05s, presa 20s em "Virar").
        // 2) com o inimigo quase na mesma posicao X, cada passo troca o lado dele e ela nunca "acerta".
        // Agora: segura o direcional ate virar (max 0,35s), ignora alvos praticamente em cima dela
        // e, se nao conseguir virar, desiste por meio segundo e segue a vida.
        private bool FaceTowards(RCG.Player p2, float x, ref AiInput o)
        {
            float now = Time.time;
            float dir = x - p2.transform.position.x;
            if (Mathf.Abs(dir) < 0.25f || now < _turnFailUntil)
            {
                _turnStart = -1f;
                return false;
            }
            int want = dir > 0f ? 1 : -1;
            if (p2.Facing.FacingSign == want)
            {
                _turnStart = -1f;
                return false;
            }
            if (_turnStart < 0f || now - _turnLastCall > 0.15f)
            {
                _turnStart = now;
            }
            _turnLastCall = now;
            if (now - _turnStart > 0.35f)
            {
                _turnStart = -1f;
                _turnFailUntil = now + 0.5f;
                CompanionTelemetry.Event("VirarFalhou", "nao conseguiu virar em 0.35s (estado=" + (p2.Fsm.GetCurrentState() ?? "").Replace("RCG.", "") + " dx=" + dir.ToString("0.00") + ")");
                return false;
            }
            o.H = want; // segura o direcional por varios frames ate o jogo virar
            return true;
        }

        private bool MoveTo(RCG.Player p2, float tx, float tz, float tolX, float tolZ, ref AiInput o)
        {
            Vector3 me = p2.transform.position;
            float dx = tx - me.x;
            float dz = tz - me.z;
            bool okX = Mathf.Abs(dx) <= tolX;
            bool okZ = Mathf.Abs(dz) <= tolZ;
            if (!okX)
            {
                o.H = dx > 0f ? 1 : -1;
                if (Mathf.Abs(dx) > 3.5f)
                {
                    o.Run = true;
                }
            }
            if (!okZ)
            {
                o.V = (dz > 0f ? 1 : -1) * (int)_zSign;
            }
            return okX && okZ;
        }

        // Descobre sozinho se "cima" no direcional aumenta ou diminui o Z do mundo.
        private void CalibrateZ(RCG.Player p2)
        {
            float z = p2.transform.position.z;
            if (!_zCalibrated && _lastV != 0)
            {
                float dz = z - _lastZ;
                if (Mathf.Abs(dz) > 0.002f)
                {
                    int expected = _lastV * (int)_zSign;
                    _zAgreement += (dz > 0f ? 1 : -1) == expected ? 1 : -1;
                    if (_zAgreement <= -12)
                    {
                        _zSign = -_zSign;
                        _zAgreement = 0;
                        CompanionPlugin.Log.LogInfo("Eixo de profundidade invertido (calibrado).");
                    }
                    else if (_zAgreement >= 20)
                    {
                        _zCalibrated = true;
                    }
                }
            }
            _lastZ = z;
        }

        private void Trace(AiInput o)
        {
            if (_p2 == null || !CompanionPlugin.VerboseLog.Value)
            {
                return;
            }
            float now = Time.time;
            string s = _p2.Fsm.GetCurrentState() ?? string.Empty;
            if (s == _traceLastState)
            {
                return;
            }
            _traceLastState = s;
            string flags = (o.Block ? "B" : "") + (o.Quick ? "Q" : "") + (o.Heavy ? "H" : "") + (o.Dodge ? "D" : "") + (o.Jump ? "J" : "") + (o.Interact ? "I" : "") + (o.Recruit ? "R" : "");
            string tgt = _target != null ? _target.name + "/" + (_target.Fsm.GetCurrentState() ?? "") : "-";
            _trace.Add(now.ToString("0.00") + " " + s.Replace("RCG.", "") + " [" + flags + " h" + o.H + " v" + o.V + "] alvo=" + tgt.Replace("RCG.", ""));
            _traceTimes.Add(now);
            while (_trace.Count > 16)
            {
                _trace.RemoveAt(0);
                _traceTimes.RemoveAt(0);
            }
            int recent = 0;
            for (int i = 0; i < _traceTimes.Count; i++)
            {
                if (now - _traceTimes[i] <= 1f)
                {
                    recent++;
                }
            }
            if (recent > 10 && now >= _nextTraceDump)
            {
                _nextTraceDump = now + 5f;
                CompanionPlugin.Log.LogWarning("Trocas de estado rapidas demais (" + recent + " em 1s):\n  " + string.Join("\n  ", _trace.ToArray()));
            }
        }

        private AiInput Finish(AiInput o)
        {
            Trace(o);
            if (_p2 != null)
            {
                bool down = !CompanionPlugin.IsAlive(_p2);
                if (down && !_p2WasDown)
                {
                    CompanionTelemetry.Event("Nocauteada", "ela caiu (vida 0) | ultima intencao antes=" + _intent);
                }
                _p2WasDown = down;
                CompanionTelemetry.Frame(_intent, null, o, _p2, _target, InCombat);
            }
            bool attacking = o.Quick || o.Heavy || o.Special || o.Block || o.Dodge || o.Interact;
            if (_p2 != null && !attacking && !InCombat && !_airActive && !Nav.Navigating && CompanionPlugin.UsePlatforming.Value && CompanionPlugin.IsAlive(_p2))
            {
                Nav.AntiStuck(_p2, ref o);
            }
            _lastV = (o.Quick || o.Heavy || o.Special || o.Block || o.Dodge) ? 0 : o.V;
            WantsToMove = o.H != 0 || o.V != 0;
            return o;
        }
    }
}
