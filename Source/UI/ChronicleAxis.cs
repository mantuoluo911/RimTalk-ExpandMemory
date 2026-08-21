using RimTalk.Memory.Utils;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimTalk.Memory.UI;

public class ChronicleAxis
{
    // 轴标签与刻度线绘制相关
    private const float AxisLabelGap = 4.0f;
    private const float DayTickHeight = 6f;
    private const float QuadrumTickHeight = 8f;
    private const float YearTickHeight = 10f;
    private const float YearTickWidth = 2f;

    // 刻度步长运算相关
    private const int MaxTickCount = 12;
    private const int MaxYearTickCount = 9;
    private const int MaxRangeForStep1 = 1 * (MaxTickCount + 1) * GenDate.TicksPerDay;
    private const int MaxRangeForStep3 = 3 * (MaxTickCount + 1) * GenDate.TicksPerDay;
    private const int MaxRangeForStep5 = 5 * (MaxTickCount + 1) * GenDate.TicksPerDay;
    private const int MaxRangeForStep15 = 15 * (MaxTickCount + 1) * GenDate.TicksPerDay;
    private const int MaxRangeForStep30 = 30 * (MaxTickCount + 1) * GenDate.TicksPerDay;

    // 缓存相关
    private Rect _rect;
    private int _startTick;
    private int _endTick;
    private readonly List<(Rect BoxRect, Rect LabelRect, string Label)> _tickDrawCache = new();
    private readonly List<(Rect BoxRect, Rect LabelRect, string Label)> _tickDrawTinyCache = new();
    private readonly List<(Rect BoxRect, Rect LabelRect, string Label)> _tickDrawMediumCache = new();

    public void Draw(Rect rect, int startTick, int endTick)
    {
        // 背景
        Widgets.DrawBoxSolid(rect, new Color(0.095f, 0.105f, 0.115f, 0.96f));

        if (startTick >= endTick) return;

        // 两端点
        Color tickColor = new(0.38f, 0.42f, 0.45f);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 2f, YearTickHeight), tickColor);
        Widgets.DrawBoxSolid(new Rect(rect.xMax - 2f, rect.y, 2f, YearTickHeight), tickColor);

        // 检查（并重建）缓存
        CheckCache(rect, startTick, endTick);

        // 绘制刻度线和标签
        foreach (var (boxRect, labelRect, label) in _tickDrawCache)
        {
            Widgets.DrawBoxSolid(boxRect, tickColor);
            Widgets.Label(labelRect, label);
        }

        using (new TextBlock(GameFont.Tiny))
            foreach (var (boxRect, labelRect, label) in _tickDrawTinyCache)
            {
                Widgets.DrawBoxSolid(boxRect, tickColor);
                Widgets.Label(labelRect, label);
            }

        using (new TextBlock(GameFont.Medium))
            foreach (var (boxRect, labelRect, label) in _tickDrawMediumCache)
            {
                Widgets.DrawBoxSolid(boxRect, tickColor);
                Widgets.Label(labelRect, label);
            }
    }

    // 检查缓存是否需要更新，如果 rect 或 tick 范围发生变化，则重新计算刻度列表和绘制缓存。
    private void CheckCache(Rect rect, int startTick, int endTick)
    {
        if (_rect != rect || _startTick != startTick || _endTick != endTick)
        {
            _rect = rect;
            _startTick = startTick;
            _endTick = endTick;
            RebuildCache();
        }
    }
    private void RebuildCache()
    {
        _tickDrawCache.Clear();
        _tickDrawTinyCache.Clear();
        _tickDrawMediumCache.Clear();

        float width = _rect.width;
        float xMax = _rect.xMax;

        float labelY = _rect.y + AxisLabelGap;
        float labelHeight = _rect.yMax - labelY;

        float x = _rect.x;
        float y = _rect.y;

        int startAbsTick = GenDate.TickGameToAbs(_startTick);
        int endAbsTick = GenDate.TickGameToAbs(_endTick);

        var tickList = CalculateTickList(startAbsTick, endAbsTick, out int tickStep);
        float xStep = tickStep / (float)(endAbsTick - startAbsTick) * width;

        bool isFirstTick = true;
        foreach (int tick in tickList)
        {
            if (isFirstTick)
            {
                x += (tick - startAbsTick) / (float)(endAbsTick - startAbsTick) * width;
                isFirstTick = false;
            }
            else x += xStep;
            
            switch (tick)
            {
                case var _ when tick % GenDate.TicksPerYear == 0:
                    float yearLabelX = x + YearTickWidth + AxisLabelGap;
                    _tickDrawMediumCache.Add((
                        new Rect(x, y, YearTickWidth, YearTickHeight),
                        new Rect(yearLabelX, labelY, xMax - yearLabelX, labelHeight),
                        $"{GenDate.Year(tick, 0L)}年"
                        ));
                    break;
                case var _ when tick % GenDate.TicksPerQuadrum == 0:
                    float quadrumLabelX = x + 1f + AxisLabelGap;
                    _tickDrawCache.Add((
                        new Rect(x, y, 1f, QuadrumTickHeight),
                        new Rect(quadrumLabelX, labelY, xMax - quadrumLabelX, labelHeight),
                        GenDate.Quadrum(tick, 0L).Label()
                    ));
                    break;
                default:
                    float dayLabelX = x + 1f + AxisLabelGap;
                    _tickDrawTinyCache.Add((
                        new Rect(x, y, 1f, DayTickHeight),
                        new Rect(dayLabelX, labelY, xMax - dayLabelX, labelHeight),
                        $"第{GenDate.DayOfQuadrum(tick, 0L)}天"
                    ));
                    break;
            }
        }

    }

    // 计算刻度列表和步长，步长梯度为 {1，3，5，15，30} + {60，120，300}*10^k，即日、象、年、年 * nice tick。
    // 返回时会把刻度和 step 都换算成绝对 tick，便于后续绘制。
    // 因为是非常纯粹的逻辑运算，所以封装成一个静态方法。
    private static IEnumerable<int> CalculateTickList(int startAbsTick, int endAbsTick, out int step)
    {
        // 使用预计算的阈值和 nice step 计算步长
        int range = endAbsTick - startAbsTick;
        float dayStep = range switch
        {
            <= MaxRangeForStep1 => 1f,
            <= MaxRangeForStep3 => 3f,
            <= MaxRangeForStep5 => 5f,
            <= MaxRangeForStep15 => 15f,
            <= MaxRangeForStep30 => 30f,
            _ => MathUtil.CalculateNiceStep(range / (float)GenDate.TicksPerYear, MaxYearTickCount) * GenDate.DaysPerYear,
        };

        // float dayStep 和 GenerateTicksFromStep 返回的 float day 都是整数 float，可以大胆直接强转 int
        step = (int)dayStep * GenDate.TicksPerDay;
        return MathUtil.GenerateTicksFromStep(
            startAbsTick / (float)GenDate.TicksPerDay,
            endAbsTick / (float)GenDate.TicksPerDay,
            dayStep
            ).Select(day => (int)day * GenDate.TicksPerDay);
    }
}
