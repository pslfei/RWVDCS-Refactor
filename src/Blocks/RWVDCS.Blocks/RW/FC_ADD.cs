using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("ADD")]
	[FCDisplay("加法")]
	public partial class ADD : Function 
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
        public LA X1 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2")]
        public LA X2 = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
        public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float k = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float b = 0;


        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float k1 = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float b1 = 0;



        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float k2 = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("规格数")]
        public float b2 = 0;


    }
}
