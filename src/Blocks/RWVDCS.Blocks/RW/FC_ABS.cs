using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("ABS")]
	[FCDisplay("绝对值")]
	public partial class ABS : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

        [PinType(PinTypes.Input)]
        [PinDisplay("算法块是否可用")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("_ENO=_EN")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, false);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入")]
		public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 12.0f);


        [PinType(PinTypes.Input)]
        [PinDisplay("输入")]
        public LA X11 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 100.0f);

        [PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LP32 TAG = new LP32();


        [PinType(PinTypes.Input)]
        [PinDisplay("输入1")]
        public LD X3 = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LD X4 = new LD(QualityTypes.Good, false, false, false, 0, true);


        [PinType(PinTypes.Input)]
        [PinDisplay("输入")]
        public LA X5 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 99.0f);

        [PinType(PinTypes.Constant)]
		[PinDisplay("规格数")]
		public float k = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float b = 0;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public UInt32 QualityT = 0;

    }
}
