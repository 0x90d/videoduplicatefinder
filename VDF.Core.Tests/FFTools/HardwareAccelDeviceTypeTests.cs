// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using FFmpeg.AutoGen;
using VDF.Core.FFTools;

namespace VDF.Core.Tests.FFTools;

/// <summary>
/// Pins the native-binding hardware-device mapping, in particular the #799 guard:
/// Vulkan must never reach the native FFmpeg binding (it segfaults the whole process
/// on some drivers), so it is downgraded to software decoding here. Other modes map
/// straight through.
/// </summary>
public class HardwareAccelDeviceTypeTests {
	[Fact]
	public void Vulkan_DowngradesToSoftwareForNativeBinding() {
		FfmpegEngine.HardwareAccelerationMode = FFHardwareAccelerationMode.vulkan;
		Assert.Equal(AVHWDeviceType.AV_HWDEVICE_TYPE_NONE, FfmpegEngine.GetConfiguredHardwareDeviceType());
	}

	[Theory]
	[InlineData(FFHardwareAccelerationMode.cuda, AVHWDeviceType.AV_HWDEVICE_TYPE_CUDA)]
	[InlineData(FFHardwareAccelerationMode.vaapi, AVHWDeviceType.AV_HWDEVICE_TYPE_VAAPI)]
	[InlineData(FFHardwareAccelerationMode.qsv, AVHWDeviceType.AV_HWDEVICE_TYPE_QSV)]
	[InlineData(FFHardwareAccelerationMode.none, AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)]
	public void OtherModes_MapThrough(FFHardwareAccelerationMode mode, AVHWDeviceType expected) {
		FfmpegEngine.HardwareAccelerationMode = mode;
		Assert.Equal(expected, FfmpegEngine.GetConfiguredHardwareDeviceType());
	}
}
