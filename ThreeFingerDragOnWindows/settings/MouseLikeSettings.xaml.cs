using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ThreeFingerDragOnWindows.utils;

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

        // 加载开关状态
        EnableMouseLikeToggle.IsOn = engine.IsEnabled;

        // 加载设置参数
        var settings = engine.Settings;
        SensitivitySlider.Value = settings.MouseSensitivity;
        ThumbScaleSlider.Value = settings.ThumbScale;
        JitterThresholdSlider.Value = settings.JitterOffset;

        // 更新显示文本
        SensitivityValueText.Text = settings.MouseSensitivity.ToString("F1");
        ThumbScaleValueText.Text = settings.ThumbScale.ToString("F1");
        JitterThresholdValueText.Text = settings.JitterOffset.ToString("F0");
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
    /// 保存设置到配置文件
    /// </summary>
    private void SaveSettings()
    {
        // TODO: 将设置保存到SettingsData
        // 暂时先记录日志
        var engine = GetMouseLikeEngine();
        if (engine?.Settings != null)
        {
            Logger.Log($"MouseLike settings - Enabled: {engine.IsEnabled}, " +
                      $"Sensitivity: {engine.Settings.MouseSensitivity}, " +
                      $"ThumbScale: {engine.Settings.ThumbScale}, " +
                      $"JitterOffset: {engine.Settings.JitterOffset}");
        }
    }
}