using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("ASEL")]
	[FCDisplay("模拟量切换选择")]
	public partial class ASEL : Function 
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
        [PinDisplay("选择信号输入")]
        public LD SEL = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入1")]
        public LA X1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 88.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LA X2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public UInt32 QualityT = 0;
    }
}
