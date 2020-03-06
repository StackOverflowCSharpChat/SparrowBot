using SharpExchange.Auth;
using System.Threading.Tasks;
using System.Collections.Generic;
using Chat;
using Chat.Data;

public class Program
{
    private static void Main(string[] args) => Init().Wait();

	private static async Task Init()
	{
        // TODO Change login details, login url & roomURL to config file
        var auth = new EmailAuthenticationProvider("", "");

        _ = auth.Login("stackoverflow.com");

        _ = Bot.init(auth);
	}
}
