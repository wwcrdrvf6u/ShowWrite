<p align="center">
  <img width="16%" align="center" src="logo-SW.png" alt="logo">
</p>

# ShowWrite视频展台 2.1
## 下载
[Github Releases](https://github.com/wwcrdrvf6u/ShowWrite/releases/)
# 关于Show Write
## 我们的设计理念
**用户能改的，我们绝对不改**（指 让用户自己改源码）
## 版本号命名
为兼容希沃视频展台的原生启动器（**sweclauncher**），以便支持从侧边栏或中控菜单直接启动 **Show Write**，我们将应用的版本号格式调整为与希沃视频展台一致的风格。
**示例：**
```
EasiCamera_2.1.1.8888
```
**结构说明：**

| 部分   | 示例值          | 含义            |
| ---- | ------------ | ------------- |
| 前缀   | `EasiCamera` | 希沃视频展台的标识缩写   |
| 分隔符  | `_`          | 固定分隔符         |
| 主版本号 | `2.1.1`      | 应用自身的版本号      |
| 尾缀   | `.8888`      | 区分希沃官方版本的附加标识 |

保持与希沃原生版本号的兼容，又方便区分自研版本与官方版本。

## 此软件的运行库
### **.NET / WPF 基础库**
* `System` — 基本类型、事件、IO 等
* `System.IO` — 文件读写
* `System.Threading.Tasks` — 异步任务
* `System.Windows` — WPF 核心类（`Window`、`Application`、事件等）
* `System.Windows.Controls` — WPF 控件（`Button`、`InkCanvas`、`Popup` 等）
* `System.Windows.Ink` — WPF 墨迹绘制（笔迹数据、编辑模式）
* `System.Windows.Input` — 输入事件（鼠标、触控、键盘）
* `System.Windows.Media.Imaging` — 图像处理（`BitmapImage`、`BitmapSource` 等）
* `System.Drawing`（别名 `D`）— GDI+ 图像处理（`Bitmap`、`Point` 等）
### **WinForms 兼容库**
* `System.Windows.Forms`（别名 `WinForms`）
  * 用于颜色选择对话框（`ColorDialog`）
  * 用于文件保存对话框（`SaveFileDialog`）
### **第三方库**
* `ImageMagick` — 图像处理库（Magick.NET，支持图像转换、编辑等）

## 加入我们

<p>
  <img width="50%" align="center" src="讨论群.jpg" alt="tqq">
</p>


   
-（希沃视频展台）
