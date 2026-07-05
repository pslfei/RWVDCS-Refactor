using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("SQRT")]
	[FCDisplay("开平方")]
	public partial class SQRT : Function 
	{
		[PinType(PinTypes.Constant)]
		[PinDisplay("算法块的描述")]
		[MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 50)]
		public string Description = "";

		[PinType(PinTypes.Input)]
		[PinDisplay("模块使能")]
		public LD Enable = new LD(QualityTypes.Good, false, false, false,0,true);

		[PinType(PinTypes.Input)]
		[PinDisplay("输入")]
		public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出")]
		public LD _ENO = new LD(QualityTypes.Good, false, false, false,0,false);

		[PinType(PinTypes.Output)]
		[PinDisplay("输出开平方值")]
		public LA OUT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 1.0f);

		[PinType(PinTypes.Constant)]
		[PinDisplay("X增益")]
		public float kX = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("X偏置")]
        public float bX = 1.0f;

        [PinType(PinTypes.Constant)]
        [PinDisplay("死区值")]
        public float DB = 1.0f;


        [PinType(PinTypes.Constant)]
        [PinDisplay("增益")]
        public float k = 1.0f;

    }
}
