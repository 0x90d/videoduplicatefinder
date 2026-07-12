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

using VDF.CLI.Commands;
using VDF.Core;

namespace VDF.CLI.Tests.Commands;

/// <summary>
/// Regression tests for #804: the 'compare' command crashed with
/// "MaxDegreeOfParallelism ('0') must be a non-zero value" because it never
/// registered the --parallelism option, so ApplyToSettings read back default(int).
/// </summary>
public class CompareCommandOptionsTests {
	[Fact]
	public void Compare_WithoutParallelism_DefaultsToOne() {
		var cmd = CompareCommand.Build();
		var parse = cmd.Parse(new[] { "--percent", "95", "--include-images" });

		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, parse);

		Assert.True(settings.MaxDegreeOfParallelism >= 1,
			$"MaxDegreeOfParallelism must never be < 1 (was {settings.MaxDegreeOfParallelism}).");
	}

	[Fact]
	public void Compare_WithParallelism_HonorsValue() {
		var cmd = CompareCommand.Build();
		var parse = cmd.Parse(new[] { "--parallelism", "8" });

		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, parse);

		Assert.Equal(8, settings.MaxDegreeOfParallelism);
	}

	[Fact]
	public void Compare_WithNegativeParallelism_IsPreserved() {
		// -1 is a valid ParallelOptions value (unbounded); only 0 is illegal, so the
		// zero-sentinel remap must not clobber a deliberate -1.
		var cmd = CompareCommand.Build();
		var parse = cmd.Parse(new[] { "--parallelism", "-1" });

		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, parse);

		Assert.Equal(-1, settings.MaxDegreeOfParallelism);
	}

	[Fact]
	public void Compare_RegistersAndHonorsCompareTimeModes() {
		// Regression: 'compare' registered none of the matching-mode options, so the
		// documented scan-then-compare workflow silently lost --use-phash,
		// --partial-clip-detection and both AI passes.
		var cmd = CompareCommand.Build();
		var parse = cmd.Parse(new[] { "--use-phash", "--partial-clip-detection", "--ai-matching", "--ai-partial", "--ai-percent", "92" });
		Assert.Empty(parse.Errors);

		var settings = new Settings();
		SharedOptions.ApplyToSettings(settings, parse);

		Assert.True(settings.UsePHashing);
		Assert.True(settings.EnablePartialClipDetection);
		Assert.True(settings.UseAiMatching);
		Assert.True(settings.EnableAiPartialDetection);
		Assert.Equal(92f, settings.AiPercent, 3);
	}

	[Fact]
	public void ApplyToSettings_LeavesUnregisteredOptionsUntouched() {
		// The mechanism behind the class of bugs above: for an option the command never
		// registered, GetValue returns default(T) and the old unconditional assignments
		// force-reset preloaded settings (from a settings file or engine defaults).
		var bare = new System.CommandLine.Command("bare");
		bare.Options.Add(SharedOptions.Percent); // one registered option as control

		var settings = new Settings {
			UseAiMatching = true,
			EnableAiPartialDetection = true,
			UsePHashing = true,
			EnablePartialClipDetection = true,
			AiPercent = 92f,
			Threshhold = 7,
		};
		SharedOptions.ApplyToSettings(settings, bare.Parse(Array.Empty<string>()));

		Assert.True(settings.UseAiMatching);
		Assert.True(settings.EnableAiPartialDetection);
		Assert.True(settings.UsePHashing);
		Assert.True(settings.EnablePartialClipDetection);
		Assert.Equal(92f, settings.AiPercent, 3);
		Assert.Equal(7, settings.Threshhold);
		Assert.Equal(96f, settings.Percent, 3); // registered option still applies its default
	}
}
