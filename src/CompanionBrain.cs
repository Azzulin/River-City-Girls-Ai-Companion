using System.Collections.Generic;
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
        public bool Jump;
        public bool Run;
        public bool Dodge;
    }

    // Decide, a cada frame, quais "botoes" a parceira aperta.
    internal class CompanionBrain
    {
        private const float MinRange = 0.45f;

        public bool InCombat;
        public bool WantsToMove;

        private float _zSign = 1f;
        private int _zAgreement;
        private bool _zCalibrated;
        private int _lastV;
        private float _lastZ;

        private CombatEntity _target;
        private float _retargetAt;

        private int _comboStep;
        private int _quickHits = 3;
        private float _nextPressAt;
        private float _pauseUntil;
        private float _range = -1f;
        private int _missStreak;

        private CombatEntity _lastThreat;
        private bool _willBlock;
        private float _blockUntil;
        private float _blockStartedAt = -1f;
        private float _blockCooldownUntil;

        private float _nextRevivePress;
        private float _nextFacingTap;

        private readonly List<CombatEntity> _enemies = new List<CombatEntity>();

        public void Reset()
        {
            _target = null;
            _comboStep = 0;
            _lastThreat = null;
            _blockUntil = 0f;
            _blockStartedAt = -1f;
            InCombat = false;
            WantsToMove = false;
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

            // 1) Reviver o jogador caido: ficar do lado e bater nele (eh assim que o jogo revive).
            if (p1 != null && p1.isActiveAndEnabled && p1.Fsm.IsCurrentState<PlayerDeathLie_Down>())
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
                InCombat = true;
                return Finish(o);
            }

            CollectEnemies(p1 != null ? p1 : p2);
            InCombat = _enemies.Count > 0;

            // 2) Defesa: se um inimigo perto esta atacando ela, defende (com cooldown pra nao travar no bloqueio).
            CombatEntity threat = FindThreat(p2);
            if (threat != _lastThreat)
            {
                _lastThreat = threat;
                _willBlock = threat != null && Random.value < CompanionPlugin.BlockChance.Value;
            }
            if (threat != null && _willBlock && now >= _blockCooldownUntil)
            {
                if (_blockStartedAt < 0f)
                {
                    _blockStartedAt = now;
                }
                _blockUntil = now + 0.25f;
            }
            if (now < _blockUntil)
            {
                if (_blockStartedAt >= 0f && now - _blockStartedAt > 0.9f)
                {
                    _blockUntil = 0f;
                    _blockStartedAt = -1f;
                    _blockCooldownUntil = now + 1.4f;
                    _willBlock = false;
                }
                else
                {
                    o.Block = true;
                    return Finish(o);
                }
            }
            else
            {
                _blockStartedAt = -1f;
            }

            // 3) Escolher alvo e lutar.
            bool lowHp = p2.StaminaPercent < 0.2f;
            CombatEntity target = PickTarget(p2, p1, lowHp);
            if (target != null)
            {
                Fight(p2, p1, target, ref o);
                return Finish(o);
            }

            // 4) Sem inimigos: seguir o jogador.
            _comboStep = 0;
            if (p1 != null && p1.isActiveAndEnabled)
            {
                Vector3 pp = p1.transform.position;
                float behind = -p1.Facing.FacingSign * 1.3f;
                MoveTo(p2, pp.x + behind, pp.z + 0.35f, 0.6f, 0.35f, ref o);
            }
            return Finish(o);
        }

        private void Fight(RCG.Player p2, RCG.Player p1, CombatEntity target, ref AiInput o)
        {
            float now = Time.time;
            Vector3 me = p2.transform.position;
            Vector3 t = target.transform.position;
            float side = me.x <= t.x ? -1f : 1f;
            float standX = t.x + side * _range * 0.85f;
            float zTol = Mathf.Clamp(p2.ZDiffHitTol / 100f, 0.12f, 0.5f) * 0.7f;

            bool inPlace = MoveTo(p2, standX, t.z, 0.22f, zTol, ref o);
            float dx = Mathf.Abs(t.x - me.x);
            bool closeEnough = dx <= _range * 1.15f && Mathf.Abs(t.z - me.z) <= zTol * 1.4f;
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
                float facing = p2.Facing.FacingSign;
                float ahead = (pp.x - me.x) * facing;
                if (ahead > 0f && ahead < _range * 1.3f && Mathf.Abs(pp.z - me.z) < zTol * 1.5f)
                {
                    o.V = (pp.z > me.z ? -1 : 1) * (int)_zSign;
                    return;
                }
            }

            if (now < _nextPressAt || now < _pauseUntil)
            {
                return;
            }

            float aggr = CompanionPlugin.Aggressiveness.Value;
            if (CompanionPlugin.UseSpecials.Value && _comboStep == 0 && p2.SpecialPercent >= 0.5f && CountEnemiesNear(me, 2.5f) >= 2 && Random.value < 0.35f)
            {
                o.Special = true;
                _nextPressAt = now + 0.7f;
                RegisterSwing();
                return;
            }

            if (_comboStep < _quickHits)
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

        private CombatEntity PickTarget(RCG.Player p2, RCG.Player p1, bool lowHp)
        {
            float now = Time.time;
            if (_target != null && IsValidEnemy(_target) && now < _retargetAt)
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
                    score -= 1.5f; // ajuda quem esta apanhando
                }
                if (e.IsLying)
                {
                    score += 3f;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = e;
                }
            }
            // Com pouca vida e sem comida, so luta se o inimigo estiver colado.
            if (lowHp && best != null && bestScore > 1.5f)
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

        private void CollectEnemies(RCG.Player anchor)
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
            float leash = CompanionPlugin.LeashDistance.Value;
            Vector3 a = anchor.transform.position;
            for (int i = 0; i < list.Count; i++)
            {
                CombatEntity e = list[i];
                if (!IsValidEnemy(e))
                {
                    continue;
                }
                Vector3 ep = e.transform.position;
                if (Mathf.Abs(ep.x - a.x) > leash || Mathf.Abs(ep.y - a.y) > 3f)
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
                if (Mathf.Abs(ep.x - me.x) > 2.2f || Mathf.Abs(ep.z - me.z) > 0.6f)
                {
                    continue;
                }
                if (ee.IsAttackingTarget || ee.AI_IsAboutToOrIsAttackingSomeone())
                {
                    return ee;
                }
            }
            return null;
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
            _lastV = (o.Quick || o.Heavy || o.Special || o.Block) ? 0 : o.V;
            WantsToMove = o.H != 0 || o.V != 0;
            return o;
        }
    }
}
