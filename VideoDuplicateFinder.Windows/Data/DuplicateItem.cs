using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Imaging;
using VideoDuplicateFinderWindows.MVVM;


namespace VideoDuplicateFinderWindows.Data {
	public class DuplicateItemViewModel : ViewModelBase, IEquatable<DuplicateItemViewModel> {
		public DuplicateItemViewModel(DuplicateFinderEngine.Data.DuplicateItem file) {
			Path = file.Path;
			Folder = file.Folder;
			Duration = file.Duration;
			Thumbnail = Utils.JoinImages(file.Thumbnail);
			SizeLong = file.SizeLong;
			Size = file.Size;
			FrameSize = file.FrameSize;
			FrameSizeInt = file.FrameSizeInt;
			AudioChannel = file.AudioChannel;
			AudioFormat = file.AudioFormat;
			AudioSampleRate = file.AudioSampleRate;
			GroupId = file.GroupId;
			Fps = file.Fps;
			DateCreated = file.DateCreated;
			Format = file.Format;
			BitRateKbs = file.BitRateKbs;
			Similarity = file.Similarity;
			IsImage = file.IsImage;
			file.ThumbnailUpdated += () => {
				Thumbnail = Utils.JoinImages(file.Thumbnail);
				Application.Current.Dispatcher.BeginInvoke(new Action(() => {
					OnPropertyChanged(nameof(Thumbnail));
				}), System.Windows.Threading.DispatcherPriority.Background);
			};
		}


		[DisplayName("Path")]
		public string Path { get; }

		public BitmapImage Thumbnail { get; set; }

		public long SizeLong { get; }



		bool _SizeBest;
		public bool SizeBest {
			get => _SizeBest;
			set {
				if (value == _SizeBest) return;
				_SizeBest = value;
				OnPropertyChanged(nameof(SizeBest));
			}
		}

		[DisplayName("Size")]
		public string Size { get; }

		public float Similarity { get; }

		public string Folder { get; }

		bool _BitrateBest;
		public bool BitrateBest {
			get => _BitrateBest;
			set {
				if (value == _BitrateBest) return;
				_BitrateBest = value;
				OnPropertyChanged(nameof(BitrateBest));
			}
		}

		[DisplayName("Group Id")]
		public Guid GroupId { get; set; }
		[DisplayName("Duration")]
		public TimeSpan Duration { get; }

		bool _DurationBest;
		public bool DurationBest {
			get => _DurationBest;
			set {
				if (value == _DurationBest) return;
				_DurationBest = value;
				OnPropertyChanged(nameof(DurationBest));
			}
		}

		[DisplayName("Frame Size")]
		public string FrameSize { get; }
		public int FrameSizeInt { get; }

		bool _FrameSizeBest;
		public bool FrameSizeBest {
			get => _FrameSizeBest;
			set {
				if (value == _FrameSizeBest) return;
				_FrameSizeBest = value;
				OnPropertyChanged(nameof(FrameSizeBest));
			}
		}

		[DisplayName("Format")]
		public string Format { get; }



		[DisplayName("Audio Format")]
		public string AudioFormat { get; }


		[DisplayName("Audio Channel")]
		public string AudioChannel { get; }


		[DisplayName("Audio Sample Rate")]
		public int AudioSampleRate { get; }


		[DisplayName("BitRate Kbs")]
		public decimal BitRateKbs { get; }

		public bool IsImage { get; }

		[DisplayName("Fps")]
		public float Fps { get; }

		[DisplayName("Date Created")]
		public DateTime DateCreated { get; }

		private bool _Checked;
		public bool Checked {
			get => _Checked;
			set {
				if (value == _Checked) return;
				_Checked = value;
				OnPropertyChanged(nameof(Checked));
			}
		}

		public bool Equals(DuplicateItemViewModel other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return SizeLong == other.SizeLong && GroupId.Equals(other.GroupId) && Duration.Equals(other.Duration) && FrameSizeInt == other.FrameSizeInt && string.Equals(Format, other.Format) && string.Equals(AudioFormat, other.AudioFormat) && string.Equals(AudioChannel, other.AudioChannel) && AudioSampleRate == other.AudioSampleRate && BitRateKbs == other.BitRateKbs && Fps.Equals(other.Fps);
		}
		public bool EqualsButSize(DuplicateItemViewModel other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return GroupId.Equals(other.GroupId) && Duration.Equals(other.Duration) && FrameSizeInt == other.FrameSizeInt && string.Equals(Format, other.Format) && string.Equals(AudioFormat, other.AudioFormat) && string.Equals(AudioChannel, other.AudioChannel) && AudioSampleRate == other.AudioSampleRate && BitRateKbs == other.BitRateKbs && Fps.Equals(other.Fps);
		}
		public bool EqualsButQuality(DuplicateItemViewModel other) {
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return GroupId.Equals(other.GroupId);
		}

		public override bool Equals(object obj) {
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != GetType()) return false;
			return Equals((DuplicateItemViewModel)obj);
		}

		public override int GetHashCode() {
			unchecked {
				var hashCode = SizeLong.GetHashCode();
				hashCode = (hashCode * 397) ^ GroupId.GetHashCode();
				hashCode = (hashCode * 397) ^ Duration.GetHashCode();
				hashCode = (hashCode * 397) ^ FrameSizeInt;
				hashCode = (hashCode * 397) ^ (Format != null ? Format.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (AudioFormat != null ? AudioFormat.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ (AudioChannel != null ? AudioChannel.GetHashCode() : 0);
				hashCode = (hashCode * 397) ^ AudioSampleRate;
				hashCode = (hashCode * 397) ^ BitRateKbs.GetHashCode();
				hashCode = (hashCode * 397) ^ Fps.GetHashCode();
				return hashCode;
			}
		}
	}
}
