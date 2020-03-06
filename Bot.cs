using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using SharpExchange.Auth;
using SharpExchange.Chat;
using SharpExchange.Chat.Events;
using SharpExchange.Chat.Actions;
using SharpExchange.Chat.Events.User.Extensions;
using SharpExchange.Net.WebSockets;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Chat.Data;
using System.Threading;

namespace Chat
{
    public static class Bot
    {
        private static List<ActionScheduler> _actionScheduler = new List<ActionScheduler>();
        private static List<RoomWatcher<DefaultWebSocket>> _roomWatcher = new List<RoomWatcher<DefaultWebSocket>>();

        private static readonly bool                  debug             = false;                       //Debug mode
        private static readonly string                username          = "@JackSparrow";              //The bots username
        private static readonly string                commandPhraseName = "Jack, ";                    //The phrase with the name
        private static readonly string                commandPhrase     = "!!";                        //The command phrase, currerently unused
        private static readonly string                commandsFile      = "commands.json";             //Set to whatever the commands json file is
        private static readonly string                mindJailFile      = "mindjail.json";             //Set to whatever the mindjail json file is
        private static readonly int                   SaveTimeDiff      = 1;                           //Set in hours
        private static readonly Dictionary<int, bool> Listening         = new Dictionary<int, bool>(); //List of rooms that the current instance of the bot is listening to

        private static HashSet<CustomCommands> _customCommands = new HashSet<CustomCommands>();
        private static HashSet<MindJailModel> _mindJail = new HashSet<MindJailModel>();
        private static List<string> _errorResponse = new List<string>
        {
            "I don't understand mate",
            "....Come Again?",
            "Is that even English?",
            "https://media3.giphy.com/media/1oJLpejP9jEvWQlZj4/giphy.gif",
        };
        private static List<string> _shutUp = new List<string>
        {
            "SHHH!",
            "Shut It!",
            "Quiet you!",
            "https://media1.giphy.com/media/KUOPgSNoKVcuQ/giphy.gif",
            "https://media3.giphy.com/media/iHskdY9SMLFZuQ2u5c/giphy.gif",
            "https://media3.giphy.com/media/LiRoVoHjMa5bO/giphy.gif"
        };
        private static List<string> _hardCommands = new List<string>()
        {
            "learn",
            "tell",
            "echo",
            "save",
            "info",
            "forget",
            "ban",
            "unban"
        };
        private static List<string> _afkList = new List<string>();
        private static DateTime _lastSave;
        private static List<string> _salutations = new List<string>()
        {
            "hey",
            "hi",
            "hello",
            "ahoy",
            "avast",
        };
        private static readonly List<RoomModel> url = new List<RoomModel>()
        {
            new RoomModel() { roomid = 1, url = "https://chat.stackoverflow.com/rooms/1/sandbox",  },
            new RoomModel() { roomid = 7, url = "https://chat.stackoverflow.com/rooms/7/c" }
        };

        private static TimeSpan oneBox = new TimeSpan(0, 2, 0);

        public static async Task init(EmailAuthenticationProvider auth)
        {
            try
            {
                //Read from the commands file
                using (StreamReader r = new StreamReader(commandsFile))
                {
                    var json = r.ReadToEnd();
                    var cmds = JsonConvert.DeserializeObject<HashSet<CustomCommands>>(json);
                    _customCommands = cmds;
                }

                //Read from the mindjail file
                using (StreamReader r = new StreamReader(mindJailFile))
                {
                    var json = r.ReadToEnd();
                    var jailedUsers = JsonConvert.DeserializeObject<HashSet<MindJailModel>>(json);
                    _mindJail = jailedUsers;
                }

                foreach(var u in url)
                {
                    var ass = new ActionScheduler(auth, u.url);
                    var roomWatch = new RoomWatcher<DefaultWebSocket>(auth, u.url);

                    if (debug)
                    {
                        Console.WriteLine("DEBUG IS ON");
                        var debugDataHandler = new AllData();
                        debugDataHandler.OnEvent += data => Console.WriteLine(data);
                        roomWatch.EventRouter.AddProcessor(debugDataHandler);
                    }

                    //Register our global exception handler
                    System.AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

                    var chatMessage = new ChatMessage();
                    chatMessage.OnEvent += async data => await ChatMessageHandlerAsync(data);
                    roomWatch.EventRouter.AddProcessor(chatMessage);

                    _actionScheduler.Add(ass);
                    _roomWatcher.Add(roomWatch);
                }

                Console.WriteLine("I've woken up");

                while (Console.ReadKey(true).Key != ConsoleKey.Q) { }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
                Console.ReadLine();
            }
        }

        static async Task ChatMessageHandlerAsync(string data)
        {
            try
            {
                string incomingMsg;
                var model = JsonConvert.DeserializeObject<DataModel>(data);

                incomingMsg = model.content.ToLower();
                string originalIncomingMsg = model.content;
                string usernameTL = username.ToLower();
                string cmdPhraseTL = commandPhraseName.ToLower();
                bool triggered = false;

                //TODO make this a value passed from program.cs
                if (model.user_id == 12265685)
                    return;

                bool opPriv = false;
                User user = new User("stackoverflow.com", model.user_id);
                if (user.IsModerator || user.Owns.Any(x => x.RoomId == 7))
                    opPriv = true;

                if (incomingMsg.StartsWith(usernameTL) || incomingMsg.StartsWith(cmdPhraseTL))
                {
                    incomingMsg = incomingMsg.Replace(usernameTL, "", true, CultureInfo.CurrentCulture).Trim();
                    originalIncomingMsg = originalIncomingMsg.Replace(username, "", true, CultureInfo.CurrentCulture).Trim();
                    incomingMsg = incomingMsg.Replace(cmdPhraseTL, "", true, CultureInfo.CurrentCulture).Trim();
                    originalIncomingMsg = originalIncomingMsg.Replace(commandPhraseName, "", true, CultureInfo.CurrentCulture).Trim();
                    incomingMsg = System.Web.HttpUtility.HtmlDecode(incomingMsg);
                    originalIncomingMsg = System.Web.HttpUtility.HtmlDecode(originalIncomingMsg);

                    triggered = true;
                }

                var ass = _actionScheduler.FirstOrDefault(x => x.RoomId == model.room_id);
                var roomWatcher = _roomWatcher.FirstOrDefault(x => x.RoomId == model.room_id);

                var currentRoom = url.FirstOrDefault(x => x.roomid == model.room_id);
                bool setTo = false; bool isSet = false;

                if(opPriv)
                {
                    //Check if the command is listen/deafen
                    if (incomingMsg.StartsWith("listen"))
                    {
                        setTo = true;
                        isSet = true;
                    }
                    else if (incomingMsg.StartsWith("deafen"))
                    {
                        isSet = true;
                    }
                }

                if (isSet)
                {
	                url.FirstOrDefault(x => x.roomid == currentRoom.roomid).currentlyListening = setTo;
                    await Say($"I'm {(setTo ? "now" : "no longer")} listening for commands in this room", ass);
                    return;
                }

                //Check if we should be listening in this room at all
                if (currentRoom.currentlyListening == false)
                    return;

                //Tell feeds to stfu
                if (model.user_id == -2)
                {
                    await SayShutUp(ass, messageId: model.message_id);
                    return;
                }

                //Check if the user talking is in mind jail
                var mindJailUser = _mindJail.FirstOrDefault(x => x.id == model.user_id);
                if (mindJailUser != null)
                {
                    if(!mindJailUser.MindJailInform)
                    {
                        if(incomingMsg.StartsWith(usernameTL))
                        {
                            await Reply($"Don't ye speak me name again, ye lily-livered scabby sea bass!! (You've been banned from using me.)", model.message_id, ass);
                            mindJailUser.MindJailInform = true;
                        }
                    }
                    return;
                }


                if(triggered) 
                {
                    if (incomingMsg.StartsWith("learn"))
                    {
                        incomingMsg = originalIncomingMsg.Replace("learn", "", true, CultureInfo.CurrentCulture).Trim();
                        string[] args = incomingMsg.Split(" ");
                        if (!string.IsNullOrWhiteSpace(incomingMsg) && args.Length != 1)
                        {
                            if (string.IsNullOrWhiteSpace(args[0]) ||
                                                     args[0] == "​" ||        //needed as thats actually a zero width space. Fuck you Mike <3
                                                     args[0].Contains("‮"))   //needed as thats actually the RTL override.   Fuck you Mike <3
                                await SayError(ass);
                            else if (args[0].Length > 16)
                                await Say("That command be far too long", ass);
                            else
                            {
                                if(_customCommands.FirstOrDefault(x => x.command.ToLower() == args[0].ToLower()) == null)
                                {
                                    incomingMsg = incomingMsg.Replace(args[0], "");

                                    string response = incomingMsg;
                                    //if (args.Length >= 3)
                                    //{
                                    //    var regex = new Regex("\"(.*?)\"");
                                    //    var match = regex.Match(incomingMsg);
                                    //    if (match.Success)
                                    //    {
                                    //        response = match.Groups[1].ToString();
                                    //    }
                                    //    else
                                    //    {
                                    //        response = "test2";
                                    //    }
                                    //}
                                    //else response = args[1];
                                    
                                    _customCommands.Add(new CustomCommands()
                                    {
                                        command = args[0],
                                        response = response,
                                        createdTime = DateTime.Now,
                                        createdBy = model.user_name,
                                        createdById = model.user_id
                                    });
                                    await Say($"I've learned the command {args[0]}", ass);
                                    await SaveCommands(ass);
                                }
                                else
                                {
                                    await Reply($"That command already exists mate", model.message_id, ass);
                                }
                            }
                        }
                        else await SayError(ass);
                    }
                    else if (incomingMsg.StartsWith("tell"))
                    {
                        incomingMsg = originalIncomingMsg.Replace("tell", "", true, CultureInfo.CurrentCulture).Trim();
                        string[] args = incomingMsg.Split(" ");
                        if (!string.IsNullOrWhiteSpace(incomingMsg) && !incomingMsg.StartsWith("@"))
                        {
                            if (incomingMsg.EndsWith("shutup"))
                            {
                                incomingMsg = incomingMsg.Replace("shutup", "");
                                await SayShutUp(ass, user: incomingMsg);
                            }
                            else
                            {
                                var command = _customCommands.FirstOrDefault(x => incomingMsg.EndsWith((string) x.command));
                                if (command != null)
                                {
                                    string response = await ChatResponse(command.response);
                                    incomingMsg = incomingMsg.Replace(command.command, "");
                                    await Say($"@{incomingMsg} {response}", ass);
                                }
                                else await SayError(ass);
                            }
                        }
                        else if (incomingMsg.StartsWith("@"))
                        {
                            await Reply("https://i.imgur.com/DOqYuo5.png", model.message_id, ass);
                        }
                        else await SayError(ass);
                    }
                    else if (incomingMsg.StartsWith("echo"))
                    {
                        incomingMsg = originalIncomingMsg.Replace("echo", "", true, CultureInfo.CurrentCulture).Trim();
                        if (string.IsNullOrWhiteSpace(incomingMsg))
                            await SayError(ass);
                        else
                            await Say(incomingMsg, ass);
                    }
                    else if (incomingMsg.StartsWith("save"))
                    {
                        if (opPriv)
                            await SaveCommands(ass, true, true);
                        else
                            await Reply("You don't have permission to do that", model.message_id, ass);
                    }
                    else if (incomingMsg.StartsWith("commands"))
                    {
                        string commands = "I know the following commands: commands";
                        foreach (var c in _hardCommands)
                            commands += $", {c}";
                        foreach (var c in _customCommands)
                            commands += $", {c.command}";
                        await Say(commands, ass);
                    }
                    else if (incomingMsg.StartsWith("info"))
                    {
                        incomingMsg = originalIncomingMsg.Replace("info ", "", true, CultureInfo.CurrentCulture).Trim();
                        var hardCommand = _hardCommands.FirstOrDefault(x => x == incomingMsg);
                        if (hardCommand != null)
                        {
                            await Reply($"Command {incomingMsg}, Created when time first began", model.message_id, ass);
                        }
                        else
                        {
                            var command = _customCommands.FirstOrDefault(x => x.command == incomingMsg);
                            if (command != null)
                            {
                                await Reply($"Command {command.command}, Created by {command.createdBy} on {command.createdTime:dd-MM-yyyy} at {command.createdTime:HH:mm}", model.message_id, ass);
                            }
                            else await Reply($"There's no command called {incomingMsg}, Savvy?", model.message_id, ass);
                        }
                    }
                    else if (incomingMsg.StartsWith("forget"))
                    {
                        incomingMsg = originalIncomingMsg.Replace("forget ", "", true, CultureInfo.CurrentCulture).Trim();
                        var hardCommands = _hardCommands.FirstOrDefault(x => x.ToLower() == incomingMsg.ToLower());
                        if (hardCommands == null)
                        {
                            var command = _customCommands.FirstOrDefault(x => x.command.ToLower() == incomingMsg.ToLower());
                            if (command != null)
                            {
                                if (command.createdById == model.user_id || opPriv)
                                {
                                    _customCommands.RemoveWhere(x => x.command.ToLower() == incomingMsg.ToLower());
                                    await Say($"Command {incomingMsg} has been forgotten", ass);
                                }
                                else await Reply("You don't have permission to do that", model.message_id, ass);
                            }
                            else await Reply($"There's no command called {incomingMsg}, Savvy?", model.message_id, ass);
                        }
                        else await Reply($"You can't do that mate", model.message_id, ass);
                    }
                    else if (incomingMsg.StartsWith("ban"))
                    {
                        if(opPriv)
                        {
                            incomingMsg = originalIncomingMsg.Replace("ban", "", true, CultureInfo.CurrentCulture).Trim();
                            if (int.TryParse(incomingMsg, out var banUserId))
                            {
                                User banUser = new User("stackoverflow.com", banUserId);
                                var banUserRoom = banUser.Owns.ToList().Where(x => x.RoomId == 7).FirstOrDefault();
                                if (!banUser.IsModerator && banUserRoom == null)
                                {
                                    _mindJail.Add(new MindJailModel() { id = banUserId });
                                    await Say($"Added {banUser.Username} to ban list", ass);
                                    await SaveMindJail();
                                }
                                else await Reply("You cannot ban that user", model.message_id, ass);
                            }
                        }
                        else await Reply("You don't have permission to do that", model.message_id, ass);
                    }
                    else if(incomingMsg.StartsWith("unban"))
                    {
                        if (opPriv)
                        {
                            incomingMsg = originalIncomingMsg.Replace("unban", "", true, CultureInfo.CurrentCulture).Trim();
                            if (int.TryParse(incomingMsg, out var banUserId))
                            {
                                var banUser = _mindJail.FirstOrDefault(x => x.id == banUserId);
                                if(banUser != null)
                                {
                                    User bannedUser = new User("stackoverflow.com", banUserId);
                                    _mindJail.RemoveWhere(x => x.id == banUserId);
                                    await Say($"Removed {bannedUser.Username} from ban list", ass);
                                    await SaveMindJail();
                                }
                            }
                            else await Reply("That user isn't banned", model.message_id, ass);
                        }
                        else await Reply("You don't have permission to do that", model.message_id, ass);
                    }
                    else
                    {
                        var command = _customCommands.FirstOrDefault(x => x.command.ToLower() == incomingMsg.ToLower());
                        if (command != null)
                        {
                            string response = await ChatResponse(command.response);

                            await Say(response, ass);
                        }
                        else await SayError(ass);
                    }
                }
                //else if(incomingMsg.StartsWith(commandPhrase))
                //{

                //}
                else
                {
                    switch (incomingMsg)
                    {
                        case "wat":
                            await Say("https://i.kym-cdn.com/photos/images/newsfeed/000/173/576/Wat8.jpg", ass);
                            break;
                        case "stop":
                            await Say("HAMMERTIME!", ass);
                            break;
                        case "sthap":
                            await Say("HAMMAHTIME!", ass);
                            break;
                        case "halt":
                            await Say("HAMMERZIET!", ass);
                            break;
                        case "стоп":
                            await Say("Время Молота!", ass);
                            break;
                        default:
                            //Do Nothing
                            break;
                    }
                }

                await CheckAutoBackup(ass);
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        public static async Task<string> ChatResponse(string response)
        {
            if (response.StartsWith("<>"))
                response = response.Replace("<>", "");
            else if (response.StartsWith(" <>"))
                response = response.Replace(" <>", "");
            else if (response.StartsWith("http"))
                response = $"[{response}]({response})";

            return response;
        }

        /// <summary>
        /// Saves the current contents of mindjail to the mindjail.json file
        /// </summary>
        /// <returns></returns>
        public static async Task SaveMindJail()
        {
            string mindJail = JsonConvert.SerializeObject(_mindJail);
            File.WriteAllText(mindJailFile, mindJail);
        }

        /// <summary>
        /// Checks the current time and the last time the commands were saved & backedup
        /// </summary>
        /// <returns></returns>
        public static async Task CheckAutoBackup(ActionScheduler ass)
        {
            if (_lastSave == DateTime.MinValue)
            {
                _lastSave = DateTime.Now;
                await SaveCommands(ass, true, false);
            }
            else
            {
                if (_lastSave.AddHours(SaveTimeDiff) <= DateTime.Now)
                {
                    await SaveCommands(ass, true, false);
                    _lastSave = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// Saves the custom commands
        /// </summary>
        /// <param name="backup"></param>
        /// <param name="outputSave"></param>
        /// <returns></returns>
        public static async Task SaveCommands(ActionScheduler ass, bool backup = false, bool outputSave = false)
        {
            string commandJson = JsonConvert.SerializeObject(_customCommands);
            File.WriteAllText(commandsFile, commandJson);

            if (backup)
                await BackupCommands(commandJson, outputSave, ass);

            Console.WriteLine($"Commands saved at {DateTime.Now:dd-MM-yyyy HH:mm}");
        }

        /// <summary>
        /// Saves a backup of the custom commands
        /// </summary>
        /// <param name="commandJson"></param>
        /// <param name="outputSave"></param>
        /// <returns></returns>
        public static async Task BackupCommands(string commandJson, bool outputSave, ActionScheduler ass)
        {
            File.WriteAllText($"{DateTime.Now:dd-MM-YYYY-HHmmss}{commandsFile}", commandJson);
            if(outputSave)
                await Say($"I've saved meself", ass);
        }

        /// <summary>
        /// Handles any and all unhandled Exceptions
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            //Say($"Help, I've fallen over and i can't get up");
            Console.WriteLine(e.ExceptionObject.ToString());
        }

        /// <summary>
        /// Outputs a message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static async Task Say(string message, ActionScheduler ass)
        {
            int _messageid = 0;
            bool isImage = false;
            Regex regex = new Regex(@"^(jpeg|jpg|png|gif|bmp)$");
            var extension = message.Substring(message.LastIndexOf('.') + 1).ToLower();
            var check = regex.Match(extension);
            if (check.Success && !extension.Contains("?"))
                isImage = true;

            if (!string.IsNullOrWhiteSpace(message))
                _messageid = await ass.CreateMessageAsync(message);

            if(isImage && _messageid != -1)
            {
                _ = Task.Factory.StartNew(async () =>
                  {
                      await Task.Delay(oneBox);
                      await EditMessage(_messageid, message, ass, addition: "...");
                  });
            }
        }
        
        /// <summary>
        /// Will Reply to any messageId passed
        /// </summary>
        /// <param name="message"></param>
        /// <param name="messageId"></param>
        /// <returns></returns>
        public static async Task Reply(string message, int messageId, ActionScheduler ass)
        {
            int _messageid = 0;
            bool isImage = false;
            Regex regex = new Regex("/\\.(jpe?g|png|gif|bmp)$/i");
            var extension = message.Substring(message.LastIndexOf('.') + 1);
            var check = regex.Match(extension);
            if (check.Success && !extension.Contains("?"))
                isImage = true;

            if (!string.IsNullOrWhiteSpace(message) && messageId != 0)
                _messageid = await ass.CreateReplyAsync(message, messageId);

            if (isImage && _messageid != -1)
            {
                _ = Task.Factory.StartNew(async () =>
                {
                    await Task.Delay(oneBox);
                    await EditMessage(_messageid, message, ass, addition: "...");
                });
            }
        }

        /// <summary>
        /// Edits the message with the relevant messageid
        /// Content: will edit the content to the value passed
        /// Addition: will add the text passed onto the end of the relvant message
        /// </summary>
        /// <param name="messageid"></param>
        /// <param name="content"></param>
        /// <param name="addition"></param>
        /// <returns></returns>
        public static async Task EditMessage(int messageid, string message, ActionScheduler ass, string content = null, string addition = null)
        {
            string newMessage = content;
            if(addition != null)
            {
                newMessage = $"{message} {addition.Trim()}";
            }

            bool success = await ass.EditMessageAsync(messageid, newMessage);
            if (!success)
            {
                _ = Task.Factory.StartNew(async () =>
                {
                    Thread.Sleep(new TimeSpan(0, 0, 10));
                    await EditMessage(messageid, message, ass, content, addition);
                });
            }
        }

        /// <summary>
        /// Will output a custom error message when there is no relative command
        /// </summary>
        /// <param name="messageId"></param>
        /// <returns></returns>
        public static async Task SayError(ActionScheduler ass, int messageId = 0)
        {
            Random random = new Random();
            int index = random.Next(0, _errorResponse.Count);

            string message = "";
            if (messageId > 0)
                message = $":{messageId.ToString()} ";

            await Say($"{message}{_errorResponse[index].ToString()}", ass);
        }

        /// <summary>
        /// Outputs a shutup message when passed either a messageId or a user(name)
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public static async Task SayShutUp(ActionScheduler ass, int messageId = 0, string user = null)
        {
            Random random = new Random();
            int index = random.Next(0, _shutUp.Count);

            string message = "";
            user = user?.Replace(" ", "");
            if (messageId > 0)
                message = $":{messageId.ToString()} ";
            else if (!string.IsNullOrWhiteSpace(user))
                message = $"@{user} ";

            if(message.StartsWith("@"))
            {
                List<string> replies = _shutUp.Where(x => !x.StartsWith("http")).ToList();
                await Say($"{message}{replies[index].ToString()}", ass);
            }
            else
                await Say($"{message}{_shutUp[index].ToString()}", ass);
        }
    }
}
