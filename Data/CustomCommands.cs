using System;
using System.Collections.Generic;
using System.Text;

namespace Chat.Data
{
    class CustomCommands
    {
        public string command { get; set; }
        public string response { get; set; }
        public string createdBy { get; set; }
        public int createdById { get; set; }
        public DateTime createdTime { get; set; }
    }
}
