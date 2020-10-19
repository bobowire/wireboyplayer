using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WireboyPlayer
{
    public class QueueThread
    {
        private ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private Task m_curTask = null;
        private object m_obj = new object();
        public void Excute(Action func)
        {
            queue.Enqueue(func);
            if (m_curTask == null)
            {
                lock (m_obj)
                {
                    if (m_curTask == null)
                    {
                        m_curTask = Task.Factory.StartNew(() => DoExcute());
                    }
                }
            }
        }
        protected virtual void DoExcute()
        {
            Thread.Sleep(500);
            do
            {
                if (!queue.IsEmpty && queue.TryDequeue(out Action func))
                {
                    func();
                }
            } while (queue.Count > 0);
            m_curTask = null;
        }
    }
}
