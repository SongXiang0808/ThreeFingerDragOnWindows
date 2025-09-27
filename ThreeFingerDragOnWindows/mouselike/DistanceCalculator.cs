using System;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.mouselike;

/// <summary>
/// 距离计算工具类，移植自MouseLikeTouchPad_I2C驱动的距离计算逻辑
/// </summary>
public static class DistanceCalculator
{
    /// <summary>
    /// 计算两点之间的欧几里得距离
    /// 移植自驱动中的 sqrt(dx * dx + dy * dy) 计算
    /// </summary>
    /// <param name="point1">第一个点</param>
    /// <param name="point2">第二个点</param>
    /// <returns>距离值</returns>
    public static float CalculateDistance(Point point1, Point point2)
    {
        float dx = point2.x - point1.x;
        float dy = point2.y - point1.y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 计算两点之间的距离及方向向量
    /// </summary>
    /// <param name="point1">起始点</param>
    /// <param name="point2">终止点</param>
    /// <returns>距离和方向向量 (dx, dy, distance)</returns>
    public static (float dx, float dy, float distance) CalculateDistanceAndDirection(Point point1, Point point2)
    {
        float dx = point2.x - point1.x;
        float dy = point2.y - point1.y;
        float distance = (float)Math.Sqrt(dx * dx + dy * dy);
        return (dx, dy, distance);
    }

    /// <summary>
    /// 判断两个手指是否在有效距离范围内
    /// 移植自驱动中的手指角色分配逻辑
    /// </summary>
    /// <param name="finger1">手指1位置</param>
    /// <param name="finger2">手指2位置</param>
    /// <param name="settings">手势设置</param>
    /// <returns>是否在有效范围内</returns>
    public static bool IsWithinValidRange(Point finger1, Point finger2, GestureSettings settings)
    {
        float distance = CalculateDistance(finger1, finger2);
        return distance >= settings.FingerMinDistance && distance <= settings.FingerMaxDistance;
    }

    /// <summary>
    /// 判断手指是否处于有效分开状态 (简化版，移除合拢检测)
    /// 移植自驱动中的按键判断逻辑
    /// </summary>
    /// <param name="finger1">手指1位置</param>
    /// <param name="finger2">手指2位置</param>
    /// <param name="settings">手势设置</param>
    /// <returns>是否在有效范围内</returns>
    public static bool IsSeparated(Point finger1, Point finger2, GestureSettings settings)
    {
        float distance = CalculateDistance(finger1, finger2);
        return distance >= settings.FingerMinDistance && distance <= settings.FingerMaxDistance;
    }

    /// <summary>
    /// 消除微小抖动 - 针对MouseLike模式优化，几乎不过滤以减少漂移
    /// 基于驱动逻辑改进，减少过度过滤
    /// </summary>
    /// <param name="value">原始值</param>
    /// <param name="jitterOffset">抖动阈值</param>
    /// <returns>消除抖动后的值</returns>
    public static float RemoveJitter(float value, float jitterOffset)
    {
        // 极小的抖动阈值，几乎不过滤以避免鼠标漂移
        float threshold = Math.Max(jitterOffset * 0.05f, 0.1f); // 最小0.1像素阈值，非常敏感

        if (Math.Abs(value) <= threshold)
            return 0;

        return value;
    }

    /// <summary>
    /// 应用鼠标敏感度计算 - 改进版本，增加亚像素累积
    /// 移植自驱动中的指针移动计算，改进版本
    /// </summary>
    /// <param name="delta">原始移动增量</param>
    /// <param name="sensitivity">敏感度</param>
    /// <param name="thumbScale">手指缩放</param>
    /// <returns>计算后的移动值</returns>
    public static Point ApplySensitivity(Point delta, float sensitivity, float thumbScale)
    {
        // 如果没有移动，直接返回零
        if (delta.x == 0 && delta.y == 0)
            return Point.Zero;

        float adjustedX = (delta.x / thumbScale) * sensitivity;
        float adjustedY = (delta.y / thumbScale) * sensitivity;

        // 改进的亚像素处理 - 四舍五入减少漂移
        return new Point((int)Math.Round(adjustedX), (int)Math.Round(adjustedY));
    }
}