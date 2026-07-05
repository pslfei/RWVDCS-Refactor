using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
    [FCName("DIFF")]
    [FCDisplay("微分")]
    public partial class DIFF : Function
    {
        [PinType(PinTypes.Constant)]
        [PinDisplay("算法块的描述")]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
        public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入")]
        public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("高限")]
        public LA H = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("低限")]
        public LA L = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("微分输出")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("增益系数")]
        public float Kd = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("微分时间常数")]
        public float Td = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;


        [PinType(PinTypes.Internal)]
        [PinDisplay("上一次的输出值")]
        float OLD_OUT = 0;

        [PinType(PinTypes.Internal)]
        [PinDisplay("上一次的输入值")]
        float OLD_X = 0;

    }
}
