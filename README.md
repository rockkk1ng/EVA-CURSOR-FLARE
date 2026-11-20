# EVA-CURSOR-FLARE

**Shake to Find Your Pointer**

Shake your mouse to find your pointer with an EVA-style cross-shaped flare on Windows.

Windows 10 / 11 桌面小工具。双击 `EvaMouseLocator.exe` 后会先播放一次光爆，随后在通知区域常驻。使用系统自带的 .NET Framework 4.x，以当前用户身份运行。

## 使用

- 快速来回甩动鼠标约两趟。横向、纵向、斜向均可。连续摇动只播放一次，停一下再摇可以再次触发。
- 白色光点从指针尖端亮起，橙红色辉光沿横纵两个方向展开。移动鼠标时，十字交点跟随指针。多屏时，光束到达当前屏幕的边缘，所有屏幕一起轻微变暗。
- 双击托盘图标或再次运行程序可以预览光爆。右击托盘图标，取消勾选「摇晃触发」即可暂停，重新勾选即可恢复。「退出」会结束程序。如图标被系统折叠，可在任务栏的隐藏图标区域找到它。

## 实现

| 部件 | 职责 |
| --- | --- |
| `src/MouseShake.cs` | 通过消息窗口接收 Windows Raw Input。将位移投影到摇晃方向，用最小行程和限时内的三次折返识别手势。冷却和停顿判定抑制重复触发。 |
| `src/BurstOverlay.cs` | 每屏一个半透明遮罩，指针所在屏幕额外绘制两条窄光带。窗口支持点击穿透，显示时保留前台焦点。动画使用单调时钟，结束后停止计时器并释放窗口和绘图资源。 |
| `src/Program.cs` | 管理系统托盘、预览、暂停和退出。通过会话内的命名事件将重复启动转为对已有进程的预览请求。 |
| `src/app.manifest` | 声明逐显示器 DPI 感知，使指针位置和光束使用一致的屏幕坐标。 |

默认时间线为白色光点起亮，约 22 毫秒开始拉出光束，约 114 毫秒四端完全展开，到 620 毫秒收束。背景遮罩最大不透明度约 23%。`BurstPainter` 中的配色和时间参数控制光效，`ShakeDetector` 中的距离和时间阈值控制手感。

## 从源码构建

在 Windows PowerShell 中进入解压目录，运行以下命令。脚本调用 Windows 的 .NET Framework C# 编译器。

```powershell
.\build.ps1 -Test
```

成品为 `build\EvaMouseLocator.exe`。只构建程序时运行 `.\build.ps1`。

## 验证

- `GestureTests.cs` 使用模拟输入轨迹检查斜向摇晃、抖动、慢移、绕圈和连续触发等情况，并实际注册 Windows 原始输入设备。
- `FrameTests.cs` 使用生产绘制逻辑检查点亮、四端触边、中心收尖、亮度和淡出。可运行 `.\build\FrameTests.exe peak.png` 将峰值帧输出为图片。
- `DesktopTests.cs` 在独立 Windows 桌面创建真实窗口，检查特效期间的鼠标命中目标和前台焦点，覆盖重复触发、中途停止以及退出时的资源释放。运行该测试需要可用的 Windows 桌面会话。
