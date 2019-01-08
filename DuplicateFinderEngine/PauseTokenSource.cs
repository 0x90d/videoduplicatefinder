using System.Threading;

namespace DuplicateFinderEngine
{
    /// <summary>
    /// Implementation of PauseTokenSource pattern based on the blog post: 
    /// http://blogs.msdn.com/b/pfxteam/archive/2013/01/13/cooperatively-pausing-async-methods.aspx 
    /// </summary>
    public class PauseTokenSource
    {
        private int m_paused;
        public bool IsPaused
        {
            get => m_paused != 0;
            set
            {
                if (value)
                {
                    Interlocked.Exchange(ref m_paused, 1);
                }
                else
                {
                    Interlocked.Exchange(ref m_paused, 0);
                }
            }
        }


    }
}
