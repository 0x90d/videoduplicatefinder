# Video Duplicate Finder (CN) - 视频重复项查找工具 (汉化版)
> [!CAUTION]
> 1. 请 **<i>不要</i>** 在 [原始仓库（0x90d/videoduplicatefinder）](https://github.com/0x90d/videoduplicatefinder) 内提出任何与汉化相关的 Issue。所有汉化问题请在 本仓库 内提交 Issue 或 PR
> 
> 2. 本仓库仅进行 Video Duplicate Finder 的汉化，不对功能进行修改。所有功能问题请使用 **<i>英语</i>** 在 [原始仓库](https://github.com/0x90d/videoduplicatefinder) 内提交 issue
> 
> 3. 所有信息以原始仓库为准，本仓库仅供参考
>
> 4. 若你在其他平台分享推荐该软件，请在分享时携带以上警告信息，谢谢。

> [!TIP]
> This is a repository for the Chinese-localized version of Video Duplicate Finder. We use some Python scripts to assist us in translating the software. If you want to create other language versions of the translation, you can take a look [HERE -> (Chairowell/videoduplicatefinder-translator)](https://github.com/Chairowell/videoduplicatefinder-translator).

Video Duplicate Finder 是一款跨平台文件查重软件，用于在硬盘上查找重复的视频（和图像）文件。软件是基于画面相似度来查找重复的视频（和图像）文件。与其他重复项查找工具不同的是，它能找出分辨率、帧率不同甚至带有水印的重复文件。

## 功能
- 跨平台，支持 Windows、Linux 和 MacOS 的图形用户界面
- 基于相似度查找重复的 视频 / 图像
- 超快的初次扫描和二次扫描
- 可调用原生 FFmpeg 功能以获得更快的速度

## 下载
- [原始版本（0x90d/videoduplicatefinder）](https://github.com/0x90d/videoduplicatefinder/releases/latest)
- [汉化版本（Chairowell/videoduplicatefinder_CN）](https://github.com/Chairowell/videoduplicatefinder_CN/releases/latest)

## 需要安装 FFmpeg & FFprobe:

### Windows 用户:
我们推荐你使用完整的（GPL）共享版本。请从 https://ffmpeg.org/download.html 获取最新的安装包。如果你想使用本地ffmpeg绑定，你**必须**使用共享版本。

将 ffmpeg 和 ffprobe 解压到 VDF.GUI.dll 所在的同一目录，或解压到名为 `bin` 的子文件夹中。或者确保它们可以在 `PATH` 系统环境变量中找到

```DIR
App-win-x64
|
├─ ffmpeg.exe   # 放在这里
├─ ffprobe.exe  # 放在这里
|
├─ bin			# 注意：bin 文件夹需要手动创建
|  ├─ ffmpeg.exe	# 或者放在 bin 文件夹里
|  └─ ffprobe.exe	# 或者放在 bin 文件夹里
|
├─ VDF.Core.dll
├─ VDF.Core.pdb
├─ VDF.GUI.deps.json
├─ VDF.GUI.dll
├─ VDF.GUI.exe
├─ VDF.GUI.pdb
├─ VDF.GUI.runtimeconfig.json
├─ WindowsBase.dll
├─ ...
└─ de
   └─ DynamicExpresso.Core.resources.dll
```

### Linux 用户:
安装 ffmpeg:
```bash
sudo apt-get update
sudo apt-get install ffmpeg
```
在 VDF 文件夹中打开终端并执行 `./VDF.GUI`
你可能需要先设置执行权限 `sudo chmod 777 VDF.GUI`

### MacOS 用户:
使用 homebrew 安装 ffmpeg / ffprobe

在 VDF 文件夹中打开终端并执行 `./VDF.GUI`，或者如果你已安装 .NET，可以执行 `dotnet VDF.GUI.dll`

你可能会遇到权限错误。打开 Mac 的系统设置，进入 `隐私与安全`，然后选择 `开发者工具`。现在将 `终端` 添加到列表中。

如果进程立即被终止（显示类似 `zsh: killed` 的信息），二进制文件可能需要签名。运行 `codesign --force --sign - ./VDF.GUI` 然后重试。

## 截图
<img src="https://user-images.githubusercontent.com/46010672/129763067-8855a538-4a4f-4831-ac42-938eae9343bd.png" width="510">

## License - 许可证
Video Duplicate Finder is licensed under GPLv3

Video Duplicate Finder 采用 GPLv3 许可证

## 致谢 / 第三方组件
- [Avalonia](https://github.com/AvaloniaUI/Avalonia)
- [ActiPro Avalonia Controls (Free Edition)](https://github.com/Actipro/Avalonia-Controls)
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- [protobuf-net](https://github.com/protobuf-net/protobuf-net)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)

## 提交汉化翻译
如果发现汉化翻译错误或有更好的汉化翻译版本，你可以：
1. 直接在本仓库中提交 Issue ，由我们修改提交
2. 直接在本仓库中提交 PR ，由我们合并提交

> [!NOTE]
> 只有在存在更好的汉化翻译才会通过您的提交，否则我们可能会拒绝你的提交