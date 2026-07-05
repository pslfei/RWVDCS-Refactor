using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("DEV")]
    [FCDisplay("偏差运算")]
    public partial class DEV : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块的描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入1")]
        public LA X1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LA X2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("偏差高限值输入")]
        public LA H = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("偏差低限值输入")]
        public LA L = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, -100.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("偏差输出")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("报警状态输出")]
        public LD ALM = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("超高限报警输出")]
        public LD HAlm = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("超低限报警输出")]
        public LD LAlm = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Constant)]
        [PinDisplay("X1增益")]
        public float k1 = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("X1偏置")]
        public float b1 = 0.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("X2增益")]
        public float k2 = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("X2偏置")]
        public float b2 = 0.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("输入值死区")]
        public float DBX = 0.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("报警死区")]
        public float DBA = 0.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;
    }
}
