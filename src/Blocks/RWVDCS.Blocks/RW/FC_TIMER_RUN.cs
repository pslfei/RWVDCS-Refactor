using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

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

            // 2. 边沿检测逻辑
            bool xRise = (X & !_prevX);       // X 上升沿
            bool xFall = (!X & _prevX);       // X 下降沿
            bool rstRise = (RST & !_prevRST); // RST 上升沿

            // 3. 根据工作模式(MODE)选择
            switch (MODE)
            {
                case 0:
                    // ---------------------------------------------------------
                    // 模式 0: TD-PULSE 延时脉冲模块（不可重触发）
                    // 行为: X 上升沿后 OUT 立即输出一个宽度为 TIME 的脉冲；脉冲输出期间 X 的变化
                    //       不影响计时(不可重触发)；RST 复位优先级最高，RST=1 时 OUT 立即复位为 0、定时器清零。
                    // ---------------------------------------------------------
                    if (RST)
                    {
                        // RST 复位信号优先级最高，OUT 立即复位、计时清零
                        OUT[0] = false;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        // 不可重触发: 仅在空闲(未计时)时响应上升沿，脉冲期间忽略新的上升沿
                        if (xRise && !_timing)
                        {
                            OUT[0] = true;  // 上升沿立即输出高电平
                            _timing = true;
                            TRun[0] = 0.0f;
                        }

                        if (_timing)
                        {
                            TRun[0] = TRun + dt;
                            if (TRun >= TIME)
                            {
                                OUT[0] = false; // 脉冲宽度达到 TIME，输出复位为低
                                _timing = false;
                            }
                        }
                    }
                    break;
                case 1:
                    // ---------------------------------------------------------
                    // 模式 1: PULSE 脉冲输出模块（可重触发）
                    // 行为: X 上升沿后输出立即拉高，保持 TIME 后回落；期间 X 再次上升沿，TIME 重新计时(可重触发)。
                    // ---------------------------------------------------------
                    if (rstRise)
                    {
                        OUT[0] = false;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        if (xRise && !RST)
                        {
                            OUT[0] = true;
                            _timing = true;
                            TRun[0] = 0.0f; // 期间 X 再次出现上升沿，TIME 重新计时
                        }

                        if (_timing)
                        {
                            TRun[0] = TRun + dt;
                            if (TRun >= TIME)
                            {
                                OUT[0] = false;
                                _timing = false;
                            }
                        }
                    }
                    break;

                case 2:
                    // ---------------------------------------------------------
                    // 模式 2: TD-ON 延时接通模块 (上电延时接通 TON)
                    // 行为: X 上升沿(非 RST)时开始计时，达 TIME 后 OUT 拉高；X 复位时 OUT 立即拉低。
                    // ---------------------------------------------------------
                    if (rstRise)
                    {
                        OUT[0] = false;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        if (xRise && !RST)
                        {
                            _timing = true;
                            TRun[0] = 0.0f;
                        }

                        if (!X)
                        {
                            // X 复位为低电平时，输出 OUT 立刻复位为低电平
                            OUT[0] = false;
                            _timing = false;
                            TRun[0] = 0.0f;
                        }

                        if (_timing)
                        {
                            if (TRun < TIME)
                            {
                                TRun[0] = TRun + dt;
                            }
                            else
                            {
                                OUT[0] = true; // 计满延时时间后输出拉高为高电平
                            }
                        }
                    }
                    break;

                case 3:
                    // ---------------------------------------------------------
                    // 模式 3: TD-OFF 延时断开模块 (断电延时断开 TOF)
                    // 行为: X 上升沿后 OUT 立即拉高；X 下降沿开始计时，达 TIME 后 OUT 拉低。
                    // ---------------------------------------------------------
                    if (rstRise)
                    {
                        OUT[0] = false;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        if (xRise)
                        {
                            OUT[0] = true;  // X 由 0 到 1，OUT 立即输出为 1
                            _timing = false;
                            TRun[0] = 0.0f;
                        }
                        else if (xFall & OUT)
                        {
                            // 在输出为高时 X 的下降沿开始计时
                            _timing = true;
                            TRun[0] = 0.0f;
                        }

                        if (_timing)
                        {
                            if (X)
                            {
                                // 计时未到时若 X 再次拉高，取消本次延时 (维持高电平)
                                _timing = false;
                                TRun[0] = 0.0f;
                            }
                            else
                            {
                                TRun[0] = TRun + dt;
                                if (TRun >= TIME)
                                {
                                    OUT[0] = false;
                                    _timing = false;
                                }
                            }
                        }
                    }
                    break;

                case 4:
                    // ---------------------------------------------------------
                    // 模式 4: TD-ON-HOLD 延时接通保持模块
                    // 行为: X 上升沿后延时 TIME 拉高 OUT，并一直保持到 RST 复位。延时未到时 X 再次上升沿则重新计时。
                    // ---------------------------------------------------------
                    if (rstRise)
                    {
                        OUT[0] = false;
                        _timing = false;
                        TRun[0] = 0.0f;
                    }
                    else
                    {
                        if (xRise)
                        {
                            _timing = true;
                            TRun[0] = 0.0f; // 延时 TIME 内再次出现上升沿，重新计时
                        }

                        if (_timing)
                        {
                            TRun[0] = TRun + dt;
                            if (TRun >= TIME)
                            {
                                OUT[0] = true; // 输出为高电平并一直保持
                                _timing = false;
                            }
                        }
                    }
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
            _prevRST = RST;
        }

    }
}
