using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class FIRST
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // ================== 1. HMI 复位指令上升沿采集 (参照 DEVICE 模式) ==================
            bool curCRS = CRS, edgeCRS = curCRS && !oldCRS;

            // ================== 2. 扫描所有输入：统计激活数 + 首个 TRUE 序号 ==================
            // 循环展开避免数组分配/GC，扫描周期可预测
            int activeCount = 0;
            int firstHit = 0;
            if (X1) { activeCount++; if (firstHit == 0) firstHit = 1; }
            if (X2) { activeCount++; if (firstHit == 0) firstHit = 2; }
            if (X3) { activeCount++; if (firstHit == 0) firstHit = 3; }
            if (X4) { activeCount++; if (firstHit == 0) firstHit = 4; }
            if (X5) { activeCount++; if (firstHit == 0) firstHit = 5; }
            if (X6) { activeCount++; if (firstHit == 0) firstHit = 6; }
            if (X7) { activeCount++; if (firstHit == 0) firstHit = 7; }
            if (X8) { activeCount++; if (firstHit == 0) firstHit = 8; }
            if (X9) { activeCount++; if (firstHit == 0) firstHit = 9; }
            if (X10) { activeCount++; if (firstHit == 0) firstHit = 10; }
            if (X11) { activeCount++; if (firstHit == 0) firstHit = 11; }
            if (X12) { activeCount++; if (firstHit == 0) firstHit = 12; }
            if (X13) { activeCount++; if (firstHit == 0) firstHit = 13; }
            if (X14) { activeCount++; if (firstHit == 0) firstHit = 14; }
            if (X15) { activeCount++; if (firstHit == 0) firstHit = 15; }
            if (X16) { activeCount++; if (firstHit == 0) firstHit = 16; }

            // ================== 3. 首出锁存与复位逻辑 ==================
            // 复位触发条件 = 硬引脚 RST 电平 OR HMI CRS 上升沿
            // 用沿触发 HMI CRS 避免按住按钮时残留复位信号干扰新事件捕获
            bool resetReq = RST | edgeCRS;
            if ((float)FNo == 0f)
            {
                // 尚未锁存首出：本周期检测到任意 TRUE 即锁存
                if (firstHit > 0)
                {
                    OUT[0] = true;
                    FNo[0] = firstHit;
                }
            }
            else
            {
                // 已锁存：仅当所有输入全 FALSE 且复位请求有效时才清除
                // 规范要求："直到输入 X1～X16 均为 FALSE，输入 RST 为 TRUE，输出 OUT 和 FNo 才会被重置"
                if (activeCount == 0 && resetReq)
                {
                    OUT[0] = false;
                    FNo[0] = 0f;
                }
            }

            // SFN 与 FNo 同值：FNo 联动逻辑功能块、SFN 反向同步到 @SFN 子测点供 HMI 显示
            SFN[0] = FNo;

            // ================== 4. 与运算输出 QOut ==================
            QOut[0] = activeCount >= (uint)NUM;

            // ================== 5. 保存沿检测历史 ==================
            oldCRS = curCRS;

            // ================== 6. 状态打包点 (PACK) → TAG.Value ==================
            // 严格遵循 FIRST.md PACK 位定义表，与 XTP 面板脚本读取的 BIT 一一对应
            uint pack = 0;
            if (X1) pack |= (1u << 0);
            if (X2) pack |= (1u << 1);
            if (X3) pack |= (1u << 2);
            if (X4) pack |= (1u << 3);
            if (X5) pack |= (1u << 4);
            if (X6) pack |= (1u << 5);
            if (X7) pack |= (1u << 6);
            if (X8) pack |= (1u << 7);
            if (X9) pack |= (1u << 8);
            if (X10) pack |= (1u << 9);
            if (X11) pack |= (1u << 10);
            if (X12) pack |= (1u << 11);
            if (X13) pack |= (1u << 12);
            if (X14) pack |= (1u << 13);
            if (X15) pack |= (1u << 14);
            if (X16) pack |= (1u << 15);
            if (RST) pack |= (1u << 16);    // Bit16: 复位信号 RST
            if (OUT) pack |= (1u << 17);    // Bit17: 输出 OUT
            if (QOut) pack |= (1u << 18);   // Bit18: 输入取真输出 QOut
            TAG.Value = pack;
        }
    }
}
