using System;
using System.Reflection;
using TikTokLiveSharp.Client;

class Program {
    static void Main() {
        try {
            var clientType = typeof(TikTokLiveClient);
            var onGiftEvent = clientType.GetEvent("OnGift");
            var handlerType = onGiftEvent.EventHandlerType;
            var invokeMethod = handlerType.GetMethod("Invoke");
            var eventArgsType = invokeMethod.GetParameters()[1].ParameterType;

            Console.WriteLine($"EventArgs type: {eventArgsType.FullName}");

            InspectType(eventArgsType);

            var giftField = eventArgsType.GetField("Gift");
            if (giftField != null) {
                Console.WriteLine($"\n--- Gift ({giftField.FieldType.Name}) Properties ---");
                InspectType(giftField.FieldType);
            }

            var senderField = eventArgsType.GetField("Sender") ?? eventArgsType.GetField("User");
            if (senderField != null) {
                Console.WriteLine($"\n--- {senderField.Name} ({senderField.FieldType.Name}) Properties ---");
                InspectType(senderField.FieldType);
            }
        } catch (Exception ex) {
            Console.WriteLine(ex.ToString());
        }
    }

    static void InspectType(Type type) {
        Console.WriteLine($"Type: {type.FullName}");
        Console.WriteLine("  Properties:");
        foreach(var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)) {
            Console.WriteLine($"    {prop.Name} ({prop.PropertyType.Name})");
        }
        Console.WriteLine("  Fields:");
        foreach(var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy)) {
            Console.WriteLine($"    {field.Name} ({field.FieldType.Name})");
        }
    }
}
