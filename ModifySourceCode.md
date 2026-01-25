# 如何修改我们的源代码
# 1. 开始
## 1.1 获取源代码
打开项目地址 https://github.com/wwcrdrvf6u/ShowWrite/ 

你会看到如图所示的界面。<img width="1467" height="760" alt="image" src="https://github.com/user-attachments/assets/b3fac228-e100-496f-80b2-b4428d48aa5c" />

点击那个**绿色的按钮“code↓”** 会显示如蓝框所示的菜单。点击**downloadZIP**，会开始下载源代码的压缩包。
此时你的浏览器会开始下载。请在下载完成后点击**打开文件夹**，以打开压缩包所在的目录，右键单击此文件，会显示快捷菜单，此时点击**解压到ShowWrite-main/**（注：showwrite-main是刚刚下载的文件的文件名）。

<img width="383" height="149" alt="image" src="https://github.com/user-attachments/assets/f387e017-c987-4a34-b69f-36a7410bfb7e" />

将文件解压缩到目录中，完成之后，双击解压完成的文件夹ShowWrite-main，来到子目录，并再次双击子目录下的文件夹ShowWrite-main，并再次双击Main文件夹。打开源代码目录，您应该会看到一些文件（如图）。

<img width="438" height="564" alt="image" src="https://github.com/user-attachments/assets/6a0bf237-4dfc-4748-9731-8b35f219bfc0" />

至此，恭喜您成功得到了showwrite软件的源代码



## 1.2 部署开发环境
 
**适用于：Visual Studio 2022（Windows 11/10）、Visual Studio 2019（Windows 7）**  
**目标框架：.NET 8（Win11/10）、.NET 5（Win7）**

---

### 概述

- **Visual Studio 2022**：适用于 Windows 11 和 Windows 10，支持 .NET 8。
- **Visual Studio 2019**：适用于 Windows 7（需满足系统要求），支持 .NET 5。

> ⚠️ 注意：  
> - **.NET 8 仅支持 Windows 10 版本 1809 及更高版本、Windows 11**。  
> - **Windows 7 已于 2020 年 1 月终止主流支持，.NET 8 官方不支持 Windows 7**。  
> - **.NET 5 是最后一个支持 Windows 7 的 .NET 版本**（需安装 KB2533623 等更新）。

---

###  系统要求

#### 2.1 Windows 11 / Windows 10（用于 .NET 8 + VS2022）

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 11（21H2 或更高）<br>Windows 10（版本 1809 或更高，建议 22H2） |
| 处理器 | 1.8 GHz 或更快（双核推荐） |
| 内存 | ≥ 4 GB（建议 8 GB 或以上） |
| 硬盘空间 | ≥ 20 GB 可用空间（VS2022 安装约需 10–20 GB） |
| 其他 | .NET Desktop Runtime（可选，但建议安装）<br>启用 .NET Framework 3.5（部分旧项目依赖） |

#### 2.2 Windows 7（用于 .NET 5 + VS2019）

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 7 SP1（必须安装 Service Pack 1） |
| 必需更新 | [KB2533623](https://support.microsoft.com/en-us/topic/microsoft-security-advisory-insecure-library-loading-could-allow-remote-code-execution-486ea436-2d47-8a3d-5cda-1c5f4c7d0d0e)（TLS 1.2 支持）<br>[KB3063858](https://support.microsoft.com/en-us/topic/update-for-universal-c-runtime-in-windows-c0514201-7fe6-95a3-b0a5-287930f3560c)（Universal C Runtime） |
| 处理器 | 1.6 GHz 或更快 |
| 内存 | ≥ 2 GB（建议 4 GB） |
| 硬盘空间 | ≥ 10 GB 可用空间 |
| 其他 | 启用 .NET Framework 3.5（控制面板 → 程序 → 启用或关闭 Windows 功能） |

> ✅ **验证 Windows 7 更新**：  
> 打开命令提示符，运行：
> ```cmd
> wmic qfe list | findstr "2533623"
> wmic qfe list | findstr "3063858"
> ```
> 若无输出，需手动安装上述更新。

---

### 安装 Visual Studio

#### 安装 Visual Studio 2022（Windows 11/10）

##### 步骤 1：下载安装程序
- 访问 [Visual Studio 官网](https://visualstudio.microsoft.com/zh-hans/vs/)
- 选择 **Community（免费）**、Professional 或 Enterprise
- 点击“下载 Visual Studio Installer”

##### 步骤 2：运行安装程序并选择工作负载
1. 启动 `vs_community.exe`（或其他版本）
2. 在“工作负载”选项卡中勾选：
   - ✅ **.NET 桌面开发**（包含 .NET SDK、WinForms、WPF）
3. 在“单个组件”选项卡中确保包含：
   - .NET 8 SDK（通常自动包含）
   - .NET Framework 4.8 Targeting Pack
   - Git for Windows
   - NuGet 包管理器

##### 步骤 3：安装
- 点击“安装”，等待完成（约 20–60 分钟）
- 安装完成后重启系统（建议）

---

#### 安装 Visual Studio 2019（Windows 7）

> ⚠️ 注意：VS2019 最终版本为 16.11。


##### 步骤 1：下载 VS2019
- VS 2017: https://aka.ms/vs/15/release/vs_community.exe
- VS 2019: https://aka.ms/vs/16/release/vs_community.exe
- 下载 **Visual Studio 2019 version 16.11**（最后一个支持 Windows 7 的版本）

##### 步骤 2：安装
1. 运行 `vs_community.exe`
2. 选择工作负载：
   - ✅ **.NET 桌面开发**
   - ✅ **ASP.NET 和 Web 开发**
3. 在“单个组件”中确保包含：
   - .NET 5 SDK（可能需手动勾选）
   - .NET Framework 4.7.2 或 4.8 开发工具
   - Windows 7 SDK（可选，用于兼容性）

> 💡 提示：若安装器未列出 .NET 5 SDK，可后续单独安装。

##### 步骤 3：完成安装
- 安装完成后重启系统


---

**附录：版本兼容性速查表**

| 组件 | Windows 11 | Windows 10 | Windows 7 SP1 |
|------|------------|------------|----------------|
| Visual Studio | 2022       | 2022       | 2019 (≤16.11)  |
| .NET SDK      | 8.0        | 8.0        | 5.0 (最高)     |
| .NET Framework| 4.8.1      | 4.8.1      | 4.8            |
| 支持状态      | 完全支持   | 完全支持   | 已终止支持     |
