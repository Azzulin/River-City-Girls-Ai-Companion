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

        // Defesa
        private CombatEntity _lastThreat;
        private float _lastThreatStart = -1f;
        private Defense _defense;
        private float _blockUntil;
        private float _blockStartedAt = -1f;
        private float _blockCooldownUntil;
        private float _dodgeCooldownUntil;

        // Outros
        private float _nextRevivePress;
        private float _nextFacingTap;
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

        public void Reset()
        {
            _target = null;
            _comboStep = 0;
            _lastThreat = null;
            _defense = Defense.None;
            _blockUntil = 0f;
            _blockStartedAt = -1f;
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

            CalibrateZ(p2);

            if (!CompanionPlugin.IsAlive(p2))
            {
                InCombat = false;
                WantsToMove = false;
                return Finish(o);
            }

            Vector3 anchorPos = Mode == CompanionMode.FicaAqui ? _holdAnchor : (p1 != null ? p1.transform.position : p2.transform.position);
            CollectEnemies(anchorPos);
            AttackLearner.Track(_enemies);
            UpdateCombatState(p2, p1, now);

            // 1) Reviver o jogador caido: ficar do lado e bater nele (eh assim que o jogo revive).
            bool p1Down = p1 != null && p1.isActiveAndEnabled && p1.Fsm.IsCurrentState<PlayerDeathLie_Down>();
            if (p1Down && !_p1WasDown)
            {
                CompanionSpeech.Say("parceiraCaiu");
            }
            else if (!p1Down && _p1WasDown && p1 != null && p1.Stamina > 0)
            {
                CompanionSpeech.Say("revivida");
            }
            _p1WasDown = p1Down;
            if (p1Down)
            {
                float side = p2.transform.position.x < p1.transform.position.x ? -1f : 1f;
                if (MoveTo(p2, p1.transform.position.x + side * 0.25f, p1.transform.position.z, 0.12f, 0.1f, ref o))
                {
                    if (!FaceTowards(p2, p1.transform.position.x, ref o) && now >= _nextRevivePress)
                    {
                        o.Quick = true;
                        _nextRevivePress = now + 0.22f;
                    }
                }
                return Finish(o);
            }

            // 2) Segurando um inimigo agarrado: tenta recrutar se ela nao tem ajudante.
            if (HandleGrab(p2, ref o))
            {
                return Finish(o);
            }

            // 3) Defesa: parry (se ja aprendeu o tempo do golpe), defesa normal ou esquiva.
            if (HandleDefense(p2, now, ref o))
            {
                return Finish(o);
            }

            // 4) Pegar arma/comida que ja estava indo buscar.
            if (ContinuePickup(p2, now, ref o))
            {
                return Finish(o);
            }

            // 5) Escolher alvo e lutar.
            bool lowHp = p2.StaminaPercent < 0.2f;
            CombatEntity target = PickTarget(p2, p1, lowHp);
            if (target != null)
            {
                TryRecruit(p2, target, now, ref o);
                if (!o.Recruit)
                {
                    Fight(p2, p1, target, ref o);
                }
                return Finish(o);
            }
            _comboStep = 0;

            // 6) Sem luta: procura armas/comida por perto.
            if (StartPickup(p2, p1, now, ref o))
            {
                return Finish(o);
            }

            // 7) Seguir o jogador (ou ficar no lugar).
            if (Mode == CompanionMode.FicaAqui)
            {
                MoveTo(p2, _holdAnchor.x, _holdAnchor.z, 0.6f, 0.35f, ref o);
            }
            else if (p1 != null && p1.isActiveAndEnabled)
            {
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

        // ---------------------------------------------------------------- Defesa

        private bool HandleDefense(RCG.Player p2, float now, ref AiInput o)
        {
            CombatEntity threat = FindThreat(p2);
            if (threat == null)
            {
                _lastThreat = null;
                _defense = Defense.None;
            }
            else
            {
                float start;
                bool attacking = AttackLearner.TryGetAttackStart(threat, out start);
                float threatStart = attacking ? start : -1f;
                if (threat != _lastThreat || !Mathf.Approximately(threatStart, _lastThreatStart))
                {
                    _lastThreat = threat;
                    _lastThreatStart = threatStart;
                    _defense = ChooseDefense(threat, attacking, now);
                }

                if (_defense == Defense.Dodge && now >= _dodgeCooldownUntil && CanAct(p2))
                {
                    float dz = p2.transform.position.z - threat.transform.position.z;
                    int dir = Mathf.Abs(dz) < 0.05f ? (Random.value < 0.5f ? 1 : -1) : (dz > 0f ? 1 : -1);
                    o.Dodge = true;
                    o.V = dir * (int)_zSign;
                    _dodgeCooldownUntil = now + 1.2f;
                    _defense = Defense.None;
                    CompanionSpeech.Say("esquiva", 0.3f);
                    return true;
                }

                if (_defense == Defense.Parry && attacking)
                {
                    float delay;
                    AttackLearner.TryGetDelay(threat, out delay);
                    float hitAt = start + delay;
                    FaceTowards(p2, threat.transform.position.x, ref o);
                    if (now >= hitAt - 0.07f && now <= hitAt + 0.3f)
                    {
                        o.H = 0;
                        o.Block = true;
                        return true;
                    }
                    if (now < hitAt - 0.07f)
                    {
                        return true; // espera o momento certo sem se comprometer com um ataque
                    }
                }

                if (_defense == Defense.Block && now >= _blockCooldownUntil)
                {
                    if (_blockStartedAt < 0f)
                    {
                        _blockStartedAt = now;
                    }
                    _blockUntil = now + 0.25f;
                }
            }

            if (now < _blockUntil)
            {
                if (_blockStartedAt >= 0f && now - _blockStartedAt > 0.9f)
                {
                    _blockUntil = 0f;
                    _blockStartedAt = -1f;
                    _blockCooldownUntil = now + 1.4f;
                    _defense = Defense.None;
                    return false;
                }
                o.Block = true;
                return true;
            }
            _blockStartedAt = -1f;
            return false;
        }

        private Defense ChooseDefense(CombatEntity threat, bool attacking, float now)
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
                return learned && attacking && CompanionPlugin.UseParry.Value ? Defense.Parry : Defense.Block;
            }
            if (r < block + 0.15f && now >= _dodgeCooldownUntil)
            {
                return Defense.Dodge;
            }
            return Defense.None;
        }

        private CombatEntity FindThreat(RCG.Player p2)
        {
            Vector3 me = p2.transform.position;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyEnitity ee = _enemies[i] as EnemyEnitity;
                if (ee == null || ee._target != p2)
                {
                    continue;
                }
                Vector3 ep = ee.transform.position;
                float reach = IsBoss(ee) ? 3.5f : 2.2f;
                if (Mathf.Abs(ep.x - me.x) > reach || Mathf.Abs(ep.z - me.z) > 0.7f)
                {
                    continue;
                }
                float start;
                if (AttackLearner.TryGetAttackStart(ee, out start) || ee.IsAttackingTarget || ee.AI_IsAboutToOrIsAttackingSomeone())
                {
                    return ee;
                }
            }
            return null;
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
                    return;
                }
            }

            // Inimigo caido: o golpe no chao do jogo (EnemyLieCondition) so sai com ela praticamente
            // em cima dele (|dx| <= 0.4, |dz| <= 0.2) e ele sendo o inimigo mais proximo dela.
            if (target.IsLying && target.CanBeGroundhit && !p2.IsCarryingPickupObject())
            {
                if (MoveTo(p2, t.x, t.z, 0.25f, 0.12f, ref o))
                {
                    o.H = 0;
                    o.V = 0;
                    o.Run = false;
                    if (now >= _nextPressAt)
                    {
                        o.Quick = true;
                        _nextPressAt = now + Random.Range(0.18f, 0.26f);
                    }
                }
                _comboStep = 0;
                return;
            }

            float rangeMul = p2.IsCarryingPickupObject() ? 1.35f : 1f;
            float range = _range * rangeMul;
            float side = me.x <= t.x ? -1f : 1f;
            float standX = t.x + side * range * 0.85f;
            float zTol = Mathf.Clamp(p2.ZDiffHitTol / 100f, 0.12f, 0.5f) * 0.7f;

            bool inPlace = MoveTo(p2, standX, t.z, 0.22f, zTol, ref o);
            float dx = Mathf.Abs(t.x - me.x);
            bool closeEnough = dx <= range * 1.15f && Mathf.Abs(t.z - me.z) <= zTol * 1.4f;
            if (!inPlace && !closeEnough)
            {
                return;
            }
            o.H = 0;
            o.Run = false;

            if (FaceTowards(p2, t.x, ref o))
            {
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
                    return;
                }
            }

            if (now < _nextPressAt || now < _pauseUntil)
            {
                return;
            }

            if (CompanionPlugin.UseSpecials.Value && _comboStep == 0 && p2.SpecialPercent >= 0.5f && (CountEnemiesNear(me, 2.5f) >= 2 || (boss && vulnerable)) && Random.value < 0.35f + aggr * 0.2f)
            {
                o.Special = true;
                _nextPressAt = now + 0.7f;
                RegisterSwing();
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

        private void RegisterSwing()
        {
            _missStreak++;
            if (_missStreak >= 8)
            {
                _missStreak = 0;
                _range = Mathf.Max(MinRange, _range * 0.85f);
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
                }
                else if (_pickTarget.isActiveAndEnabled && now > _pickGiveUpAt)
                {
                    _pickBlacklist[_pickTarget.GetInstanceID()] = now + 10f;
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

            if (CompanionPlugin.UseWeapons.Value && !p2.IsCarryingPickupObject())
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

        private void CollectEnemies(Vector3 anchor)
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
                if (Mathf.Abs(ep.x - anchor.x) > leash || Mathf.Abs(ep.y - anchor.y) > 3f)
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
        private bool FaceTowards(RCG.Player p2, float x, ref AiInput o)
        {
            float dir = x - p2.transform.position.x;
            if (Mathf.Abs(dir) < 0.05f)
            {
                return false;
            }
            int want = dir > 0f ? 1 : -1;
            if (p2.Facing.FacingSign == want)
            {
                return false;
            }
            if (Time.time >= _nextFacingTap)
            {
                o.H = want;
                _nextFacingTap = Time.time + 0.05f;
            }
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

        private AiInput Finish(AiInput o)
        {
            _lastV = (o.Quick || o.Heavy || o.Special || o.Block || o.Dodge) ? 0 : o.V;
            WantsToMove = o.H != 0 || o.V != 0;
            return o;
        }
    }
}
