using System.Collections;
using System.Collections.Generic;

namespace VideoDuplicateFinderLinux
{
    public sealed class DuplicateItemComparer : IComparer<DuplicateItemViewModel>
    {
        static readonly CaseInsensitiveComparer caseiComp = new CaseInsensitiveComparer();

        public int Compare(DuplicateItemViewModel x, DuplicateItemViewModel y) {
	        if (x.GroupId == y.GroupId) {
		        if (x.IsGroupHeader && y.IsGroupHeader) return 0;
		        if (x.IsGroupHeader) return -1;
		        if (y.IsGroupHeader) return 1;
				return caseiComp.Compare(x.Path, y.Path);
	        }
	        return caseiComp.Compare(x.GroupId, y.GroupId);
			
        }
    }
}
