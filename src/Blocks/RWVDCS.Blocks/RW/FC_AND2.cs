using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("AND2")]
	[FCDisplay("逻辑与")]
	public partial class AND2 : Function 
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
        [PinDisplay("输入1")]
        public LD X1 = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LD X2 = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出")]
        public LD OUT = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public UInt32 QualityT = 0;
    }
}
