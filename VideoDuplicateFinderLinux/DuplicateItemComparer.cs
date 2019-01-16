using System.Collections;
using System.Collections.Generic;

namespace VideoDuplicateFinderLinux
{
    public sealed class DuplicateItemComparer : IComparer<DuplicateItemViewModel>
    {
        private readonly CaseInsensitiveComparer caseiComp = new CaseInsensitiveComparer();

        public int Compare(DuplicateItemViewModel x, DuplicateItemViewModel y)
        {
            var vExt = caseiComp.Compare(x.GroupId, y.GroupId);
            return vExt != 0 ? vExt : caseiComp.Compare(x.Path, y.Path);
        }
    }
}
