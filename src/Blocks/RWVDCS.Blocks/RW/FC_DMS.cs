using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("DMS")]
    [FCDisplay("开关量手操器")]
    public partial class DMS : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("跟踪值")]
        public LD TR = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("跟踪开关")]
        public LD TS = new LD(QualityTypes.Good, false, false, false, 0, false);

        // ==================== 输出 ====================

        [PinType(PinTypes.Output)]
        [PinDisplay("使能输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD OUT = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 (PACK)，按位映射 跟踪/输出/手动指令/脉冲 状态")]
        public LP32 TAG = new LP32();

        // ==================== 参数 ====================

        [PinType(PinTypes.Constant)]
        [PinDisplay("脉冲时长（单位：秒，pulse 模式下有效）")]
        public float PulT = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("指令模式：0=pulse 1=toggle 2=normal")]
        public UInt32 Mode = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("源端页号")]
        public UInt32 PAGE = 0;

        // ============ HMI 命令引脚（框架/IOMap 按字段名绑定 @CXM 测点） ============

        [PinType(PinTypes.Constant)]
        [PinDisplay("手动指令源端页号")]
        public UInt32 CXMPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("手动指令（HMI 投入=1 / 切除=0）")]
        public LD CXM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;
    }
}
