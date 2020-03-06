using System;
using System.Collections.Generic;
using System.Text;

namespace Chat.Data
{
    public class DataModel
    {
        #pragma warning disable IDE1006 // Naming Styles
        public int event_type { get; set; }
        public int time_stamp { get; set; }
        public string content { get; set; }
        public int id { get; set; }
        public int user_id { get; set; }
        public string user_name { get; set; }
        public int room_id { get; set; }
        public string room_name { get; set; }
        public int message_id { get; set; }
        public int parent_id { get; set; }
        #pragma warning restore IDE1006 // Naming Styles
    }
}
