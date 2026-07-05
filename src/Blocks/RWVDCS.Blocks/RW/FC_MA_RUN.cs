using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class MA
    {
        // ================== HMI 命令边沿检测历史缓存 ==================
        // 脉冲类指令（加 / 减 / 微加 / 微减 / 切手动 / 投自动）由框架/IOMap 注入对应 LD 引脚
        private bool oldCUP, oldCDN, oldCFU, oldCFD;
        private bool oldCTM, oldCTA;

        // 设定值反写跟踪：区分"人工改动 CXM"与"上周期反写"，避免互锁
        private float oldCXM;
        private bool cxmInitialized;

        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // 提取当前周期输入，避免重复装箱 / 拆箱
            float curX = X;
            float curFB = FB;
            float curTR = TR;
            float curOut = OUT;
            float max = MaxOut;
            float min = MinOut;
            if (min > max) min = max;

            bool curTS = TS;
            bool curToM = ToM;
            bool curReqA = ReqA;
            bool curLoc = Loc;
            bool curLI = LI;
            bool curLD = LD;
            bool isManual = Ma;

            // ================== 1. HMI 命令上升沿采集 ==================
            // 引脚由框架/IOMap 注入：面板按钮按下后对应 LD 引脚电平拉高
            bool curCUP = CUP, edgeCUP = curCUP && !oldCUP;
            bool curCDN = CDN, edgeCDN = curCDN && !oldCDN;
            bool curCFU = CFU, edgeCFU = curCFU && !oldCFU;
            bool curCFD = CFD, edgeCFD = curCFD && !oldCFD;
            bool curCTM = CTM, edgeCTM = curCTM && !oldCTM;
            bool curCTA = CTA, edgeCTA = curCTA && !oldCTA;
            float curCXM = CXM;

            // ================== 2. 手 / 自动模式切换 ==================
            // 优先级：ToM > ReqA > HMI 边沿。ToM 为 TRUE 时闭锁 ReqA（MA.md 规范）
            if (curToM) isManual = true;
            else if (curReqA) isManual = false;
            else
            {
                if (edgeCTM) isManual = true;
                if (edgeCTA) isManual = false;
            }

            // ================== 3. 优先级控制与输出计算 ==================
            // 工况优先级：启动 > 就地 > 跟踪 > 手动 > 自动
            uint pack = 0;
            QualityTypes outQuality = QualityTypes.Good;

            if (startupCounter < 3)
            {
                // 启动前 3 周期：强制手动，OUT = FB
                isManual = true;
                curOut = curFB;
                outQuality = FB.Quality;
                pack |= (1u << 14);
                if (startupCounter == 0) pack |= (1u << 15);
                startupCounter++;
            }
            else if (curLoc)
            {
                // 就地：OUT 跟随 FB
                curOut = curFB;
                outQuality = FB.Quality;
            }
            else if (curTS)
            {
                // 跟踪：OUT 跟随 TR，品质同步
                curOut = curTR;
                outQuality = TR.Quality;
            }
            else if (isManual)
            {
                // 手动：响应 HMI 命令（设定值直输 + 增减边沿）
                outQuality = QualityTypes.Good; // TODO: 按 QualityT 参数合成

                // 设定值直接输入：面板点击中央数值控件直写 @CXM
                // 仅当用户实际改动了 @CXM 才生效，避免与反写循环互锁
                bool cxmEdge = cxmInitialized && curCXM != oldCXM;
                if (cxmEdge)
                {
                    curOut = curCXM;
                }
                else
                {
                    // 增减边沿响应。LI / LD 闭锁同时在手动模式下生效，
                    // 防止 HMI 端被破解或网络回放攻击触发越界指令（后端纵深防护）
                    float bigStep = Math.Abs(Step1);    // 增 / 减 按钮步长
                    float smallStep = Math.Abs(Step2);  // 微增 / 微减 按钮步长

                    // 增方向：增按钮优先走大步长，否则微增走小步长；LI 闭锁增时不响应
                    if (!curLI)
                    {
                        if (edgeCUP) curOut += bigStep;
                        else if (edgeCFU) curOut += smallStep;
                    }
                    // 减方向：减按钮优先走大步长，否则微减走小步长；LD 闭锁减时不响应
                    if (!curLD)
                    {
                        if (edgeCDN) curOut -= bigStep;
                        else if (edgeCFD) curOut -= smallStep;
                    }
                }
            }
            else
            {
                // 自动：跟随 X，方向闭锁仅限增 / 减相反方向
                outQuality = QualityTypes.Good; // TODO: 按 QualityT 参数合成 X.Quality
                bool blockInc = curLI && curX > curOut;
                bool blockDec = curLD && curX < curOut;
                if (!blockInc && !blockDec)
                    curOut = curX;
            }

            // ================== 4. 安全限幅 ==================
            if (curOut > max) curOut = max;
            else if (curOut < min) curOut = min;

            // ================== 5. PACK 状态字组装 ==================
            // 严格遵循 MA.md PACK 位定义表，与 XTP 面板颜色 / 灰显表达式一一对应
            if (curTS) pack |= (1u << 0);       // Bit0  TS
            if (curToM) pack |= (1u << 1);      // Bit1  ToM
            if (curReqA) pack |= (1u << 2);     // Bit2  ReqA
            if (isManual) pack |= (1u << 3);    // Bit3  Ma（M 红 / A 绿依据）
            if (curLoc) pack |= (1u << 4);      // Bit4  Loc
            // Bit5 保护态：HMI 灰显总闸（DisableExp = BIT5）
            if (curTS || !isManual || curLoc) pack |= (1u << 5);
            if (curCUP) pack |= (1u << 6);      // Bit6  增指令（HMI 动画反馈）
            if (curCDN) pack |= (1u << 7);      // Bit7  减指令
            if (curCFU) pack |= (1u << 8);      // Bit8  微增指令
            if (curCFD) pack |= (1u << 9);      // Bit9  微减指令
            if (curLI) pack |= (1u << 10);      // Bit10 BI 闭锁增
            if (curLD) pack |= (1u << 11);      // Bit11 BD 闭锁减
            if (curCTM) pack |= (1u << 12);     // Bit12 切手动指令
            if (curCTA) pack |= (1u << 13);     // Bit13 投自动指令
            // Bit14 / Bit15 启动标志已在启动分支内组装

            // ================== 6. 保存沿检测历史 ==================
            oldCUP = curCUP;
            oldCDN = curCDN;
            oldCFU = curCFU;
            oldCFD = curCFD;
            oldCTM = curCTM;
            oldCTA = curCTA;

            // ================== 7. 输出 & 设定值反写 ==================
            Ma[0] = isManual;
            MST[0] = isManual; // 手动状态镜像到独立 @MST 测点（HMI 主要依赖 PACK Bit3）
            OUT[0] = curOut;
            OUT.Quality = outQuality;

            // 设定值反写：将 OUT 同步回 CXM 引脚，保持 HMI SP 显示与实际输出一致
            // 同时刷新 oldCXM 基线，避免本周期反写的值被下周期误判为"人工变更"
            CXM[0] = curOut;
            oldCXM = curOut;
            cxmInitialized = true;

            // ================== 8. 状态打包点（PACK） → TAG.Value ==================
            // 框架自动将 LP32 TAG 同步到外部 @TAG（DPU.<块>.Value）测点，供 HMI 颜色/灰显绑定
            TAG.Value = pack;
        }
    }
}
