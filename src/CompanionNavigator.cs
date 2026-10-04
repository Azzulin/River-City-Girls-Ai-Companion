using System.Collections.Generic;
using RCG;
using UnityEngine;

namespace RCGCompanion
{
    // Plataforma por imitacao: grava o caminho do jogador (onde andou, pulou, pulou na parede,
    // subiu/desceu escada) e, quando ela nao alcanca o jogador andando, refaz esse caminho.
    internal class CompanionNavigator
    {
        private enum CrumbType
        {
            Ground,
            Jump,
            Climb,
            ClimbDown
        }

        private class Crumb
        {
            public CrumbType Type;
            public Vector3 Pos;
            public float Time;
            public int H;
            public int V;
            public bool Running;
            public float WallJumpDelay = -1f;
            public int WallJumpH;
            public float LandY = float.NaN;
            public Vector3 LandPos;
        }

        private const int MaxCrumbs = 80;
        private const float HeightGap = 0.6f;

        private readonly List<Crumb> _crumbs = new List<Crumb>();
        private RCG.Player _recordedPlayer;
        private string _p1LastState = string.Empty;
        private bool _p1WasGrounded = true;
        private Crumb _p1CurrentJump;
        private float _p1JumpTime;

        // Execucao
        private int _index = -1;
        private bool _jumped;
        private float _jumpStartedAt;
        private bool _wallJumped;
        private bool _climbing;
        private float _stepStartedAt;
        private bool _runUpDone;
        private int _fails;

        // Anti-travamento (pulinho por cima de obstaculos)
        private Vector3 _lastPos;
        private float _noProgressSince = -1f;
        private float _nextHop;

        public bool Navigating
        {
            get { return _index >= 0; }
        }

        public int Fails
        {
            get { return _fails; }
        }

        public void Reset()
        {
            _index = -1;
            _jumped = false;
            _climbing = false;
            _fails = 0;
        }

        // ------------------------------------------------------------ Gravacao do caminho do jogador

        public void Record(RCG.Player p1)
        {
            if (p1 == null || !p1.isActiveAndEnabled)
            {
                return;
            }
            if (p1 != _recordedPlayer)
            {
                _recordedPlayer = p1;
                _crumbs.Clear();
                Reset();
                _p1CurrentJump = null;
            }
            float now = Time.time;
            string s = p1.Fsm.GetCurrentState() ?? string.Empty;
            Vector3 pos = p1.transform.position;
            bool grounded = p1.IsGrounded;

            if (s != _p1LastState)
            {
                if (s.EndsWith(".PlayerJump"))
                {
                    Crumb c = new Crumb();
                    c.Type = CrumbType.Jump;
                    c.Pos = pos;
                    c.Time = now;
                    c.H = p1.CombatInput.HorizontalDir;
                    c.V = p1.CombatInput.VerticalDir;
                    c.Running = _p1LastState.EndsWith(".PlayerRun") || _p1LastState.EndsWith("CarryRun");
                    Add(c);
                    _p1CurrentJump = c;
                    _p1JumpTime = now;
                }
                else if (s.EndsWith(".PlayerWallJumpIntro") && _p1CurrentJump != null && _p1CurrentJump.WallJumpDelay < 0f)
                {
                    _p1CurrentJump.WallJumpDelay = now - _p1JumpTime;
                    _p1CurrentJump.WallJumpH = -_p1CurrentJump.H;
                }
                else if (s.EndsWith(".PlayerClimb"))
                {
                    Crumb c = new Crumb();
                    c.Type = CrumbType.Climb;
                    c.Pos = pos;
                    c.Time = now;
                    Add(c);
                    _p1CurrentJump = null;
                }
                else if (s.EndsWith(".PlayerClimbTopDown"))
                {
                    Crumb c = new Crumb();
                    c.Type = CrumbType.ClimbDown;
                    c.Pos = pos;
                    c.Time = now;
                    Add(c);
                }
                _p1LastState = s;
            }

            if (grounded && !_p1WasGrounded)
            {
                if (_p1CurrentJump != null)
                {
                    _p1CurrentJump.LandY = pos.y;
                    _p1CurrentJump.LandPos = pos;
                    _p1CurrentJump = null;
                }
                AddGround(pos, now, true);
            }
            else if (grounded)
            {
                AddGround(pos, now, false);
            }
            _p1WasGrounded = grounded;

            // Esquece caminhos muito antigos.
            while (_crumbs.Count > 0 && now - _crumbs[0].Time > 90f)
            {
                _crumbs.RemoveAt(0);
                if (_index >= 0)
                {
                    _index--;
                }
            }
        }

        private void AddGround(Vector3 pos, float now, bool force)
        {
            Crumb last = _crumbs.Count > 0 ? _crumbs[_crumbs.Count - 1] : null;
            if (!force && last != null && last.Type == CrumbType.Ground && Mathf.Abs(last.Pos.x - pos.x) + Mathf.Abs(last.Pos.z - pos.z) < 1.2f && Mathf.Abs(last.Pos.y - pos.y) < 0.3f)
            {
                return;
            }
            Crumb c = new Crumb();
            c.Type = CrumbType.Ground;
            c.Pos = pos;
            c.Time = now;
            Add(c);
        }

        private void Add(Crumb c)
        {
            _crumbs.Add(c);
            if (_crumbs.Count > MaxCrumbs)
            {
                _crumbs.RemoveAt(0);
                if (_index >= 0)
                {
                    _index--;
                }
            }
        }

        // ------------------------------------------------------------ Execucao

        // Precisa navegar quando o jogador esta numa altura diferente (plataforma, escada etc.).
        public bool NeedsPath(RCG.Player p1, RCG.Player p2)
        {
            if (p1 == null || !p1.isActiveAndEnabled || !p1.IsGrounded)
            {
                return Navigating;
            }
            return Mathf.Abs(p1.transform.position.y - p2.transform.position.y) > HeightGap || Navigating;
        }

        public bool Drive(RCG.Player p1, RCG.Player p2, ref AiInput o, int zSign)
        {
            float now = Time.time;
            Vector3 me = p2.transform.position;

            if (_index < 0)
            {
                _index = FindStart(me);
                if (_index < 0)
                {
                    return false; // sem caminho gravado: deixa o teleporte resolver
                }
                BeginStep(now);
            }

            // Chegou na altura do jogador: fim da navegacao.
            if (p2.IsGrounded && Mathf.Abs(p1.transform.position.y - me.y) <= HeightGap && !_jumped && !_climbing)
            {
                Reset();
                return false;
            }

            if (_index >= _crumbs.Count)
            {
                Reset();
                return false;
            }

            Crumb c = _crumbs[_index];
            if (now - _stepStartedAt > 7f)
            {
                Fail(me);
                return true;
            }

            switch (c.Type)
            {
                case CrumbType.Ground:
                    if (MoveTo(me, c.Pos, 0.35f, 0.25f, ref o, zSign) && p2.IsGrounded)
                    {
                        Next(now);
                    }
                    return true;
                case CrumbType.Climb:
                    return DriveClimb(p2, c, ref o, zSign, now, 1);
                case CrumbType.ClimbDown:
                    return DriveClimb(p2, c, ref o, zSign, now, -1);
                default:
                    return DriveJump(p2, c, ref o, zSign, now);
            }
        }

        private bool DriveJump(RCG.Player p2, Crumb c, ref AiInput o, int zSign, float now)
        {
            Vector3 me = p2.transform.position;
            int dir = c.H != 0 ? c.H : (c.LandPos.x > c.Pos.x ? 1 : -1);

            if (!_jumped)
            {
                if (!p2.IsGrounded)
                {
                    return true;
                }
                if (c.Running && !_runUpDone)
                {
                    // Volta um pouco para pegar embalo, como o jogador fez.
                    Vector3 runUp = c.Pos - new Vector3(dir * 2.2f, 0f, 0f);
                    if (MoveTo(me, runUp, 0.3f, 0.2f, ref o, zSign))
                    {
                        _runUpDone = true;
                    }
                    return true;
                }
                float dx = c.Pos.x - me.x;
                float dz = c.Pos.z - me.z;
                bool alignedZ = Mathf.Abs(dz) <= 0.2f;
                if (c.Running)
                {
                    o.H = dir;
                    o.Run = true;
                    if (!alignedZ)
                    {
                        o.V = (dz > 0f ? 1 : -1) * zSign;
                    }
                    if (alignedZ && dx * dir <= 0.2f)
                    {
                        PressJump(c, ref o, now);
                    }
                    return true;
                }
                if (MoveTo(me, c.Pos, 0.2f, 0.18f, ref o, zSign))
                {
                    PressJump(c, ref o, now);
                }
                return true;
            }

            // No ar: segura a mesma direcao que o jogador segurou.
            float t = now - _jumpStartedAt;
            int airH = _wallJumped ? c.WallJumpH : c.H;
            if (!c.Running && !float.IsNaN(c.LandY))
            {
                float ldx = c.LandPos.x - me.x;
                airH = Mathf.Abs(ldx) > 0.15f ? (ldx > 0f ? 1 : -1) : 0;
                if (_wallJumped)
                {
                    airH = c.WallJumpH;
                }
            }
            o.H = airH;
            o.V = c.V;
            if (c.WallJumpDelay > 0f && !_wallJumped && t >= c.WallJumpDelay)
            {
                o.Jump = true;
                _wallJumped = true;
            }
            if (t > 0.25f && p2.IsGrounded)
            {
                bool landedHigh = !float.IsNaN(c.LandY) && Mathf.Abs(me.y - c.LandY) <= 0.5f;
                if (landedHigh || float.IsNaN(c.LandY))
                {
                    Next(now);
                }
                else
                {
                    Fail(me);
                }
            }
            return true;
        }

        private void PressJump(Crumb c, ref AiInput o, float now)
        {
            o.Jump = true;
            o.H = c.H;
            o.V = c.V;
            _jumped = true;
            _jumpStartedAt = now;
        }

        private bool DriveClimb(RCG.Player p2, Crumb c, ref AiInput o, int zSign, float now, int dir)
        {
            Vector3 me = p2.transform.position;
            string s = p2.Fsm.GetCurrentState() ?? string.Empty;
            bool inClimb = s.Contains("Climb");
            if (!_climbing && !inClimb)
            {
                if (MoveTo(me, c.Pos, 0.25f, 0.2f, ref o, zSign))
                {
                    o.V = dir; // escada usa o direcional "cru": cima sobe, baixo desce
                    if (dir > 0 && now - _stepStartedAt > 1.5f)
                    {
                        o.Jump = true; // algumas escadas comecam no alto: pula e segura cima
                    }
                }
                return true;
            }
            _climbing = true;
            o.V = dir;
            if (!inClimb && p2.IsGrounded)
            {
                Next(now);
            }
            return true;
        }

        private void Next(float now)
        {
            _index++;
            BeginStep(now);
        }

        private void BeginStep(float now)
        {
            _jumped = false;
            _wallJumped = false;
            _climbing = false;
            _runUpDone = false;
            _stepStartedAt = now;
        }

        private void Fail(Vector3 me)
        {
            _fails++;
            if (CompanionPlugin.VerboseLog.Value)
            {
                CompanionPlugin.Log.LogInfo("Plataforma: tentativa falhou (" + _fails + "), recomecando do ponto mais proximo.");
            }
            _index = FindStart(me);
            BeginStep(Time.time);
        }

        // Escolhe o ponto do caminho mais proximo dela (de preferencia na mesma altura).
        private int FindStart(Vector3 me)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _crumbs.Count; i++)
            {
                Vector3 p = _crumbs[i].Pos;
                float dy = Mathf.Abs(p.y - me.y);
                float score = Mathf.Abs(p.x - me.x) + Mathf.Abs(p.z - me.z) * 2f + dy * 4f - i * 0.02f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private static bool MoveTo(Vector3 me, Vector3 target, float tolX, float tolZ, ref AiInput o, int zSign)
        {
            float dx = target.x - me.x;
            float dz = target.z - me.z;
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
                o.V = (dz > 0f ? 1 : -1) * zSign;
            }
            return okX && okZ;
        }

        // ------------------------------------------------------------ Pulinho quando trava num obstaculo

        public void AntiStuck(RCG.Player p2, ref AiInput o)
        {
            float now = Time.time;
            Vector3 me = p2.transform.position;
            bool moving = o.H != 0;
            if (!moving || !p2.IsGrounded || o.Jump)
            {
                _noProgressSince = -1f;
                _lastPos = me;
                return;
            }
            if (Mathf.Abs(me.x - _lastPos.x) > 0.04f)
            {
                _noProgressSince = -1f;
                _lastPos = me;
                return;
            }
            if (_noProgressSince < 0f)
            {
                _noProgressSince = now;
            }
            if (now - _noProgressSince > 0.6f && now >= _nextHop)
            {
                o.Jump = true; // segura a mesma direcao e pula por cima
                _nextHop = now + 1.5f;
                _noProgressSince = -1f;
                if (CompanionPlugin.VerboseLog.Value)
                {
                    CompanionPlugin.Log.LogInfo("Travada num obstaculo: pulando.");
                }
            }
        }
    }
}
