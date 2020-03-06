using System;
using System.Collections.Generic;
using System.Text;

namespace Chat.Data
{
    public class RoomModel
    {
        public RoomModel()
        {
            currentlyListening = true;
        }

        public string url { get; set; }
        public int roomid { get; set; }
        public bool currentlyListening { get; set; }
    }
}
