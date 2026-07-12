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

using System.Text.Json.Serialization;

namespace VDF.GUI.Data {
	/// <summary>Named after what the user wants to FIND, not after algorithms (locked decision 8).</summary>
	public enum ScanProfile {
		/// <summary>Copies, renames and re-encodes of the same video or photo. Fastest.</summary>
		ExactAndNear,
		/// <summary>Also finds crops, watermarks, flips and quality changes. The default.</summary>
		EditedAndAltered,
		/// <summary>Everything above plus AI matching for re-edited copies and clips cut out
		/// of longer videos — visual only, no audio decoding (maintainer decision 2026-07-12).</summary>
		AiScan,
		/// <summary>Everything above plus audio-fingerprint clip matching — the most
		/// thorough combination, and the slowest first scan.</summary>
		DeepClean,
		/// <summary>The user's own knob values, untouched.</summary>
		Custom,
	}

	/// <summary>The settings a profile manages. Everything else stays the user's business.</summary>
	public sealed class ScanKnobs {
		[JsonInclude] public float Percent { get; set; }
		[JsonInclude] public bool CompareHorizontallyFlipped { get; set; }
		[JsonInclude] public bool IgnoreBlackPixels { get; set; }
		[JsonInclude] public bool IgnoreWhitePixels { get; set; }
		[JsonInclude] public bool EnablePartialClipDetection { get; set; }
		[JsonInclude] public bool UseAiMatching { get; set; }
		[JsonInclude] public bool EnableAiPartialDetection { get; set; }
	}

	/// <summary>
	/// Maps scan profiles onto the managed settings knobs. The active profile is always
	/// DERIVED from the current knob values — editing any managed setting therefore
	/// switches the selection to Custom automatically, and nothing is ever lost: leaving
	/// a custom state snapshots the knobs so selecting Custom later restores them.
	/// Deliberately NOT managed: UsePHash (changing the algorithm invalidates the
	/// fingerprint database) and every filter/performance/database setting.
	/// </summary>
	internal static class ScanProfileMapper {

		internal static readonly ScanKnobs ExactAndNear = new() {
			Percent = 98f,
			CompareHorizontallyFlipped = false,
			IgnoreBlackPixels = false,
			IgnoreWhitePixels = false,
			EnablePartialClipDetection = false,
			UseAiMatching = false,
			EnableAiPartialDetection = false,
		};
		internal static readonly ScanKnobs EditedAndAltered = new() {
			Percent = 92f,
			CompareHorizontallyFlipped = true,
			IgnoreBlackPixels = true,
			IgnoreWhitePixels = true,
			EnablePartialClipDetection = false,
			UseAiMatching = false,
			EnableAiPartialDetection = false,
		};
		internal static readonly ScanKnobs AiScan = new() {
			Percent = 92f,
			CompareHorizontallyFlipped = true,
			IgnoreBlackPixels = true,
			IgnoreWhitePixels = true,
			// Deliberately NO audio pass: the AI partial pass covers clips visually
			// without decoding every file's full audio track.
			EnablePartialClipDetection = false,
			UseAiMatching = true,
			EnableAiPartialDetection = true,
		};
		internal static readonly ScanKnobs DeepClean = new() {
			Percent = 92f,
			CompareHorizontallyFlipped = true,
			IgnoreBlackPixels = true,
			IgnoreWhitePixels = true,
			EnablePartialClipDetection = true,
			UseAiMatching = true,
			EnableAiPartialDetection = true,
		};

		internal static ScanKnobs? BundleFor(ScanProfile profile) => profile switch {
			ScanProfile.ExactAndNear => ExactAndNear,
			ScanProfile.EditedAndAltered => EditedAndAltered,
			ScanProfile.AiScan => AiScan,
			ScanProfile.DeepClean => DeepClean,
			_ => null,
		};

		internal static ScanKnobs Capture(SettingsFile settings) => new() {
			Percent = settings.Percent,
			CompareHorizontallyFlipped = settings.CompareHorizontallyFlipped,
			IgnoreBlackPixels = settings.IgnoreBlackPixels,
			IgnoreWhitePixels = settings.IgnoreWhitePixels,
			EnablePartialClipDetection = settings.EnablePartialClipDetection,
			UseAiMatching = settings.UseAiMatching,
			EnableAiPartialDetection = settings.EnableAiPartialDetection,
		};

		internal static bool Matches(SettingsFile settings, ScanKnobs knobs) =>
			settings.Percent == knobs.Percent &&
			settings.CompareHorizontallyFlipped == knobs.CompareHorizontallyFlipped &&
			settings.IgnoreBlackPixels == knobs.IgnoreBlackPixels &&
			settings.IgnoreWhitePixels == knobs.IgnoreWhitePixels &&
			settings.EnablePartialClipDetection == knobs.EnablePartialClipDetection &&
			settings.UseAiMatching == knobs.UseAiMatching &&
			settings.EnableAiPartialDetection == knobs.EnableAiPartialDetection;

		/// <summary>The profile the current knob values correspond to; Custom when none match.</summary>
		internal static ScanProfile Detect(SettingsFile settings) =>
			Matches(settings, ExactAndNear) ? ScanProfile.ExactAndNear :
			Matches(settings, DeepClean) ? ScanProfile.DeepClean :
			Matches(settings, AiScan) ? ScanProfile.AiScan :
			Matches(settings, EditedAndAltered) ? ScanProfile.EditedAndAltered :
			ScanProfile.Custom;

		/// <summary>
		/// Applies a profile's bundle. Selecting Custom restores the snapshot taken when
		/// the user last left a custom state (no-op when there is none).
		/// </summary>
		internal static void Apply(ScanProfile profile, SettingsFile settings) {
			if (profile == ScanProfile.Custom) {
				if (settings.CustomScanKnobs is ScanKnobs backup)
					ApplyKnobs(backup, settings);
				return;
			}
			// Leaving a custom state: remember the expert's values so nothing is lost.
			if (Detect(settings) == ScanProfile.Custom)
				settings.CustomScanKnobs = Capture(settings);
			ApplyKnobs(BundleFor(profile)!, settings);
		}

		static void ApplyKnobs(ScanKnobs knobs, SettingsFile settings) {
			settings.Percent = knobs.Percent;
			settings.CompareHorizontallyFlipped = knobs.CompareHorizontallyFlipped;
			settings.IgnoreBlackPixels = knobs.IgnoreBlackPixels;
			settings.IgnoreWhitePixels = knobs.IgnoreWhitePixels;
			settings.EnablePartialClipDetection = knobs.EnablePartialClipDetection;
			settings.UseAiMatching = knobs.UseAiMatching;
			settings.EnableAiPartialDetection = knobs.EnableAiPartialDetection;
		}
	}
}
