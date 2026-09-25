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

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VDF.GUI.Data;
using VDF.GUI.ViewModels;
using VDF.GUI.Views;

namespace VDF.GUI.HeadlessTests;

/// <summary>#885/#895: criteria in the quality order dialog can be switched off, from the keyboard too.</summary>
public class QualityOrderDialogTests {
	[Fact]
	public Task SpaceOnACriterion_SwitchesItOff_AndTheResultSaysSo() => HeadlessUi.Run(() => {
		HeadlessUi.Shell();
		var dialog = new QualityOrderDialog();
		dialog.Show();
		HeadlessUi.Pump();
		try {
			var vm = dialog.ViewModel;
			var list = dialog.FindControl<ListBox>("CriteriaListBox")!;
			// The new criterion is there, switched off by default.
			var larger = vm.CriteriaOrder.Single(o => o.Key == "SizeLarger");
			Assert.False(larger.IsEnabled);
			Assert.Contains("SizeLarger", vm.Result.Disabled);

			var size = vm.CriteriaOrder.Single(o => o.Key == "Size");
			vm.SelectedItem = size;
			HeadlessUi.Pump();
			list.ContainerFromItem(size)!.Focus(NavigationMethod.Tab);
			HeadlessUi.Pump();
			dialog.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
			dialog.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
			HeadlessUi.Pump();

			Assert.False(size.IsEnabled);
			Assert.Contains("Size", vm.Result.Disabled);
			Assert.Equal(vm.CriteriaOrder.Count, vm.Result.Order.Count); // the order keeps everything

			// The row says whether the criterion is used, since its box takes no focus.
			var container = list.ContainerFromItem(size)!;
			Assert.EndsWith(App.Lang["QualityOrderDialog.Ignored"], AutomationProperties.GetName(container));
			var box = container.GetVisualDescendants().OfType<CheckBox>().Single();
			Assert.False(box.IsChecked);
			Assert.False(box.Focusable);
		}
		finally {
			dialog.Hide();
			HeadlessUi.Pump();
		}
	});
}
