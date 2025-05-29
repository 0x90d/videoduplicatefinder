using Avalonia.Platform.Storage;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VDF.Core;
using VDF.Core.Utils; // For Logger and DatabaseUtils (though DB not directly used here)
using VDF.GUI.Data;   // For SettingsFile
using VDF.GUI.Utils;  // For PickerDialogUtils

namespace VDF.GUI.ViewModels
{
    public class SegmentComparisonVM : ReactiveObject // Assuming ReactiveObject is the base like in MainWindowVM
    {
        private string _videoAPath;
        public string VideoAPath
        {
            get => _videoAPath;
            set => this.RaiseAndSetIfChanged(ref _videoAPath, value);
        }

        private string _videoBPath;
        public string VideoBPath
        {
            get => _videoBPath;
            set => this.RaiseAndSetIfChanged(ref _videoBPath, value);
        }

        // Segment A Definition
        private Core.SegmentDefinition.DefinitionMode _modeA = Core.SegmentDefinition.DefinitionMode.AbsoluteTime;
        public Core.SegmentDefinition.DefinitionMode ModeA
        {
            get => _modeA;
            set => this.RaiseAndSetIfChanged(ref _modeA, value);
        }

        private TimeSpan _absoluteStartTimeA = TimeSpan.FromSeconds(0);
        public TimeSpan AbsoluteStartTimeA
        {
            get => _absoluteStartTimeA;
            set => this.RaiseAndSetIfChanged(ref _absoluteStartTimeA, value);
        }

        private TimeSpan _absoluteEndTimeA = TimeSpan.FromSeconds(10);
        public TimeSpan AbsoluteEndTimeA
        {
            get => _absoluteEndTimeA;
            set => this.RaiseAndSetIfChanged(ref _absoluteEndTimeA, value);
        }

        private Core.SegmentDefinition.OffsetReference _startReferenceA = Core.SegmentDefinition.OffsetReference.FromStart;
        public Core.SegmentDefinition.OffsetReference StartReferenceA
        {
            get => _startReferenceA;
            set => this.RaiseAndSetIfChanged(ref _startReferenceA, value);
        }

        private TimeSpan _startOffsetA = TimeSpan.FromSeconds(0);
        public TimeSpan StartOffsetA
        {
            get => _startOffsetA;
            set => this.RaiseAndSetIfChanged(ref _startOffsetA, value);
        }

        private Core.SegmentDefinition.OffsetReference _endReferenceA = Core.SegmentDefinition.OffsetReference.FromEnd;
        public Core.SegmentDefinition.OffsetReference EndReferenceA
        {
            get => _endReferenceA;
            set => this.RaiseAndSetIfChanged(ref _endReferenceA, value);
        }

        private TimeSpan _endOffsetA = TimeSpan.FromSeconds(0);
        public TimeSpan EndOffsetA
        {
            get => _endOffsetA;
            set => this.RaiseAndSetIfChanged(ref _endOffsetA, value);
        }

        // Segment B Definition
        private Core.SegmentDefinition.DefinitionMode _modeB = Core.SegmentDefinition.DefinitionMode.AbsoluteTime;
        public Core.SegmentDefinition.DefinitionMode ModeB
        {
            get => _modeB;
            set => this.RaiseAndSetIfChanged(ref _modeB, value);
        }

        private TimeSpan _absoluteStartTimeB = TimeSpan.FromSeconds(0);
        public TimeSpan AbsoluteStartTimeB
        {
            get => _absoluteStartTimeB;
            set => this.RaiseAndSetIfChanged(ref _absoluteStartTimeB, value);
        }

        private TimeSpan _absoluteEndTimeB = TimeSpan.FromSeconds(10);
        public TimeSpan AbsoluteEndTimeB
        {
            get => _absoluteEndTimeB;
            set => this.RaiseAndSetIfChanged(ref _absoluteEndTimeB, value);
        }

        private Core.SegmentDefinition.OffsetReference _startReferenceB = Core.SegmentDefinition.OffsetReference.FromStart;
        public Core.SegmentDefinition.OffsetReference StartReferenceB
        {
            get => _startReferenceB;
            set => this.RaiseAndSetIfChanged(ref _startReferenceB, value);
        }

        private TimeSpan _startOffsetB = TimeSpan.FromSeconds(0);
        public TimeSpan StartOffsetB
        {
            get => _startOffsetB;
            set => this.RaiseAndSetIfChanged(ref _startOffsetB, value);
        }

        private Core.SegmentDefinition.OffsetReference _endReferenceB = Core.SegmentDefinition.OffsetReference.FromEnd;
        public Core.SegmentDefinition.OffsetReference EndReferenceB
        {
            get => _endReferenceB;
            set => this.RaiseAndSetIfChanged(ref _endReferenceB, value);
        }

        private TimeSpan _endOffsetB = TimeSpan.FromSeconds(0);
        public TimeSpan EndOffsetB
        {
            get => _endOffsetB;
            set => this.RaiseAndSetIfChanged(ref _endOffsetB, value);
        }

        // Comparison Parameters
        private int _numberOfThumbnails = 5;
        public int NumberOfThumbnails
        {
            get => _numberOfThumbnails;
            set => this.RaiseAndSetIfChanged(ref _numberOfThumbnails, value);
        }

        private Core.ComparisonParameters.ComparisonMethod _selectedComparisonMethod = Core.ComparisonParameters.ComparisonMethod.DirectSequenceMatch;
        public Core.ComparisonParameters.ComparisonMethod SelectedComparisonMethod
        {
            get => _selectedComparisonMethod;
            set => this.RaiseAndSetIfChanged(ref _selectedComparisonMethod, value);
        }

        // Results & Status
        private string _resultMessage;
        public string ResultMessage
        {
            get => _resultMessage;
            set => this.RaiseAndSetIfChanged(ref _resultMessage, value);
        }

        private float _similarityScoreVM;
        public float SimilarityScoreVM
        {
            get => _similarityScoreVM;
            set => this.RaiseAndSetIfChanged(ref _similarityScoreVM, value);
        }

        public ObservableCollection<string> MatchStartTimesInBVM { get; } = new ObservableCollection<string>();

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private string _busyText;
        public string BusyText
        {
            get => _busyText;
            set => this.RaiseAndSetIfChanged(ref _busyText, value);
        }

        // Enum Value Providers
        public IEnumerable<Core.SegmentDefinition.DefinitionMode> DefinitionModes => 
            Enum.GetValues(typeof(Core.SegmentDefinition.DefinitionMode)).Cast<Core.SegmentDefinition.DefinitionMode>();
        
        public IEnumerable<Core.SegmentDefinition.OffsetReference> OffsetReferences => 
            Enum.GetValues(typeof(Core.SegmentDefinition.OffsetReference)).Cast<Core.SegmentDefinition.OffsetReference>();

        public IEnumerable<Core.ComparisonParameters.ComparisonMethod> ComparisonMethods => 
            Enum.GetValues(typeof(Core.ComparisonParameters.ComparisonMethod)).Cast<Core.ComparisonParameters.ComparisonMethod>();

        // Commands
        public ICommand BrowseVideoACommand { get; }
        public ICommand BrowseVideoBCommand { get; }
        public ReactiveCommand<Unit, Unit> CompareSegmentsCommand { get; }

        public SegmentComparisonVM()
        {
            BrowseVideoACommand = ReactiveCommand.CreateFromTask(BrowseVideoA);
            BrowseVideoBCommand = ReactiveCommand.CreateFromTask(BrowseVideoB);
            CompareSegmentsCommand = ReactiveCommand.CreateFromTask(CompareSegments, this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy));
        }

        private async Task BrowseVideoA()
        {
            var path = await PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions { Title = "Select Video A" });
            if (!string.IsNullOrEmpty(path))
            {
                VideoAPath = path;
            }
        }

        private async Task BrowseVideoB()
        {
            var path = await PickerDialogUtils.OpenFilePicker(new FilePickerOpenOptions { Title = "Select Video B" });
            if (!string.IsNullOrEmpty(path))
            {
                VideoBPath = path;
            }
        }

        private async Task CompareSegments()
        {
            IsBusy = true;
            BusyText = "Comparing segments...";
            ResultMessage = string.Empty;
            SimilarityScoreVM = 0;
            MatchStartTimesInBVM.Clear();

            try
            {
                if (string.IsNullOrEmpty(VideoAPath) || string.IsNullOrEmpty(VideoBPath))
                {
                    ResultMessage = "Please select both Video A and Video B.";
                    return;
                }

                var segADef = new SegmentDefinition
                {
                    VideoPath = VideoAPath,
                    Mode = ModeA,
                    AbsoluteStartTime = AbsoluteStartTimeA,
                    AbsoluteEndTime = AbsoluteEndTimeA,
                    StartReference = StartReferenceA,
                    StartOffset = StartOffsetA,
                    EndReference = EndReferenceA,
                    EndOffset = EndOffsetA
                };

                var segBDef = new SegmentDefinition
                {
                    VideoPath = VideoBPath,
                    Mode = ModeB,
                    AbsoluteStartTime = AbsoluteStartTimeB,
                    AbsoluteEndTime = AbsoluteEndTimeB,
                    StartReference = StartReferenceB,
                    StartOffset = StartOffsetB,
                    EndReference = EndReferenceB,
                    EndOffset = EndOffsetB
                };

                var compParams = new ComparisonParameters
                {
                    NumberOfThumbnails = NumberOfThumbnails,
                    Method = SelectedComparisonMethod
                };

                // Use current application settings for comparison aspects like Percent, IgnorePixels
                var currentCoreSettings = new Core.Settings
                {
                    Percent = SettingsFile.Instance.Percent,
                    IgnoreBlackPixels = SettingsFile.Instance.IgnoreBlackPixels,
                    IgnoreWhitePixels = SettingsFile.Instance.IgnoreWhitePixels,
                    ExtendedFFToolsLogging = SettingsFile.Instance.ExtendedFFToolsLogging
                    // Other settings if relevant
                };

                var comparer = new SegmentComparer();

                SegmentComparisonResult coreResult = await Task.Run(() => 
                    comparer.CompareSegments(segADef, segBDef, compParams, currentCoreSettings));

                if (coreResult.IsSuccess)
                {
                    ResultMessage = coreResult.Message;
                    if (compParams.Method == Core.ComparisonParameters.ComparisonMethod.DirectSequenceMatch)
                    {
                        SimilarityScoreVM = coreResult.SimilarityScore;
                    }
                    else if (compParams.Method == Core.ComparisonParameters.ComparisonMethod.SearchASinB)
                    {
                        foreach (var ts in coreResult.MatchStartTimesInB)
                        {
                            MatchStartTimesInBVM.Add(ts.ToString(@"hh\:mm\:ss\.fff"));
                        }
                    }
                }
                else
                {
                    ResultMessage = $"Error: {coreResult.Message}";
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"Error during segment comparison: {ex}");
                ResultMessage = $"An unexpected error occurred: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
                BusyText = string.Empty;
            }
        }
    }
}
