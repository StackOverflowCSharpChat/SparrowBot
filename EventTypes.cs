using Newtonsoft.Json.Linq;
using SharpExchange.Chat.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace Chat
{
    public class AllData : ChatEventDataProcessor, IChatEventHandler<string>
    {
        // The type of events we want to process.
        private EventType[] events = new[] { EventType.All };

        public override EventType[] Events => events;

        public event Action<string> OnEvent;

        // Process the incoming JSON data coming from the RoomWatcher's
        // WebSocket. In this example, we just stringify the object and
        // invoke any listeners.
        public override void ProcessEventData(EventType _, JToken data) => OnEvent?.Invoke(data.ToString());
    }

    public class ChatMessage : ChatEventDataProcessor, IChatEventHandler<string>
    {
        // The type of events we want to process.
        private EventType[] events = new[] { EventType.MessagePosted };

        public override EventType[] Events => events;

        public event Action<string> OnEvent;

        // Process the incoming JSON data coming from the RoomWatcher's
        // WebSocket. In this example, we just stringify the object and
        // invoke any listeners.
        public override void ProcessEventData(EventType _, JToken data) => OnEvent?.Invoke(data.ToString());
    }
}
