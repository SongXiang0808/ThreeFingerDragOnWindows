using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ThreeFingerDragOnWindows.utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace ThreeFingerDragOnWindows.settings;

public sealed partial class MouseLikeSettings : Page
{
    public MouseLikeSettings()
    {
        this.InitializeComponent();
        LoadSettings();
        UpdateSettingsPanelVisibility();
        UpdateStatusText();
    }

    /// <summary>
    /// 获取MouseLike手势引擎实例
    /// </summary>
    private mouselike.MouseLikeGestureEngine GetMouseLikeEngine()
    {
        // 从App实例获取HandlerWindow，然后获取ContactsManager和MouseLikeGestureEngine
        return App.Instance?.HandlerWindow?.GetContactsManager()?.MouseLikeGestureEngine;
    }

    /// <summary>
    /// 加载当前设置
    /// </summary>
    private void LoadSettings()
    {
        var engine = GetMouseLikeEngine();
        if (engine == null) return;

        // 从SettingsData加载保存的设置
        var settingsData = App.SettingsData;

        // 设置引擎状态
        engine.IsEnabled = settingsData.MouseLikeModeEnabled;
        engine.UpdateSettings(settingsData.MouseLikeSensitivity, settingsData.MouseLikeThumbScale);
        engine.Settings.JitterOffset = settingsData.MouseLikeJitterOffset;

        // 更新UI控件
        EnableMouseLikeToggle.IsOn = engine.IsEnabled;
        SensitivitySlider.Value = engine.Settings.MouseSensitivity;
        ThumbScaleSlider.Value = engine.Settings.ThumbScale;
        JitterThresholdSlider.Value = engine.Settings.JitterOffset;

        // 更新显示文本
        SensitivityValueText.Text = engine.Settings.MouseSensitivity.ToString("F1");
        ThumbScaleValueText.Text = engine.Settings.ThumbScale.ToString("F1");
        JitterThresholdValueText.Text = engine.Settings.JitterOffset.ToString("F0");

        Logger.Log($"MouseLike settings loaded - Enabled: {engine.IsEnabled}");
    }

    /// <summary>
    /// 更新设置面板可见性
    /// </summary>
    private void UpdateSettingsPanelVisibility()
    {
        SettingsPanel.Visibility = EnableMouseLikeToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 更新状态文本
    /// </summary>
    private void UpdateStatusText()
    {
        if (EnableMouseLikeToggle.IsOn)
        {
            StatusText.Text = "MouseLike 模式已启用 - 触摸板手势已激活";
        }
        else
        {
            StatusText.Text = "MouseLike 模式已禁用 - 使用标准三指拖拽";
        }
    }

    /// <summary>
    /// MouseLike模式开关切换事件
    /// </summary>
    private void EnableMouseLikeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var engine = GetMouseLikeEngine();
        if (engine == null) return;

        bool isEnabled = EnableMouseLikeToggle.IsOn;
        engine.IsEnabled = isEnabled;

        UpdateSettingsPanelVisibility();
        UpdateStatusText();

        Logger.Log($"MouseLike mode {(isEnabled ? "enabled" : "disabled")} via settings");

        // 保存设置
        SaveSettings();
    }

    /// <summary>
    /// 鼠标敏感度滑块变化事件
    /// </summary>
    private void SensitivitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (SensitivityValueText == null) return;

        float sensitivity = (float)e.NewValue;
        SensitivityValueText.Text = sensitivity.ToString("F1");

        var engine = GetMouseLikeEngine();
        engine?.UpdateSettings(sensitivity: sensitivity);

        Logger.Log($"MouseLike sensitivity updated to {sensitivity}");
        SaveSettings();
    }

    /// <summary>
    /// 手指尺寸缩放滑块变化事件
    /// </summary>
    private void ThumbScaleSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (ThumbScaleValueText == null) return;

        float thumbScale = (float)e.NewValue;
        ThumbScaleValueText.Text = thumbScale.ToString("F1");

        var engine = GetMouseLikeEngine();
        engine?.UpdateSettings(thumbScale: thumbScale);

        Logger.Log($"MouseLike thumb scale updated to {thumbScale}");
        SaveSettings();
    }

    /// <summary>
    /// 抖动阈值滑块变化事件
    /// </summary>
    private void JitterThresholdSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (JitterThresholdValueText == null) return;

        float jitterOffset = (float)e.NewValue;
        JitterThresholdValueText.Text = jitterOffset.ToString("F0");

        var engine = GetMouseLikeEngine();
        if (engine?.Settings != null)
        {
            engine.Settings.JitterOffset = jitterOffset;
            Logger.Log($"MouseLike jitter offset updated to {jitterOffset}");
            SaveSettings();
        }
    }

    /// <summary>
    /// 重置设置按钮点击事件
    /// </summary>
    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // 重置为默认值
        SensitivitySlider.Value = 1.0f;
        ThumbScaleSlider.Value = 1.0f;
        JitterThresholdSlider.Value = 10.0f;

        var engine = GetMouseLikeEngine();
        if (engine?.Settings != null)
        {
            engine.Settings.MouseSensitivity = 1.0f;
            engine.Settings.ThumbScale = 1.0f;
            engine.Settings.JitterOffset = 10.0f;
        }

        Logger.Log("MouseLike settings reset to defaults");
        SaveSettings();
    }

    /// <summary>
    /// 显示日志文件位置按钮点击事件
    /// </summary>
    private void ShowLogFileButton_Click(object sender, RoutedEventArgs e)
    {
        string logFilePath = Logger.GetLogFilePath();
        Logger.Log($"Opening log file location: {logFilePath}");

        // 强制刷新日志确保所有内容都写入文件
        Logger.FlushNow();
        Logger.Log("Log file flushed to disk");

        try
        {
            // 等待一下让文件写入完成
            System.Threading.Thread.Sleep(100);

            // 打开日志文件所在文件夹
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{logFilePath}\"");
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to open log file location: {ex.Message}");
        }
    }

    /// <summary>
    /// 保存设置到配置文件
    /// </summary>
    private void SaveSettings()
    {
        var engine = GetMouseLikeEngine();
        if (engine?.Settings == null) return;

        // 保存到SettingsData
        var settingsData = App.SettingsData;
        settingsData.MouseLikeModeEnabled = engine.IsEnabled;
        settingsData.MouseLikeSensitivity = engine.Settings.MouseSensitivity;
        settingsData.MouseLikeThumbScale = engine.Settings.ThumbScale;
        settingsData.MouseLikeJitterOffset = engine.Settings.JitterOffset;

        // 触发设置保存
        App.SettingsData.save();

        Logger.Log($"MouseLike settings saved - Enabled: {engine.IsEnabled}, " +
                  $"Sensitivity: {engine.Settings.MouseSensitivity}, " +
                  $"ThumbScale: {engine.Settings.ThumbScale}, " +
                  $"JitterOffset: {engine.Settings.JitterOffset}");
    }
}