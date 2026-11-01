using ARPG.Core.Simulation;

namespace ARPG.Runtime
{
    /// <summary>
    /// 관찰 모드 배속 실행기(구현계획 4.2).
    /// accumulator += unscaledDeltaTime × speed, 1/30초씩 Step. Time.timeScale 은 건드리지 않는다(이중 적용 방지).
    /// tick 을 건너뛰어 속도를 맞추지 않고, 처리 한도를 넘으면 누적을 제한하고 '처리 지연'을 표시한다.
    /// </summary>
    public sealed class VisualSimulationRunner
    {
        public BattleSimulation Sim { get; private set; }
        public double Speed = 1.0;
        public bool Paused = true;
        public int MaxStepsPerFrame = 600;
        public bool Lagging { get; private set; }
        public int StepsLastFrame { get; private set; }
        private double _acc;
        private double _tickSec;

        public void Begin(BattleSimulation sim)
        {
            Sim = sim;
            _acc = 0;
            _tickSec = sim != null ? sim.Rules.TickSec : 1.0 / 30.0;
            Lagging = false;
        }

        /// <summary>최근 두 상태 사이 보간 비율(화면 전용).</summary>
        public float Alpha
        {
            get
            {
                if (Sim == null || Sim.IsFinished || Paused) return 1f;
                double a = _acc / _tickSec;
                return a < 0 ? 0f : (a > 1 ? 1f : (float)a);
            }
        }

        public void Update(double unscaledDeltaTime)
        {
            StepsLastFrame = 0;
            if (Sim == null || Sim.IsFinished || Paused) return;
            _acc += unscaledDeltaTime * Speed;
            int steps = 0;
            while (_acc >= _tickSec && steps < MaxStepsPerFrame && !Sim.IsFinished)
            {
                Sim.Step();
                _acc -= _tickSec;
                steps++;
            }
            StepsLastFrame = steps;
            // 처리 한도 초과: 누적 시간을 일부 버려 실제 재생 배속을 낮춘다(전투 tick 은 건너뛰지 않음)
            double cap = _tickSec * 4;
            Lagging = _acc > cap;
            if (_acc > cap) _acc = cap;
            if (Sim.IsFinished) _acc = 0;
        }

        public void StepOnce()
        {
            if (Sim == null || Sim.IsFinished) return;
            Paused = true;
            _acc = 0;
            Sim.Step();
        }
    }
}
