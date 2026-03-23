using System;
using System.Reflection;
using TikTokLiveSharp.Client;
using TikTokLiveSharp.Events;

class Program {
    static void Main() {
        try {
            Console.WriteLine("--- GiftEventArgs ---");
            InspectType(typeof(GiftEventArgs));
            
            var giftField = typeof(GiftEventArgs).GetProperty("Gift") ?? (object)typeof(GiftEventArgs).GetField("Gift") as PropertyInfo != null ? typeof(GiftEventArgs).GetProperty("Gift") : null;
            // Accessing via property or field
            
            Console.WriteLine("\n--- TikTokLiveSharp.Models.User ---");
            // Assuming the sender is of this type
            var senderProp = typeof(GiftEventArgs).GetProperty("Sender");
            if (senderProp != null) InspectType(senderProp.PropertyType);
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }

    static void InspectType(Type type) {
        Console.WriteLine($"Type: {type.FullName}");
        foreach(var prop in type.GetProperties()) {
            Console.WriteLine($"  Property: {prop.Name} ({prop.PropertyType.Name})");
        }
        foreach(var field in type.GetFields()) {
            Console.WriteLine($"  Field: {field.Name} ({field.FieldType.Name})");
        }
    }
}
