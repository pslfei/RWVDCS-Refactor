using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using System;

namespace RWVDCS.Blocks.RW
{
    public partial class PTC
    {
        protected override void Run(ICommand cmd)
        {
            _ENO[0] = Enable;
            if (!Enable)
                return;

            float pVal = P;
            float tVal = T;

            if (pVal <= 0.0f || tVal <= -273.15f)
                return;

            float tAbs = tVal + 273.15f;
            int mode = (int)MODE;
            int ftype = (int)FType;

            float result = 0.0f;

            if (mode == 2)
            {
                result = CalcSaturationT(pVal);
                OUT[0] = result;
                return;
            }

            if (mode == 3)
            {
                result = CalcSaturationP(tVal);
                OUT[0] = result;
                return;
            }

            float molarMass = GetMolarMass(ftype);
            float cp = GetCp(ftype);
            float rSpecific = 8.314f / molarMass * 1000.0f;

            switch (mode)
            {
                case 0:
                    result = pVal * 1e6f / (rSpecific * tAbs);
                    break;
                case 1:
                    result = cp * tVal;
                    break;
                case 4:
                    result = cp * (float)Math.Log(tAbs / 273.15) -
                             rSpecific / 1000.0f * (float)Math.Log(pVal / 0.101325);
                    break;
            }

            OUT[0] = result;
        }

        private float GetMolarMass(int ftype)
        {
            switch (ftype)
            {
                case 0: return 28.97f;
                case 1: return 18.015f;
                case 2: return 28.014f;
                case 3: return 31.999f;
                case 4: return 2.016f;
                case 5: return 39.948f;
                default: return 28.97f;
            }
        }

        private float GetCp(int ftype)
        {
            switch (ftype)
            {
                case 0: return 1.005f;
                case 1: return 2.08f;
                case 2: return 1.040f;
                case 3: return 0.918f;
                case 4: return 14.30f;
                case 5: return 0.520f;
                default: return 1.005f;
            }
        }

        private float CalcSaturationT(float pMPa)
        {
            double pKPa = pMPa * 1000.0;
            double logP = Math.Log10(pKPa * 7.50062);
            double tSat;
            if (pMPa < 0.101325)
                tSat = 1730.63 / (8.07131 - logP) - 233.426;
            else
                tSat = 1810.94 / (8.14019 - logP) - 244.485;
            return (float)tSat;
        }

        private float CalcSaturationP(float tC)
        {
            double logP;
            if (tC < 100.0)
                logP = 8.07131 - 1730.63 / (233.426 + tC);
            else
                logP = 8.14019 - 1810.94 / (244.485 + tC);
            double pMmHg = Math.Pow(10.0, logP);
            double pMPa = pMmHg / 7500.62;
            return (float)pMPa;
        }
    }
}
