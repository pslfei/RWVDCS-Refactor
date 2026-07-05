using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
    public partial class FIRST32
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable) return;

            // ================== 1. HMI 复位指令上升沿采集 (参照 DEVICE 模式) ==================
            bool curCRS = CRS, edgeCRS = curCRS && !oldCRS;

            // ================== 2. 扫描 32 个输入：统计激活数 + 首个 TRUE 序号 ==================
            // 循环展开避免数组分配/GC，配合 CPU 预取与流水线带来稳定的扫描速度
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
            if (X17) { activeCount++; if (firstHit == 0) firstHit = 17; }
            if (X18) { activeCount++; if (firstHit == 0) firstHit = 18; }
            if (X19) { activeCount++; if (firstHit == 0) firstHit = 19; }
            if (X20) { activeCount++; if (firstHit == 0) firstHit = 20; }
            if (X21) { activeCount++; if (firstHit == 0) firstHit = 21; }
            if (X22) { activeCount++; if (firstHit == 0) firstHit = 22; }
            if (X23) { activeCount++; if (firstHit == 0) firstHit = 23; }
            if (X24) { activeCount++; if (firstHit == 0) firstHit = 24; }
            if (X25) { activeCount++; if (firstHit == 0) firstHit = 25; }
            if (X26) { activeCount++; if (firstHit == 0) firstHit = 26; }
            if (X27) { activeCount++; if (firstHit == 0) firstHit = 27; }
            if (X28) { activeCount++; if (firstHit == 0) firstHit = 28; }
            if (X29) { activeCount++; if (firstHit == 0) firstHit = 29; }
            if (X30) { activeCount++; if (firstHit == 0) firstHit = 30; }
            if (X31) { activeCount++; if (firstHit == 0) firstHit = 31; }
            if (X32) { activeCount++; if (firstHit == 0) firstHit = 32; }

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
                    FNo[0] = (float)firstHit;
                }
            }
            else
            {
                // 已锁存：仅当所有 32 路输入全 FALSE 且复位请求有效时才清除上次首出印记
                // 规范要求："直到输入 X1～X32 均为 FALSE，输入 RST 为 TRUE，输出 OUT 和 FNo 才会被重置"
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

            // ================== 6. 状态打包点1 (PACK1) → TAG.Value ==================
            // 严格遵循 FIRST32.md PACK1 位定义表
            uint pack1 = 0;
            if (X1) pack1 |= (1u << 0);
            if (X2) pack1 |= (1u << 1);
            if (X3) pack1 |= (1u << 2);
            if (X4) pack1 |= (1u << 3);
            if (X5) pack1 |= (1u << 4);
            if (X6) pack1 |= (1u << 5);
            if (X7) pack1 |= (1u << 6);
            if (X8) pack1 |= (1u << 7);
            if (X9) pack1 |= (1u << 8);
            if (X10) pack1 |= (1u << 9);
            if (X11) pack1 |= (1u << 10);
            if (X12) pack1 |= (1u << 11);
            if (X13) pack1 |= (1u << 12);
            if (X14) pack1 |= (1u << 13);
            if (X15) pack1 |= (1u << 14);
            if (X16) pack1 |= (1u << 15);
            if (RST) pack1 |= (1u << 16);   // Bit16: 复位信号 RST
            if (OUT) pack1 |= (1u << 17);   // Bit17: 输出 OUT
            if (QOut) pack1 |= (1u << 18);  // Bit18: 输入取真输出 QOut
            TAG.Value = pack1;

            // ================== 7. 状态打包点2 (PACK2) → PK2.Value ==================
            // 规范明确要求 X17~X32 映射到 Bit16~Bit31 (而非 Bit0~Bit15)
            // 与 popup_FSTX32.xtp 中 DiExp="dpu.device_FST@PK2.Value.BITxx" (xx=17~31) 严格对应
            uint pack2 = 0;
            if (X17) pack2 |= (1u << 16);
            if (X18) pack2 |= (1u << 17);
            if (X19) pack2 |= (1u << 18);
            if (X20) pack2 |= (1u << 19);
            if (X21) pack2 |= (1u << 20);
            if (X22) pack2 |= (1u << 21);
            if (X23) pack2 |= (1u << 22);
            if (X24) pack2 |= (1u << 23);
            if (X25) pack2 |= (1u << 24);
            if (X26) pack2 |= (1u << 25);
            if (X27) pack2 |= (1u << 26);
            if (X28) pack2 |= (1u << 27);
            if (X29) pack2 |= (1u << 28);
            if (X30) pack2 |= (1u << 29);
            if (X31) pack2 |= (1u << 30);
            if (X32) pack2 |= (1u << 31);
            PK2.Value = pack2;
        }
    }
}
