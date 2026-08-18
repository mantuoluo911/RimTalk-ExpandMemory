using System;

namespace RimTalk.Memory.UI
{
    public class Cursor
    {
        public int CursorTick;
        private MemoryChronicle _chronoicle;
        private MemoryTimelineView _timeline;

        public Cursor() { }
        public Cursor(MemoryChronicle chronoicle, MemoryTimelineView timeline)
        {
            _chronoicle = chronoicle;
            _timeline = timeline;
        }
        
        internal void Initialize(MemoryChronicle chronoicle, MemoryTimelineView timeline)
        {
            _chronoicle = chronoicle;
            _timeline = timeline;
        }

        public void KeepCursorVisible()
        {
            _chronoicle.KeepCursorVisible();
        }
    }
}
