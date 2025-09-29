using System;
using System.Runtime.InteropServices;
using ThreeFingerDragEngine.utils;
using ThreeFingerDragOnWindows.utils;

namespace ThreeFingerDragOnWindows.touchpad;

/// <summary>
/// 触摸板物理信息，移植自MouseLikeTouchPad_I2C驱动的触摸板尺寸检测
/// </summary>
public class TouchpadInfo
{
    public uint PhysicalMaxX { get; set; }
    public uint PhysicalMaxY { get; set; }
    public uint LogicalMaxX { get; set; }
    public uint LogicalMaxY { get; set; }
    public sbyte UnitExponent { get; set; }
    public byte Unit { get; set; }

    public double PhysicalWidthMm { get; set; }
    public double PhysicalHeightMm { get; set; }

    public float TouchpadDpmmX { get; set; }
    public float TouchpadDpmmY { get; set; }

    public bool IsValid => PhysicalWidthMm > 0 && PhysicalHeightMm > 0;

    /// <summary>
    /// 计算基于触摸板实际尺寸的手指距离参数
    /// </summary>
    public void CalculateFingerDistanceParameters(float thumbScale, out float fingerMinDistance, out float fingerMaxDistance)
    {
        // 基于C++驱动的算法：FingerMinDistance = 12mm * DPMM * thumbScale
        fingerMinDistance = 12f * TouchpadDpmmX * thumbScale;
        fingerMaxDistance = fingerMinDistance * 4f; // 最大距离是最小距离的4倍

        Logger.Log($"TouchpadInfo: Calculated finger distances - Min: {fingerMinDistance:F1}, Max: {fingerMaxDistance:F1}");
    }

    /// <summary>
    /// 获取基于触摸板尺寸的抖动消除偏移量
    /// </summary>
    public float GetJitterOffset(float baseJitter = 0.4f)
    {
        // 基于触摸板尺寸调整抖动消除
        float dpmmAverage = (TouchpadDpmmX + TouchpadDpmmY) / 2f;
        float adjustedJitter = baseJitter * Math.Max(1f, dpmmAverage / 10f);
        return Math.Min(adjustedJitter, 2f); // 限制最大值
    }

    /// <summary>
    /// 基于触摸板尺寸计算垂直容差
    /// </summary>
    public float GetVerticalTolerance()
    {
        // 基于触摸板高度的12%作为垂直容差，但至少20mm的逻辑单位
        float heightBasedTolerance = LogicalMaxY * 0.12f;
        float minimumTolerance = 20f * TouchpadDpmmY;
        return Math.Max(heightBasedTolerance, minimumTolerance);
    }

    public override string ToString()
    {
        return $"TouchpadInfo: {PhysicalWidthMm:F1}mm x {PhysicalHeightMm:F1}mm, " +
               $"DPMM: ({TouchpadDpmmX:F2}, {TouchpadDpmmY:F2}), " +
               $"Logical: {LogicalMaxX}x{LogicalMaxY}, " +
               $"Physical: {PhysicalMaxX}x{PhysicalMaxY}";
    }
}

/// <summary>
/// 触摸板信息检测器，移植自C++驱动的HID报告描述符分析算法
/// </summary>
public static class TouchpadInfoDetector
{
    // HID单位指数表，对应C++驱动中的UnitExponent_Table
    private static readonly sbyte[] UnitExponentTable = {0,1,2,3,4,5,6,7,-8,-7,-6,-5,-4,-3,-2,1};

    /// <summary>
    /// 尝试从触摸板设备获取物理尺寸信息
    /// 移植自C++驱动的AnalyzeHidReportDescriptor函数
    /// </summary>
    public static TouchpadInfo DetectTouchpadInfo()
    {
        var info = new TouchpadInfo();

        try
        {
            // 获取触摸板设备句柄
            var deviceHandle = GetTouchpadDeviceHandle();
            if (deviceHandle == IntPtr.Zero)
            {
                Logger.Log("TouchpadInfoDetector: No touchpad device found");
                return CreateDefaultTouchpadInfo();
            }

            // 获取预解析数据
            var preparsedData = GetPreparsedData(deviceHandle);
            if (preparsedData == IntPtr.Zero)
            {
                Logger.Log("TouchpadInfoDetector: Failed to get preparsed data");
                return CreateDefaultTouchpadInfo();
            }

            try
            {
                // 获取设备能力
                if (HidP_GetCaps(preparsedData, out var caps) != HIDP_STATUS_SUCCESS)
                {
                    Logger.Log("TouchpadInfoDetector: Failed to get HID capabilities");
                    return CreateDefaultTouchpadInfo();
                }

                // 获取值能力
                var valueCapsLength = caps.NumberInputValueCaps;
                var valueCaps = new HIDP_VALUE_CAPS[valueCapsLength];

                if (HidP_GetValueCaps(HIDP_REPORT_TYPE.HidP_Input, valueCaps, ref valueCapsLength, preparsedData) != HIDP_STATUS_SUCCESS)
                {
                    Logger.Log("TouchpadInfoDetector: Failed to get value capabilities");
                    return CreateDefaultTouchpadInfo();
                }

                // 解析触摸板物理参数
                ParseTouchpadDimensions(valueCaps, info);

                if (info.IsValid)
                {
                    Logger.Log($"TouchpadInfoDetector: Successfully detected - {info}");
                }
                else
                {
                    Logger.Log("TouchpadInfoDetector: Detection failed, using defaults");
                    return CreateDefaultTouchpadInfo();
                }
            }
            finally
            {
                Marshal.FreeHGlobal(preparsedData);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"TouchpadInfoDetector: Exception during detection - {ex.Message}");
            return CreateDefaultTouchpadInfo();
        }

        return info;
    }

    private static IntPtr GetTouchpadDeviceHandle()
    {
        uint deviceListCount = 0;
        var rawInputDeviceListSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();

        if (GetRawInputDeviceList(null, ref deviceListCount, rawInputDeviceListSize) != 0)
            return IntPtr.Zero;

        var devices = new RAWINPUTDEVICELIST[deviceListCount];

        if (GetRawInputDeviceList(devices, ref deviceListCount, rawInputDeviceListSize) != deviceListCount)
            return IntPtr.Zero;

        foreach (var device in devices)
        {
            if (device.dwType != RIM_TYPEHID) continue;

            uint deviceInfoSize = 0;
            if (GetRawInputDeviceInfo(device.hDevice, RIDI_DEVICEINFO, IntPtr.Zero, ref deviceInfoSize) != 0)
                continue;

            var deviceInfo = new RID_DEVICE_INFO { cbSize = deviceInfoSize };

            if (GetRawInputDeviceInfo(device.hDevice, RIDI_DEVICEINFO, ref deviceInfo, ref deviceInfoSize) == unchecked((uint)-1))
                continue;

            // 检查是否为精确触摸板设备
            if (deviceInfo.hid.usUsagePage == 0x000D && deviceInfo.hid.usUsage == 0x0005)
            {
                return device.hDevice;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr GetPreparsedData(IntPtr deviceHandle)
    {
        uint preparsedDataSize = 0;

        if (GetRawInputDeviceInfo(deviceHandle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref preparsedDataSize) != 0)
            return IntPtr.Zero;

        var preparsedData = Marshal.AllocHGlobal((int)preparsedDataSize);

        if (GetRawInputDeviceInfo(deviceHandle, RIDI_PREPARSEDDATA, preparsedData, ref preparsedDataSize) != preparsedDataSize)
        {
            Marshal.FreeHGlobal(preparsedData);
            return IntPtr.Zero;
        }

        return preparsedData;
    }

    private static void ParseTouchpadDimensions(HIDP_VALUE_CAPS[] valueCaps, TouchpadInfo info)
    {
        bool inTouchTlc = false;

        foreach (var valueCap in valueCaps)
        {
            // 检查是否在触摸集合中
            if (valueCap.LinkCollection > 0)
            {
                inTouchTlc = true;
            }

            if (!inTouchTlc) continue;

            // 解析X轴参数
            if (valueCap.Usage == 0x30) // HID_USAGE_X
            {
                info.PhysicalMaxX = (uint)valueCap.PhysicalMax;
                info.LogicalMaxX = (uint)valueCap.LogicalMax;
                info.UnitExponent = UnitExponentTable[valueCap.UnitsExp & 0x0F];
                info.Unit = (byte)(valueCap.Units & 0xFF);

                Logger.Log($"TouchpadInfoDetector: X-axis - Physical: {info.PhysicalMaxX}, Logical: {info.LogicalMaxX}, UnitExp: {info.UnitExponent}, Unit: 0x{info.Unit:X2}");
            }
            // 解析Y轴参数
            else if (valueCap.Usage == 0x31) // HID_USAGE_Y
            {
                info.PhysicalMaxY = (uint)valueCap.PhysicalMax;
                info.LogicalMaxY = (uint)valueCap.LogicalMax;

                Logger.Log($"TouchpadInfoDetector: Y-axis - Physical: {info.PhysicalMaxY}, Logical: {info.LogicalMaxY}");

                // 计算物理尺寸（毫米）
                CalculatePhysicalDimensions(info);
                break; // Y轴处理完成后退出
            }
        }
    }

    private static void CalculatePhysicalDimensions(TouchpadInfo info)
    {
        if (info.PhysicalMaxX == 0 || info.PhysicalMaxY == 0) return;

        double unitMultiplier = Math.Pow(10.0, info.UnitExponent);

        // 根据单位计算物理尺寸
        if (info.Unit == 0x11) // 厘米单位
        {
            info.PhysicalWidthMm = info.PhysicalMaxX * unitMultiplier * 10;
            info.PhysicalHeightMm = info.PhysicalMaxY * unitMultiplier * 10;
        }
        else // 0x13为英寸单位
        {
            info.PhysicalWidthMm = info.PhysicalMaxX * unitMultiplier * 25.4;
            info.PhysicalHeightMm = info.PhysicalMaxY * unitMultiplier * 25.4;
        }

        // 计算每毫米的点数（DPMM）
        if (info.PhysicalWidthMm > 0 && info.PhysicalHeightMm > 0)
        {
            info.TouchpadDpmmX = (float)(info.LogicalMaxX / info.PhysicalWidthMm);
            info.TouchpadDpmmY = (float)(info.LogicalMaxY / info.PhysicalHeightMm);
        }
    }

    private static TouchpadInfo CreateDefaultTouchpadInfo()
    {
        // 创建默认的触摸板信息（基于用户的1600x1200坐标系统）
        var info = new TouchpadInfo
        {
            PhysicalMaxX = 1200,
            PhysicalMaxY = 700,
            LogicalMaxX = 1600,      // 匹配用户的实际坐标范围
            LogicalMaxY = 1200,      // 匹配用户的实际坐标范围
            UnitExponent = -2,
            Unit = 0x11,
            PhysicalWidthMm = 120.0, // 12cm
            PhysicalHeightMm = 70.0,  // 7cm
            TouchpadDpmmX = 13.3f,    // 1600/120 ≈ 13.3点/毫米
            TouchpadDpmmY = 17.1f     // 1200/70 ≈ 17.1点/毫米
        };

        Logger.Log($"TouchpadInfoDetector: Using default touchpad info optimized for 1600x1200 - {info}");
        return info;
    }

    // Win32 API导入
    #region Win32 API

    [DllImport("User32", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [Out] RAWINPUTDEVICELIST[] pRawInputDeviceList,
        ref uint puiNumDevices,
        uint cbSize);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr hDevice,
        uint uiCommand,
        ref RID_DEVICE_INFO pData,
        ref uint pcbSize);

    [DllImport("Hid.dll", SetLastError = true)]
    private static extern uint HidP_GetCaps(
        IntPtr PreparsedData,
        out HIDP_CAPS Capabilities);

    [DllImport("Hid.dll", CharSet = CharSet.Auto)]
    private static extern uint HidP_GetValueCaps(
        HIDP_REPORT_TYPE ReportType,
        [Out] HIDP_VALUE_CAPS[] ValueCaps,
        ref ushort ValueCapsLength,
        IntPtr PreparsedData);

    // 常量
    private const uint RIM_TYPEHID = 2;
    private const uint RIDI_DEVICEINFO = 0x2000000b;
    private const uint RIDI_PREPARSEDDATA = 0x20000005;
    private const uint HIDP_STATUS_SUCCESS = 0x00110000;

    // 结构体定义
    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICELIST
    {
        public readonly IntPtr hDevice;
        public readonly uint dwType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO
    {
        public uint cbSize;
        public readonly uint dwType;
        public readonly RID_DEVICE_INFO_HID hid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RID_DEVICE_INFO_HID
    {
        public readonly uint dwVendorId;
        public readonly uint dwProductId;
        public readonly uint dwVersionNumber;
        public readonly ushort usUsagePage;
        public readonly ushort usUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public readonly ushort Usage;
        public readonly ushort UsagePage;
        public readonly ushort InputReportByteLength;
        public readonly ushort OutputReportByteLength;
        public readonly ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public readonly ushort[] Reserved;

        public readonly ushort NumberLinkCollectionNodes;
        public readonly ushort NumberInputButtonCaps;
        public readonly ushort NumberInputValueCaps;
        public readonly ushort NumberInputDataIndices;
        public readonly ushort NumberOutputButtonCaps;
        public readonly ushort NumberOutputValueCaps;
        public readonly ushort NumberOutputDataIndices;
        public readonly ushort NumberFeatureButtonCaps;
        public readonly ushort NumberFeatureValueCaps;
        public readonly ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_VALUE_CAPS
    {
        public readonly ushort UsagePage;
        public readonly byte ReportID;
        [MarshalAs(UnmanagedType.U1)] public readonly bool IsAlias;
        public readonly ushort BitField;
        public readonly ushort LinkCollection;
        public readonly ushort LinkUsage;
        public readonly ushort LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public readonly bool IsRange;
        [MarshalAs(UnmanagedType.U1)] public readonly bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)] public readonly bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)] public readonly bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)] public readonly bool HasNull;
        public readonly byte Reserved;
        public readonly ushort BitSize;
        public readonly ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public readonly ushort[] Reserved2;
        public readonly uint UnitsExp;
        public readonly uint Units;
        public readonly int LogicalMin;
        public readonly int LogicalMax;
        public readonly int PhysicalMin;
        public readonly int PhysicalMax;
        public readonly ushort UsageMin;
        public readonly ushort UsageMax;
        public readonly ushort StringMin;
        public readonly ushort StringMax;
        public readonly ushort DesignatorMin;
        public readonly ushort DesignatorMax;
        public readonly ushort DataIndexMin;
        public readonly ushort DataIndexMax;

        public ushort Usage => UsageMin;
        public ushort StringIndex => StringMin;
        public ushort DesignatorIndex => DesignatorMin;
        public ushort DataIndex => DataIndexMin;
    }

    private enum HIDP_REPORT_TYPE
    {
        HidP_Input,
        HidP_Output,
        HidP_Feature
    }

    #endregion
}