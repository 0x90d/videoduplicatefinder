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

using VDF.GUI.Data;

namespace VDF.GUI.Tests {
	public class ScanProfileMapperTests {

		[Fact]
		public void FreshDefaults_AreTheRecommendedEditedProfile() {
			var settings = new SettingsFile();
			Assert.Equal(ScanProfile.EditedAndAltered, ScanProfileMapper.Detect(settings));
		}

		[Fact]
		public void Apply_SetsEveryManagedKnob() {
			var settings = new SettingsFile();
			ScanProfileMapper.Apply(ScanProfile.ExactAndNear, settings);

			Assert.Equal(98f, settings.Percent);
			Assert.False(settings.CompareHorizontallyFlipped);
			Assert.False(settings.IgnoreBlackPixels);
			Assert.False(settings.IgnoreWhitePixels);
			Assert.False(settings.EnablePartialClipDetection);
			Assert.Equal(ScanProfile.ExactAndNear, ScanProfileMapper.Detect(settings));
		}

		[Fact]
		public void DeepClean_IsEditedPlusPartialClips() {
			var settings = new SettingsFile();
			ScanProfileMapper.Apply(ScanProfile.DeepClean, settings);

			Assert.True(settings.EnablePartialClipDetection);
			Assert.Equal(ScanProfile.DeepClean, ScanProfileMapper.Detect(settings));
			settings.EnablePartialClipDetection = false;
			Assert.Equal(ScanProfile.EditedAndAltered, ScanProfileMapper.Detect(settings));
		}

		[Fact]
		public void EditingAnyManagedKnob_SwitchesDetectionToCustom() {
			var settings = new SettingsFile();
			ScanProfileMapper.Apply(ScanProfile.EditedAndAltered, settings);

			settings.Percent = 85f;
			Assert.Equal(ScanProfile.Custom, ScanProfileMapper.Detect(settings));
		}

		[Fact]
		public void LeavingCustom_SnapshotsKnobs_AndCustomRestoresThem() {
			var settings = new SettingsFile();
			// the expert's setup
			settings.Percent = 85f;
			settings.CompareHorizontallyFlipped = false;
			settings.IgnoreBlackPixels = false;
			settings.IgnoreWhitePixels = true;
			settings.EnablePartialClipDetection = true;
			Assert.Equal(ScanProfile.Custom, ScanProfileMapper.Detect(settings));

			ScanProfileMapper.Apply(ScanProfile.ExactAndNear, settings);
			Assert.Equal(ScanProfile.ExactAndNear, ScanProfileMapper.Detect(settings));
			Assert.NotNull(settings.CustomScanKnobs);

			ScanProfileMapper.Apply(ScanProfile.Custom, settings);
			Assert.Equal(85f, settings.Percent);
			Assert.False(settings.CompareHorizontallyFlipped);
			Assert.False(settings.IgnoreBlackPixels);
			Assert.True(settings.IgnoreWhitePixels);
			Assert.True(settings.EnablePartialClipDetection);
			Assert.Equal(ScanProfile.Custom, ScanProfileMapper.Detect(settings));
		}

		[Fact]
		public void SwitchingBetweenBundles_DoesNotOverwriteTheCustomSnapshot() {
			var settings = new SettingsFile();
			settings.Percent = 85f; // custom state
			ScanProfileMapper.Apply(ScanProfile.ExactAndNear, settings);
			ScanProfileMapper.Apply(ScanProfile.DeepClean, settings);

			ScanProfileMapper.Apply(ScanProfile.Custom, settings);
			Assert.Equal(85f, settings.Percent);
		}

		[Fact]
		public void ApplyCustom_WithoutSnapshot_LeavesSettingsUntouched() {
			var settings = new SettingsFile();
			ScanProfileMapper.Apply(ScanProfile.Custom, settings);
			Assert.Equal(ScanProfile.EditedAndAltered, ScanProfileMapper.Detect(settings));
		}
	}
}
