using ReactiveUI;
using System;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace VideoDuplicateFinderLinux
{
    public class DuplicateItemViewModel : ReactiveObject, IEquatable<DuplicateItemViewModel>
    {
        public DuplicateItemViewModel(DuplicateFinderEngine.Data.DuplicateItem file)
        {
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
				if (IsGroupHeader) return;
				Thumbnail = Utils.JoinImages(file.Thumbnail);
				this.RaisePropertyChanged(nameof(Thumbnail));
			};
        }

		/*
		 * There is no grouping in Avalonia yet. This workaround turns a VM into a group header
		 */
		public DuplicateItemViewModel(string groupTitle, Guid duplicateGroupGuid) {
	        IsGroupHeader = true;
	        GroupHeaderTitle = groupTitle;
	        GroupId = duplicateGroupGuid;
		}
		public string GroupHeaderTitle { get; }
        public bool IsGroupHeader { get; }



		[DisplayName("Path")]
        public string Path { get; }

        public Bitmap Thumbnail { get; set; }

        public long SizeLong { get; }
		
		public IBrush SizeForeground { get; set; }

		[DisplayName("Size")]
        public string Size { get; }

        public float Similarity { get; }

		public bool IsImage { get; }

		public string Folder { get; }
		
        public IBrush BitRateForeground { get; set; }

        [DisplayName("Group Id")]
        public Guid GroupId { get; set; }
        [DisplayName("Duration")]
        public TimeSpan Duration { get; }

		public IBrush DurationForeground { get; set; }

		[DisplayName("Frame Size")]
        public string FrameSize { get; }
        public int FrameSizeInt { get; }

		public IBrush FrameSizeForeground { get; set; }

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


        [DisplayName("Fps")]
        public float Fps { get; }

        [DisplayName("Date Created")]
        public DateTime DateCreated { get; }

        private bool _Checked;
        public bool Checked
        {
            get => _Checked;
            set => this.RaiseAndSetIfChanged(ref _Checked, value);
        }

        public bool Equals(DuplicateItemViewModel other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return SizeLong == other.SizeLong && GroupId.Equals(other.GroupId) && Duration.Equals(other.Duration) && FrameSizeInt == other.FrameSizeInt && string.Equals(Format, other.Format) && string.Equals(AudioFormat, other.AudioFormat) && string.Equals(AudioChannel, other.AudioChannel) && AudioSampleRate == other.AudioSampleRate && BitRateKbs == other.BitRateKbs && Fps.Equals(other.Fps);
        }
        public bool EqualsButSize(DuplicateItemViewModel other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return GroupId.Equals(other.GroupId) && Duration.Equals(other.Duration) && FrameSizeInt == other.FrameSizeInt && string.Equals(Format, other.Format) && string.Equals(AudioFormat, other.AudioFormat) && string.Equals(AudioChannel, other.AudioChannel) && AudioSampleRate == other.AudioSampleRate && BitRateKbs == other.BitRateKbs && Fps.Equals(other.Fps);
        }
        public bool EqualsButQuality(DuplicateItemViewModel other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return GroupId.Equals(other.GroupId);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((DuplicateItemViewModel)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
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
