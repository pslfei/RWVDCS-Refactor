using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("BTOL")]
	[FCDisplay("布尔量转长整型")]
	public partial class BTOL : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("输入")]
        public LD _EN = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        // 输入引脚B0-B15
        [PinType(PinTypes.Input)]
        [PinDisplay("输入0")]
        public LD B0 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入1")]
        public LD B1 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LD B2 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入3")]
        public LD B3 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入4")]
        public LD B4 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入5")]
        public LD B5 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入6")]
        public LD B6 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入7")]
        public LD B7 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入8")]
        public LD B8 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入9")]
        public LD B9 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入10")]
        public LD B10 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入11")]
        public LD B11 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入12")]
        public LD B12 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入13")]
        public LD B13 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入14")]
        public LD B14 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入15")]
        public LD B15 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LP32 OUT = new LP32();

        [PinType(PinTypes.Constant)]
        [PinDisplay("高字/低字")]
        public UInt32 WORD = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;

    }
}
