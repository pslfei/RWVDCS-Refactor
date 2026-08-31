using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class TIMER
    {
        protected override void Run(ICommand cmd)
        {
            // 1. 模块使能逻辑
            _ENO[0] = Enable;
            if (!Enable)
            {
                return;
            }

            // 扫描周期(秒)，与 FC_DELAY / FC_ETIMER 等保持一致，取系统真实运算周期
            float dt = cmd.Dpu.Cycle;
            float time = Math.Max(0.0f, TIME);

            void ResetTimer()
            {
                OUT[0] = false;
                _timing = false;
                TRun[0] = 0.0f;
            }

            // 2. 边沿检测逻辑
            bool xRise = (X & !_prevX);       // X 上升沿
            bool xFall = (!X & _prevX);       // X 下降沿

            // MODE 在线变化时，上一模式的 OUT/TRun/_timing 不能泄漏到新模式。
            // 首次运行不在这里清零，由各模式按自己的空闲态规则规范化工程初值/快照值。
            byte modeToken = MODE < byte.MaxValue ? (byte)MODE : (byte)(byte.MaxValue - 1);
            bool firstExecution = _lastMode == byte.MaxValue;
            if (!firstExecution && _lastMode != modeToken)
                ResetTimer();
            _lastMode = modeToken;

            // 3. 根据工作模式(MODE)选择
            switch (MODE)
            {
                case 0:
                    // ---------------------------------------------------------
                    // 模式 0: TD-PULSE 延时脉冲模块（不可重触发）
                    // 行为: X 上升沿后延时 TIME，再输出一个扫描周期的脉冲；延时期间忽略新的上升沿。
                    // ---------------------------------------------------------
                    if (RST)
                    {
                        ResetTimer();
                    }
                    else
                    {
                        bool startedThisCycle = false;
                        bool pulseThisCycle = false;

                        if (xRise && !_timing)
                        {
                            _timing = true;
                            TRun[0] = 0.0f;
                            startedThisCycle = true;
                        }

                        if (_timing)
                        {
                            if (!startedThisCycle)
                                TRun[0] = TRun + dt;

                            if (TRun >= time)
                            {
                                pulseThisCycle = true;
                                _timing = false;
                            }
                        }

                        // 局部变量天然保证脉冲只保持当前一个运算周期，并清除错误的 OUT 默认高值。
                        OUT[0] = pulseThisCycle;
                    }
                    break;
                case 1:
                    // ---------------------------------------------------------
                    // 模式 1: PULSE 脉冲输出模块（可重触发）
                    // 行为: X 上升沿后输出立即拉高，保持 TIME 后回落；期间 X 再次上升沿，TIME 重新计时(可重触发)。
                    // ---------------------------------------------------------
                    if (RST)
                    {
                        ResetTimer();
                    }
                    else
                    {
                        bool startedThisCycle = false;
                        if (xRise)
                        {
                            OUT[0] = true;
                            _timing = true;
                            TRun[0] = 0.0f;
                            startedThisCycle = true;
                        }

                        if (_timing)
                        {
                            OUT[0] = true;

                            // 触发周期本身不扣减脉宽，确保 TIME 小于一个扫描周期时仍有一个可见脉冲。
                            if (!startedThisCycle)
                                TRun[0] = TRun + dt;

                            if (!startedThisCycle && TRun >= time)
                            {
                                OUT[0] = false;
                                _timing = false;
                            }
                        }
                        else
                        {
                            // PULSE 的空闲态必须为低，不能继承工程默认值或其他模式的高输出。
                            OUT[0] = false;
                        }
                    }
                    break;

                case 2:
                    // ---------------------------------------------------------
                    // 模式 2: TD-ON 延时接通模块 (上电延时接通 TON)
                    // 行为: X 上升沿(非 RST)时开始计时，达 TIME 后 OUT 拉高；X 复位时 OUT 立即拉低。
                    // ---------------------------------------------------------
                    if ((bool)RST || !(bool)X)
                    {
                        // TD-ON 在复位有效或 X 变低时立即回到空闲态。
                        ResetTimer();
                    }
                    else
                    {
                        bool startedThisCycle = false;
                        if (xRise)
                        {
                            _timing = true;
                            TRun[0] = 0.0f;
                            startedThisCycle = true;
                        }

                        if (_timing)
                        {
                            if (!startedThisCycle && TRun < time)
                                TRun[0] = TRun + dt;

                            // 达到 TIME 的当前周期立即置位，不再额外延迟一个扫描周期。
                            OUT[0] = TRun >= time;
                        }
                        else
                        {
                            OUT[0] = false;
                        }
                    }
                    break;

                case 3:
                    // ---------------------------------------------------------
                    // 模式 3: TD-OFF 延时断开模块 (断电延时断开 TOF)
                    // 行为: X 上升沿后 OUT 立即拉高；X 下降沿开始计时，达 TIME 后 OUT 拉低。
                    // ---------------------------------------------------------
                    if (RST)
                    {
                        ResetTimer();
                    }
                    else if (X)
                    {
                        // TD-OFF 的接通方向为电平逻辑：X 为高时 OUT 必须立即为高，
                        // 同时取消尚未完成的延时断开。
                        OUT[0] = true;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        bool startedThisCycle = false;
                        if (xFall)
                        {
                            _timing = true;
                            TRun[0] = 0.0f;
                            startedThisCycle = true;
                        }

                        if (_timing)
                        {
                            if (!startedThisCycle)
                                TRun[0] = TRun + dt;

                            if (TRun >= time)
                            {
                                OUT[0] = false;
                                _timing = false;
                            }
                            else
                                OUT[0] = true;
                        }
                        else
                        {
                            // X 为低且没有延时断开任务时，空闲输出必须为低。
                            OUT[0] = false;
                        }
                    }
                    break;

                case 4:
                    // ---------------------------------------------------------
                    // 模式 4: TD-ON-HOLD 延时接通保持模块
                    // 行为: X 上升沿后延时 TIME 拉高 OUT，并一直保持到 RST 复位。延时未到时 X 再次上升沿则重新计时。
                    // ---------------------------------------------------------
                    if (RST)
                    {
                        ResetTimer();
                    }
                    else
                    {
                        // 兼容升级前 MODE=4 的完成态：旧实现完成后为 OUT=true、_timing=false、TRun>=TIME。
                        if (firstExecution && time > 0.0f && !_timing && (bool)OUT && TRun >= time)
                            _timing = true;

                        bool completed = _timing && TRun >= time;
                        bool startedThisCycle = false;
                        if (xRise && !completed)
                        {
                            _timing = true;
                            TRun[0] = 0.0f;
                            startedThisCycle = true;
                        }

                        if (_timing)
                        {
                            if (!startedThisCycle && TRun < time)
                                TRun[0] = TRun + dt;

                            // 完成后保留 _timing=true 作为内部锁存态，直到 RST 复位。
                            OUT[0] = TRun >= time;
                        }
                        else
                        {
                            OUT[0] = false;
                        }
                    }
                    break;

                default:
                    // 非法模式按安全空闲态处理，避免保留上一模式输出。
                    ResetTimer();
                    break;
            }

            // 4. 品质传递逻辑
            if (QualityT == 1 || QualityT == 2)
            {
                // 弱传递(1)、强传递(2)：输出主要跟随 X，因此主要判断 X 的品质
                if (X.Quality != QualityTypes.Good)
                {
                    OUT.Quality = QualityTypes.Bad;
                    TRun.Quality = QualityTypes.Bad;
                }
                else
                {
                    OUT.Quality = QualityTypes.Good;
                    TRun.Quality = QualityTypes.Good;
                }
            }
            else // 0: NoTransfer
            {
                OUT.Quality = QualityTypes.Good;
                TRun.Quality = QualityTypes.Good;
            }

            // 5. 更新历史状态
            _prevX = X;
        }

    }
}
