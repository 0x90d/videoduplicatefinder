using System.Collections;
using System.Collections.Generic;

namespace VideoDuplicateFinderLinux
{
    public sealed class DuplicateItemComparer : IComparer<DuplicateFinderEngine.Data.DuplicateItem>
    {
        private readonly CaseInsensitiveComparer caseiComp = new CaseInsensitiveComparer();

        public int Compare(DuplicateFinderEngine.Data.DuplicateItem x, DuplicateFinderEngine.Data.DuplicateItem y)
        {
            var vExt = caseiComp.Compare(x.GroupId, y.GroupId);
            return vExt != 0 ? vExt : caseiComp.Compare(x.Path, y.Path);
        }
    }
}
