using System;
using System.Collections.Generic;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System.Runtime.InteropServices;

namespace RWVDCS.Blocks.RW
{
	[FCName("STAAVG")]
	[FCDisplay("均值块")]
	public partial class STAAVG : Function 
	{
        [PinType(PinTypes.Input)]
        [PinDisplay("模块使能")]
        public LD Enable = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入1(启动/停止)")]
        public LD START = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Input)]
        [PinDisplay("输入2(采样数据)")]
        public LA X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("输出使能")]
        public LD _ENO = new LD(QualityTypes.Good, false, false, false, 0, true);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前缓冲区数据和")]
        public LA SUM = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前缓冲区数据均值")]
        public LA AVG = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前缓冲区数据最大值")]
        public LA MAX = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("当前缓冲区数据最小值")]
        public LA MIN = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("连续采样时溢出的采样数据")]
        public LA Old_X = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Output)]
        [PinDisplay("缓冲区数据存储满标志")]
        public LD FIN = new LD(QualityTypes.Good, false, false, false, 0, false);

        [PinType(PinTypes.Output)]
        [PinDisplay("缓冲区存储的数据量")]
        public LA COUNT = new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0.0f, 0, 0.0f);

        [PinType(PinTypes.Constant)]
        [PinDisplay("采样周期间隔")]
        public uint ITE = 1;

        [PinType(PinTypes.Constant)]
        [PinDisplay("缓冲区可存储的最大数据量")]
        public uint AMOUNT = 900;

        [PinType(PinTypes.Constant)]
        [PinDisplay("品质传递")]
        public uint QualityT = 0; // 0:NoTransfer, 1:OrTransfer, 2:AndTransfer

        // --- 内部状态变量 (不作为引脚暴露) ---
        [PinType(PinTypes.Internal)]
        [MarshalAsAttribute(UnmanagedType.ByValArray, SizeConst = 900)]
        private float[] _buffer = new float[900]; // 最大支持900个数据的缓冲区


        [PinType(PinTypes.Internal)]
        private int _head = 0;                    // 队列头指针 (最旧的数据)

        [PinType(PinTypes.Internal)]
        private int _tail = 0;                    // 队列尾指针 (下一个插入位置)

        [PinType(PinTypes.Internal)]
        private int _currentCount = 0;            // 队列当前数据量

        [PinType(PinTypes.Internal)]
        private uint _cycleCounter = 0;           // 运算周期计数器

        [PinType(PinTypes.Internal)]
        private uint _lastAmount = uint.MaxValue; // 记录上一次的AMOUNT，用于检测变化

    }
}
