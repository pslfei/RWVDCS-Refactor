using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
	public partial class LOG
	{
		protected override void Run(ICommand cmd) 
		{
			//if (X2 == 0)
			//{
			//	OUT[0] = Math.Log(X1);
   //         }
			//else
			//{
   //             if(X2 == 1) {
			//	OUT[0] = Math.Log(X2, X1);
			//}

            float safeX1 = X1;
            float safeX2 = X2;
            double result = 0.0;
            // 2. 数学定义域保护 1：真数 X1 必须大于 0
            if (safeX1 <= 0.0f)
            {
                // 如果输入了非法的零或负数，强制箝位为一个极小的正数
                // 避免产生 NaN (Not a Number) 导致下游控制回路崩溃
                safeX1 = 1e-6f;
            }
            // 3. 执行对数运算
            // 浮点数判断是否为 0，推荐使用极小值容差判断，避免精度丢失导致的误判
            if (Math.Abs(safeX2) <= 1e-6f)
            {
                // 文档规定：当 X2 为 0.0 时，计算自然对数 ln(X1)
                result = Math.Log(safeX1);
            }
            else
            {
                // 数学定义域保护 2：底数 X2 不能等于 1，且不能为负数 (除特例 0 外)
                if (safeX2 < 0.0f || Math.Abs(safeX2 - 1.0f) <= 1e-6f)
                {
                    // 遇到非法底数，输出 0 进行安全兜底
                    result = 0.0;
                }
                else
                {
                    // 核心修复：Math.Log(真数, 底数)
                    // 文档：OUT = log_{X2} X1  =>  Math.Log(X1, X2)
                    result = Math.Log(safeX1, safeX2);
                }
            }
            // 4. 类型转换并赋值给输出引脚
            // 为了防止极端情况下的浮点溢出，这里也可以加一层对 float.MaxValue 的限幅
            if (result > float.MaxValue) result = float.MaxValue;
            if (result < float.MinValue) result = float.MinValue;
            OUT[0] = (float)result;
        }
	}
}
