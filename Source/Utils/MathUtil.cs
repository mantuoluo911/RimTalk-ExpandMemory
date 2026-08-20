using System;
using System.Collections.Generic;
using UnityEngine;

namespace RimTalk.Memory.Utils;

public static class MathUtil
{
    /// <summary>
    /// 生成位于 [min, max] 内的所有 nice 刻度，延迟枚举。
    /// </summary>
    public static List<long> GenerateTicks(
        int min,
        int max,
        int maxTickCount,
        out long step)
    {
        if (max <= min)
            throw new ArgumentException("max must be greater than min.");

        if (maxTickCount < 1)
            throw new ArgumentException("maxTickCount must be at least 1.");

        step = CalculateNiceStep((long)max - min, maxTickCount);

        List<long> ticks = new();
        for (long tick = (min > 0 ? min + step - 1 : min) / step * step; tick <= max; tick += step)
            ticks.Add(tick);

        return ticks;
    }

    /// <summary>
    /// 生成位于 [min, max] 内的所有 nice 刻度，延迟枚举。
    /// </summary>
    public static List<float> GenerateTicks(
        float min,
        float max,
        int maxTickCount,
        out float step)
    {
        if (max <= min)
            throw new ArgumentException("max must be greater than min.");

        if (maxTickCount < 1)
            throw new ArgumentException("maxTickCount must be at least 1.");

        step = CalculateNiceStep(max - min, maxTickCount);

        // 与 long 版本一致：min > 0 时向上对齐到 step 的整数倍。
        float start = min > 0f ? (float)Math.Ceiling(min / step) * step : min;

        // 用 start + i * step 而非累加，避免浮点误差累积。
        List<float> ticks = new();
        for (int i = 0; ; i++)
        {
            float tick = start + i * step;
            if (tick > max)
                break;

            ticks.Add(tick);
        }

        return ticks;
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

    /// <summary>
    /// 根据给定范围和最大刻度数量，计算合适的刻度步长。
    /// 步长形式为 1 / 2 / 5 × 10^n，n 可为负数。
    /// 注意两端点不被计入刻度数量。
    /// </summary>
    public static float CalculateNiceStep(
        float range,
        int maxTickCount)
    {
        if (range <= 0f)
            throw new ArgumentException("range must be positive.");

        if (maxTickCount < 1)
            throw new ArgumentException("maxTickCount must be at least 1.");

        // N 个刻度产生 N + 1 个间隔，因此步长取 range / (N + 1)，
        // 向上取到 nice 值的工作由下方 switch 完成。
        float roughStep = range / (maxTickCount + 1);

        // 找到 roughStep 所在的 10 的数量级，支持小数数量级。
        float magnitude = 1f;

        while (magnitude * 10f <= roughStep)
            magnitude *= 10f;

        while (magnitude > roughStep)
            magnitude /= 10f;

        // 向上取到 1 / 2 / 5 / 10 再恢复数量级。
        return (roughStep / magnitude) switch
        {
            <= 1f => 1f,
            <= 2f => 2f,
            <= 5f => 5f,
            _ => 10f
        } * magnitude;
    }
}
