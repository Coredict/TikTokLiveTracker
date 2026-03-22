using TikTokLiveSharp.Models.Protobuf.Messages;

namespace TikTokLiveSharp.Events.Objects;

public class TikTokGift
{
	public delegate void TikTokGiftEventHandler<TEventArgs>(TikTokGift gift, TEventArgs args);

	public delegate void TikTokGiftChangedEventHandler(TikTokGift gift, long change, long newAmount);

	public readonly Gift Gift;

	public readonly User Sender;

	public readonly User Receiver;

	public long Amount { get; private set; }

	public bool StreakFinished { get; private set; }

	public event TikTokGiftChangedEventHandler OnAmountChanged;

	public event TikTokGiftEventHandler<long> OnStreakFinished;

	public TikTokGift(TikTokLiveSharp.Models.Protobuf.Messages.GiftMessage message)
	{
		Gift = message?.Gift;
		Sender = message?.User;
		Amount = message?.ComboCount ?? 0;
		if (Gift.IsStreakable)
		{
			StreakFinished = message != null && message.RepeatEnd == 1;
		}
		else
		{
			StreakFinished = true;
		}
		Receiver = message?.ToUser;
	}

	internal virtual void FinishStreak()
	{
		StreakFinished = true;
		this.OnStreakFinished?.Invoke(this, Amount);
	}

	internal void UpdateGiftAmount(long amount)
	{
		if (amount > Amount)
		{
			long change = amount - Amount;
			Amount = amount;
			this.OnAmountChanged?.Invoke(this, change, Amount);
		}
	}
}
