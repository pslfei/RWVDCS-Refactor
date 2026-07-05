using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class VSET
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 提取当前周期输入，避免重复装箱/拆箱
            float curOut = OUT;
            float curTR = TR;
            float max = MaxOut;
            float min = MinOut;
            if (min > max) min = max;
            bool curTS = TS;

            uint pack = 0;
            QualityTypes outQuality = QualityTypes.Good;

            // ================== 1. 首次运行：OUT = TR (DPU 启动恢复) ==================
            // 规范要求 DPU 启动时输出等于跟踪值，且品质同步
            if (firstRun)
            {
                curOut = curTR;
                outQuality = TR.Quality;
                pack |= (1u << 5); // Bit5: 启动标记
                firstRun = false;
            }

            // ================== 2. HMI 命令上升沿采集 (参照 DEVICE 模式) ==================
            // 引脚由框架/IOMap 注入：HMI 面板按下按钮后，对应 LD 引脚电平拉高
            bool curCUP = CUP, edgeCUP = curCUP && !oldCUP;
            bool curCDN = CDN, edgeCDN = curCDN && !oldCDN;
            bool curCFU = CFU, edgeCFU = curCFU && !oldCFU;
            bool curCFD = CFD, edgeCFD = curCFD && !oldCFD;
            float curCXM = CXM;

            // ================== 3. 主控制逻辑：跟踪 vs 手动设定 ==================
            if (curTS)
            {
                // 跟踪模式 (优先级最高)：OUT 跟随 TR，品质同步
                // HMI 面板 DisableExp = BIT0，跟踪态下所有按钮已在前端灰显，
                // 后端再以分支闭锁形式做纵深防御，丢弃任何残留沿
                curOut = curTR;
                outQuality = TR.Quality;
                pack |= (1u << 0); // Bit0: 跟踪开关指示
            }
            else
            {
                // 手动设定模式：品质恢复为 Good
                outQuality = QualityTypes.Good;

                // 优先响应 HMI 数值直输 (面板中央数值控件直写 CXM 引脚)
                // 仅当用户实际改动了 CXM 才生效，避免与反写循环互锁
                bool cxmEdge = cxmInitialized && curCXM != oldCXM;
                if (cxmEdge)
                {
                    curOut = curCXM;
                }
                else
                {
                    // 加 / 减 / 微加 / 微减 边沿响应
                    // 使用绝对值，防止参数被错误配置为负数导致按钮反向
                    float s1 = Math.Abs(Step1);
                    float s2 = Math.Abs(Step2);
                    if (edgeCUP) curOut += s1;
                    if (edgeCDN) curOut -= s1;
                    if (edgeCFU) curOut += s2;
                    if (edgeCFD) curOut -= s2;
                }
            }

            // ================== 4. 安全限幅 ==================
            if (curOut > max) curOut = max;
            else if (curOut < min) curOut = min;

            // ================== 5. PACK 状态字组装 ==================
            // 严格遵循 VSET.md PACK 位定义表，与 XTP 面板颜色 / 灰显表达式一一对应
            if (curCFU) pack |= (1u << 3); // Bit3: 微增指令 (HMI 动画反馈)
            if (curCFD) pack |= (1u << 4); // Bit4: 微减指令
            if (curCUP) pack |= (1u << 6); // Bit6: 增指令
            if (curCDN) pack |= (1u << 7); // Bit7: 减指令

            // ================== 6. 保存沿检测历史 ==================
            oldCUP = curCUP;
            oldCDN = curCDN;
            oldCFU = curCFU;
            oldCFD = curCFD;

            // ================== 7. 输出 ==================
            OUT[0] = curOut;
            OUT.Quality = outQuality;

            // 反写：将 OUT 同步回 CXM 引脚，保持 HMI SP 显示与实际输出一致
            // 同时刷新 oldCXM 基线，避免本周期反写的值被下周期误判为"人工变更"
            CXM[0] = curOut;
            oldCXM = curOut;
            cxmInitialized = true;

            // ================== 8. 状态打包点 (PACK) → TAG.Value ==================
            // 框架自动将 LP32 TAG 同步到外部 @TAG 测点，供 HMI 颜色/灰显绑定使用
            TAG.Value = pack;
        }
    }
}
