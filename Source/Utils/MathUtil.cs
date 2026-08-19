using System;
using System.Collections.Generic;

namespace RimTalk.Memory.Utils;

public static class MathUtil
{
    /// <summary>
    /// 生成位于 [min, max] 内的所有 nice 刻度，延迟枚举。
    /// </summary>
    public static IEnumerable<long> GenerateTicks(
        int min,
        int max,
        int maxTickCount)
    {
        if (max <= min)
            throw new ArgumentException("max must be greater than min.");

        if (maxTickCount < 1)
            throw new ArgumentException("maxTickCount must be at least 1.");

        long step = CalculateNiceStep((long)max - min, maxTickCount);

        for (long value = (min > 0 ? min + step - 1 : min) / step * step; value <= max; value += step)
            yield return value;
    }

    /// <summary>
    /// 根据给定范围和最大刻度数量，计算合适的刻度步长。
    /// 步长形式为 1 / 2 / 5 × 10^n。
    /// 注意两端点不被计入刻度数量。
    /// </summary>
    public static long CalculateNiceStep(
        long range,
        int maxTickCount)
    {
        if (range <= 0)
            throw new ArgumentException("range must be positive.");

        if (maxTickCount < 1)
            throw new ArgumentException("maxTickCount must be at least 1.");

        // N 个刻度产生 N + 1 个间隔，因此步长取 ceil(range / (N + 1))。
        long roughStep = (range + maxTickCount) / (maxTickCount + 1);

        // 找到 roughStep 所在的 10 的数量级。
        long magnitude = 1;

        while (magnitude * 10 <= roughStep)
            magnitude *= 10;

        // ceil(roughStep / magnitude)，然后向上取到 1 / 2 / 5 / 10 再恢复数量级。
        return ((roughStep + magnitude - 1) / magnitude) switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        } * magnitude;
    }
}
