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

/// <summary>
/// Covers the SharedOptions.ApplyToSettings wiring for the two options added alongside the
/// pHash-quorum / matching-parallelism work — the same option→Settings seam that shipped the
/// #804 crash when a command forgot to register an option and ApplyToSettings read default(T).
/// </summary>
public class SharedOptionsApplyTests {

	static Settings Apply(System.CommandLine.Command cmd, params string[] args) {
		// No culture pinning: SharedOptions' numeric options parse invariantly by design, so
		// "0.8" is 0.8 on every host (see NumericOptions_ParseInvariantly_UnderCommaDecimalLocale).
		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, cmd.Parse(args));
		return settings;
	}

	// ---- --phash-sample-ratio ----

	[Fact]
	public void Scan_WithoutPhashSampleRatio_UsesDocumentedDefault() {
		// Registered on 'scan' with a 0.6 DefaultValueFactory — omitting it yields 0.6.
		Assert.Equal(0.6f, Apply(ScanCommand.Build()).PHashRequiredMatchingSampleRatio);
	}

	[Fact]
	public void Compare_WithoutPhashSampleRatio_UsesDocumentedDefault() {
		// 'compare' does NOT register the option; ApplyToSettings must fall back to 0.6 (its
		// GetResult is null), not leave 0 — otherwise pHash compares would demand only 1 sample.
		Assert.Equal(0.6f, Apply(CompareCommand.Build()).PHashRequiredMatchingSampleRatio);
	}

	[Fact]
	public void Scan_WithPhashSampleRatio_HonorsValue() {
		Assert.Equal(0.8f, Apply(ScanCommand.Build(), "--phash-sample-ratio", "0.8").PHashRequiredMatchingSampleRatio, 3);
	}

	[Theory]
	[InlineData("0", 0.01f)]     // explicit 0 clamps to the 0.01 minimum, NOT the 0.6 "unset" default
	[InlineData("0.005", 0.01f)] // below minimum clamps up
	[InlineData("1", 1f)]        // maximum
	[InlineData("5", 1f)]        // above maximum clamps down
	public void Scan_PhashSampleRatio_IsClampedToRange(string value, float expected) {
		Assert.Equal(expected, Apply(ScanCommand.Build(), "--phash-sample-ratio", value).PHashRequiredMatchingSampleRatio, 3);
	}

	// ---- --matching-parallelism ----

	[Fact]
	public void Compare_WithoutMatchingParallelism_DefaultsToAuto() {
		// 0 is the valid "automatic CPU-headroom cap" sentinel resolved later by the engine.
		Assert.Equal(0, Apply(CompareCommand.Build()).MatchingMaxDegreeOfParallelism);
	}

	[Theory]
	[InlineData("4", 4)]
	[InlineData("-1", -1)]
	public void MatchingParallelism_HonorsValue(string value, int expected) {
		Assert.Equal(expected, Apply(ScanCommand.Build(), "--matching-parallelism", value).MatchingMaxDegreeOfParallelism);
		Assert.Equal(expected, Apply(CompareCommand.Build(), "--matching-parallelism", value).MatchingMaxDegreeOfParallelism);
	}

	// ---- locale-independent numeric parsing ----

	[Fact]
	public void NumericOptions_ParseInvariantly_UnderCommaDecimalLocale() {
		// Regression: CLI numeric options must accept the invariant "0.8"/"95.5" form on every
		// OS locale. Before the invariant-culture parsers, a comma-decimal host (de-DE) read the
		// "." as a group separator, so "0.8" became 8 and "95.5" became 955.
		var prev = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");
		try {
			var settings = Apply(ScanCommand.Build(),
				"--phash-sample-ratio", "0.8", "--percent", "95.5", "--partial-clip-min-ratio", "0.25");
			Assert.Equal(0.8f, settings.PHashRequiredMatchingSampleRatio, 3);
			Assert.Equal(95.5f, settings.Percent, 3);
			Assert.Equal(0.25, settings.PartialClipMinRatio, 3);
		}
		finally {
			CultureInfo.CurrentCulture = prev;
		}
	}
}
