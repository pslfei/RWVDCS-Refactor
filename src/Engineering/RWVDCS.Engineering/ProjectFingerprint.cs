using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RWVDCS.Engineering;

/// <summary>
/// 工程指纹：对工程模型的规范化内容求 SHA-256，取前 8 字节十六进制。
/// 同一 mdb 装载出的模型指纹恒定；任何点/块/管脚/参数的增删改都会改变指纹。
/// 工况/快照按指纹判断"工程是否一致"，在线下装后指纹随之更新。
/// </summary>
/// <remarks>
/// 必须在 <b>装配前的原始模型</b>上计算：RuntimeBuilder 装配过程会就地改写
/// 管脚 PointName（pin-point 源块输出追加）与 Reversed（粘滞取反），装配后的模型
/// 不再等于工程库内容。宿主在 LoadProject 时先克隆/计算指纹再装配。
/// </remarks>
public static class ProjectFingerprint
{
    public static string Compute(EngineeringModel model)
    {
        using var sha = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(model.Controllers.Count);
            foreach (var c in model.Controllers)
            {
                w.Write(c.Id);
                w.Write(c.Name);
                w.Write(c.Address ?? "");

                w.Write(c.Points.Count);
                foreach (var p in c.Points)
                {
                    w.Write(p.Name);
                    w.Write(p.DataType);
                    w.Write(FormatValue(p.DefaultValue));
                    w.Write(p.MaxValue);
                    w.Write(p.MinValue);
                    w.Write(p.LowAlarmLimit1Value);
                    w.Write(p.LowAlarmLimit2Value);
                    w.Write(p.LowAlarmLimit3Value);
                    w.Write(p.HighAlarmLimit1Value);
                    w.Write(p.HighAlarmLimit2Value);
                    w.Write(p.HighAlarmLimit3Value);
                    w.Write(p.Description ?? "");
                    w.Write(p.Unit ?? "");
                }

                w.Write(c.Blocks.Count);
                foreach (var b in c.Blocks)
                {
                    w.Write(b.Name);
                    w.Write(b.FcName);
                    w.Write(b.Pins.Count);
                    foreach (var pin in b.Pins)
                    {
                        w.Write(pin.PinName);
                        w.Write(pin.PointName ?? "");
                        w.Write(pin.Reversed);
                        w.Write(pin.HasDefaultValue);
                        w.Write(FormatValue(pin.DefaultValue));
                    }
                }
            }
        }
        stream.FlushFinalBlock();
        return Convert.ToHexString(sha.Hash!.AsSpan(0, 8)).ToLowerInvariant();
    }

    /// <summary>默认值的规范化文本（float 用 R 格式；float[] 逐元素；DefaultFromPoint 用占位名）。</summary>
    internal static string FormatValue(object? value) => value switch
    {
        null => "",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        float[] arr => string.Join(",", arr.Select(x => x.ToString("R", CultureInfo.InvariantCulture))),
        DefaultFromPoint dfp => "@point:" + dfp.PointName,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };
}
