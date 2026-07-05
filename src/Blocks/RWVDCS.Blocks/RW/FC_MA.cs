using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("MA")]
    [FCDisplay("模拟量手操器")]
    public partial class MA : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("自动指令输入")]
        public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("跟踪值")]
        public LA TR = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("跟踪开关")]
        public LD TS = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("切手动开关")]
        public LD ToM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("投自动开关")]
        public LD ReqA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输出OUT高限值")]
        public LA MaxOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("输出OUT低限值")]
        public LA MinOut = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("闭锁增开关")]
        public LD LI = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("闭锁减开关")]
        public LD LD = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("反馈值")]
        public LA FB = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("就地开关")]
        public LD Loc = new LD(QualityTypes.Good, false, false, false, 0, false);

        // ==================== 输出 ====================

        [PinType(PinTypes.Output)]
        [PinDisplay("使能输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示，手动状态为1，自动状态为0")]
        public LD Ma = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("HMI 状态打包点 (PACK)，按位映射 跟踪/手自动/增减/微调/闭锁/启动 状态")]
        public LP32 TAG = new LP32();

        // ==================== 参数 ====================

        [PinType(PinTypes.Constant)]
        [PinDisplay("增减步长（工程单位绝对值）")]
        public float Step1 = 5.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("微调增减步长（工程单位绝对值）")]
        public float Step2 = 1.0f;

        // ============ HMI 命令引脚（框架/IOMap 按字段名绑定 @CXM/@CUP/... 测点） ============

        [PinType(PinTypes.Constant)]
        [PinDisplay("手动指令源端页号")]
        public UInt32 CXMPAGE = 0;

        [PinType(PinTypes.IO)]
        [PinDisplay("手动指令（设定值直输）")]
        public LA CXM = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("增指令源端页号")]
        public UInt32 CUPPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("增指令")]
        public LD CUP = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("减指令源端页号")]
        public UInt32 CDNPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("减指令")]
        public LD CDN = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("微调增指令源端页号")]
        public UInt32 CFUPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("微调增指令")]
        public LD CFU = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("微调减指令源端页号")]
        public UInt32 CFDPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("微调减指令")]
        public LD CFD = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("切手动指令源端页号")]
        public UInt32 CTMPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("切手动指令")]
        public LD CTM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("切自动指令源端页号")]
        public UInt32 CTAPAGE = 0;

        [PinType(PinTypes.Input)]
        [PinDisplay("切自动指令")]
        public LD CTA = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("手动状态指示源端页号")]
        public UInt32 MSTPAGE = 0;

        [PinType(PinTypes.Output)]
        [PinDisplay("手动状态指示（镜像 Ma，供独立 @MST 测点）")]
        public LD MST = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;

        // ==================== 内部状态 ====================

        [PinType(PinTypes.Internal)]
        [PinDisplay("启动周期计数")]
        public int startupCounter = 0;
    }
}
