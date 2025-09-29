# MouseLike 参数调节指南

## FingerTracker 常量
- `HoldDistanceFactor`：控制左右键手指相对指针的最小水平距离倍率。值越小，手指可以更靠近仍保持按键；值越大，可降低误触，但需要更大间距。
- `HoldDistanceMinimum`：当 `FingerMinDistance` 很小时时的保底距离（像素）。调低可以提升贴近拖动的成功率；调高能避免近距离误判。
- `ScrollSeparationFactor`：判定双指滚动时允许的最大水平距离倍率。减小可严格要求垂直滑动，降低误触左右键；增大可容忍轻微偏斜，但过大可能放宽过头。
- `ScrollSeparationMinimum`：配合上项使用，限制双指滚动的最小水平间隔（像素），避免因为倍率过小导致阈值过低。

## SettingsData 可调属性
- `FingerMinDistance` / `FingerMaxDistance`：左右键候选的距离范围。减小 `Min` 能让更近的手指参与按键；增大 `Max` 可保证距离拉开时仍保持按下。
- `FingerVerticalTolerance`：左右键允许的上下偏移。调低可减少斜向移动被识别为按键；调高有助于拖动时避免误判松开。
- `JitterOffset`：抖动过滤阈值。适度调大可以抵消轻微抖动导致的释放；过大可能让细微拖动不生效。

## 调整建议
1. 左键拖动容易断开：先小幅降低 `HoldDistanceFactor` 或 `HoldDistanceMinimum`，必要时再下调 `FingerMinDistance`。
2. 双指滚动被误判为左右键：逐步提高 `ScrollSeparationFactor` 或 `ScrollSeparationMinimum`，如仍存在误判再适当增大 `FingerVerticalTolerance`。
3. 左右键过于敏感：反向调大 `HoldDistanceFactor` 或减小 `FingerVerticalTolerance`，同时保持滚动相关参数不过度放宽。

每次只调一组参数，并结合日志中的 `AssignButtons` / `IsLikelyScroll` 输出观察效果，便于回退和定位。
