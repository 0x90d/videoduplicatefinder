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

using System.Globalization;
using VDF.CLI.Commands;
using VDF.Core;

namespace VDF.CLI.Tests.Commands;

public class AiOptionsTests {

	static Settings Apply(params string[] args) {
		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, ScanCommand.Build().Parse(args));
		return settings;
	}

	[Fact]
	public void Defaults_AiIsOffWithDocumentedThresholds() {
		var settings = Apply();
		Assert.False(settings.UseAiMatching);
		Assert.False(settings.EnableAiPartialDetection);
		Assert.Equal(94f, settings.AiPercent);
		Assert.Equal(89f, settings.AiPartialHitPercent);
	}

	[Fact]
	public void Flags_EnableThePasses() {
		var settings = Apply("--ai-matching", "--ai-partial");
		Assert.True(settings.UseAiMatching);
		Assert.True(settings.EnableAiPartialDetection);
	}

	[Theory]
	[InlineData("92.5", 92.5f)]
	[InlineData("40", 50f)]   // clamps up to the minimum
	[InlineData("150", 100f)] // clamps down to the maximum
	public void AiPercent_HonorsAndClampsValue(string value, float expected) {
		Assert.Equal(expected, Apply("--ai-percent", value).AiPercent, 3);
	}

	[Fact]
	public void AiNumericOptions_ParseInvariantly_UnderCommaDecimalLocale() {
		// Same regression class as the 2026-07 invariant-culture batch: on de-DE a
		// culture-parsed "92.5" became 925 (dot read as group separator).
		var prev = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");
		try {
			var settings = Apply("--ai-percent", "92.5", "--ai-partial-hit-percent", "88.5");
			Assert.Equal(92.5f, settings.AiPercent, 3);
			Assert.Equal(88.5f, settings.AiPartialHitPercent, 3);
		}
		finally {
			CultureInfo.CurrentCulture = prev;
		}
	}
}
