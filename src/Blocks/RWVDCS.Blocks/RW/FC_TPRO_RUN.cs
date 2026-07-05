using System;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Blocks.RW
{
	public partial class TPRO
	{
		protected override void Run(ICommand cmd)
		{
			if (!Enable) return;

			float cycle = cmd.Dpu.Cycle; // 扫描周期(秒)
			float currentX = X;

			// 1. 质量检查 - 检测温度测点坏质量
			bool isBad = (X.Quality != QualityTypes.Good);
			BAD[0] = isBad;

			// 2. 飞升速率计算 (°C/min)
			float rate = 0;
			if (cycle > 0 && oldX != 0)
				rate = Math.Abs(currentX - oldX) / (cycle / 60.0f);
			oldX = currentX;

			// 3. 飞升速率报警
			RAlm[0] = (rate >= RatV);

			// 4. 温度高报警（带死区DB1）
			if (currentX > AlmV)
				HAlm[0] = true;
			else if (currentX <= AlmV - DB1)
				HAlm[0] = false;

			// 5. 温度保护动作逻辑
			if (isBad)
			{
				// 坏质量时始终闭锁温度保护动作
				PAct[0] = false;
				timerAcc = 0;
			}
			else if (!Cut && !PAct)
			{
				// X>ProV 且无坏质量 且未达到飞升速率限制值，延时TIME后触发保护
				if (currentX > ProV && !RAlm)
				{
					timerAcc += cycle;
					if (timerAcc >= TIME)
					{
						PAct[0] = true;
						Cut[0] = true;
					}
				}
				else
				{
					timerAcc = 0;
				}
			}

			// 6. 重新投入（复位）逻辑
			if (Cut)
			{
				if (!EnR)
				{
					// EnR=0: 温度降到 ProV-DB2 以下自动重新投入
					if (currentX < ProV - DB2)
					{
						Cut[0] = false;
						PAct[0] = false;
						timerAcc = 0;
					}
				}
				else
				{
					// EnR=1: 由外部RST信号控制重新投入
					if (RST)
					{
						Cut[0] = false;
						PAct[0] = false;
						timerAcc = 0;
					}
				}
			}

			// 7. 综合故障报警 TRL = 坏质量 OR 飞升速率报警 OR 温度高报警
			TRL[0] = BAD | RAlm | HAlm;
		}
	}
}
