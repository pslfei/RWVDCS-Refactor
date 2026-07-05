using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class DMS
    {
        // 边沿检测与脉冲计时（partial class 私有状态，跨周期保持）
        private bool _oldCmdCxm = false; // 上一周期手动指令值（PACK Bit6）
        private bool _pulseActive = false; // 脉冲是否进行中（PACK Bit8）
        private float _pulseTimer = 0f;    // 脉冲计时器

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 提取当前周期输入，避免重复装箱 / 拆箱
            bool currentOut = OUT;
            bool curTS = TS;
            bool curTR = TR;
            bool curCXM = CXM; // 手动指令引脚：HMI 投入=1 / 切除=0 写 @CXM
            float cycle = (float)cmd.Dpu.Cycle;

            uint pack = 0;

            // ================== 核心控制逻辑（优先级：跟踪 TS > 手动指令 CXM） ==================
            if (curTS)
            {
                // 跟踪模式：无条件输出 TR，复位脉冲状态，拦截任何 HMI 操作
                currentOut = curTR;
                OUT.Quality = TR.Quality;

                _pulseActive = false;
                _pulseTimer = 0f;
            }
            else
            {
                OUT.Quality = QualityTypes.Good;

                // 手动指令上升沿（@CXM 由 0 变 1）
                bool risingEdge = !_oldCmdCxm && curCXM;

                if (Mode == 0) // pulse: 检测到上升沿输出宽度为 PulT 的正脉冲
                {
                    // 脉冲进行中屏蔽新的上升沿
                    if (risingEdge && !_pulseActive)
                    {
                        currentOut = true;
                        _pulseActive = true;
                        _pulseTimer = 0f;
                    }
                    if (_pulseActive)
                    {
                        _pulseTimer += cycle;
                        if (_pulseTimer >= PulT)
                        {
                            currentOut = false;
                            _pulseActive = false;
                            _pulseTimer = 0f;
                        }
                    }
                }
                else if (Mode == 1) // toggle: 每次上升沿翻转输出
                {
                    if (risingEdge) currentOut = !currentOut;
                }
                else if (Mode == 2) // normal: 输出跟随手动指令
                {
                    currentOut = curCXM;
                }
            }

            // ================== PACK 状态字组装（DMS.md 位定义表） ==================
            if (curTS) pack |= (1u << 0);        // Bit0 跟踪开关 TS
            if (curTR) pack |= (1u << 1);        // Bit1 跟踪值 TR
            if (currentOut) pack |= (1u << 2);   // Bit2 输出 OUT
            // Bit3~Bit5 备用
            if (_oldCmdCxm) pack |= (1u << 6);   // Bit6 上一周期手动指令值
            if (curCXM) pack |= (1u << 7);       // Bit7 本周期手动指令值
            if (_pulseActive) pack |= (1u << 8); // Bit8 PULSE 脉冲模式正在进行

            // ================== 刷新历史 & 输出 ==================
            _oldCmdCxm = curCXM;
            OUT[0] = currentOut;

            // 状态打包点 → TAG.Value（框架同步到 DPU.<块>.Value，供 HMI 颜色/灰显绑定）
            TAG.Value = pack;
        }
    }
}
