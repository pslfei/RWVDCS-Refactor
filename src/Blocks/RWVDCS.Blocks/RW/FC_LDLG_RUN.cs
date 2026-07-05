using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class LDLG
	{
		protected override void Run(ICommand cmd)
		{
			// 输出使能状态；未使能时保持上一次输出并跳过本周期运算
			_ENO[0] = Enable;
			if (!Enable)
				return;

			float T = cmd.Dpu.Cycle;
			float currentX = X;
			float currentOut;


			if (cmd.Dpu.CycleCount == 1)
			{
				// 第一个运算周期：输出直接等于输入，并以此初始化历史状态
				currentOut = currentX;
			}
			else if (TS)
			{
				// 跟踪模式（TS=1）：输出等于跟踪值 TR
				currentOut = TR;
			}
			else if (LD == 0.0f && LG == 0.0f)
			{
				// 直通模式：超前/滞后常数均为 0 时，输出直接等于输入
				currentOut = currentX;
			}
			else
			{
				// 采用双线性变换离散化的超前-滞后差分方程：
				// OUT(n) = (2*LG-T)/(T+2*LG)*OUT(n-1)
				//        + (T+2*LD)/(T+2*LG)*X(n)
				//        + (T-2*LD)/(T+2*LG)*X(n-1)
				// 强制满足文档约束 LG >= T/2，保证离散算法稳定且分母不为 0
				float safeLG = LG;
				if (safeLG < T / 2.0f)
				{
					safeLG = T / 2.0f;
				}
				float denominator = T + 2.0f * safeLG;
				float term1 = (2.0f * safeLG - T) / denominator * OLD_OUT;
				float term2 = (T + 2.0f * LD) / denominator * currentX;
				float term3 = (T - 2.0f * LD) / denominator * OLD_X;
				currentOut = term1 + term2 + term3;
            }

			// 输出高低限幅（High / Low）
			if (currentOut > H) currentOut = H;
			if (currentOut < L) currentOut = L;

			// 保存历史状态供下一周期递推使用（保存限幅后的值，防止积分饱和）
			OLD_X = currentX;
			OLD_OUT = currentOut;

			// 写入实际输出引脚
			OUT[0] = currentOut;
		}
	}
}
