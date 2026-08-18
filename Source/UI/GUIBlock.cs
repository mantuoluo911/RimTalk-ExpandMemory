using System;
using UnityEngine;

namespace RimTalk.Memory.UI
{
    public struct GUIBlock : IDisposable
    {
        private Color _color;
        private bool _enabled;

        public GUIBlock() => Record();
        public GUIBlock(Color color)
        {
            Record();
            GUI.color = color;
        }
        public GUIBlock(bool enabled)
        {
            Record();
            GUI.enabled = enabled;
        }
        public GUIBlock(Color color, bool enabled)
        {
            Record();
            GUI.color = color;
            GUI.enabled = enabled;
        }

        private void Record()
        {
            _color = GUI.color;
            _enabled = GUI.enabled;
        }

        public void Dispose()
        {
            GUI.color = _color;
            GUI.enabled = _enabled;
        }
    }
}
