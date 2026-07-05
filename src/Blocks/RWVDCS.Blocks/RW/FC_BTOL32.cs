using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("BTOL32")]
	[FCDisplay("布尔量转长整型")]
	public partial class BTOL32 : Function 
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

        // 输入引脚B0-B31
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

        [PinType(PinTypes.Input)]
        [PinDisplay("输入16")]
        public LD B16 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入17")]
        public LD B17 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入18")]
        public LD B18 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入19")]
        public LD B19 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入20")]
        public LD B20 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入21")]
        public LD B21 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入22")]
        public LD B22 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入23")]
        public LD B23 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入24")]
        public LD B24 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入25")]
        public LD B25 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入26")]
        public LD B26 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入27")]
        public LD B27 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入28")]
        public LD B28 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入29")]
        public LD B29 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入30")]
        public LD B30 = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入31")]
        public LD B31 = new LD(QualityTypes.Good, false, false, false, 0, false);


        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LP32 OUT = new LP32();


        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public UInt32 QualityT = 0;

    }
}
