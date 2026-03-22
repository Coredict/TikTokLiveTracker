// See https://aka.ms/new-console-template for more information
using System;
using System.Threading.Tasks;
using TikTokLiveSharp.Client;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Testing TikTokLive_Sharp...");
        var client = new TikTokLiveClient(uniqueID: "stmester3.0"); // known live user

        client.OnConnected += (sender, e) => Console.WriteLine($"Connected: {e}");
        client.OnDisconnected += (sender, e) => Console.WriteLine($"Disconnected: {e}");
        client.OnGift += (sender, e) => Console.WriteLine($"GIFT: {e.Gift.Name}");

        try
        {
            await client.RunAsync(new System.Threading.CancellationToken());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex}");
        }
    }
}
